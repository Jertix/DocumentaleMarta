using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DocumentaleMarta.Tests;

/// <summary>Le note di completamento di una cartella nel servizio: ci sono solo finché la cartella è completata.</summary>
public class NoteCompletamentoServiceTests : IDisposable
{
    private static readonly DateOnly Oggi = new(2026, 10, 9);

    private readonly ArchivioDiProva _a = new();

    public void Dispose() => _a.Dispose();

    private async Task<CartellaDettaglio> NuovaAsync() => await _a.CreaCartellaAsync("Fatture", "Fattura 1");

    [Fact]
    public async Task UnaCartellaNuova_NonHaNote()
    {
        var cartella = await NuovaAsync();

        Assert.Null(cartella.Dati.NoteCompletamento);
    }

    [Fact]
    public async Task CompletandoLaCartella_LeNoteSiSalvanoESiRileggono()
    {
        var cartella = await NuovaAsync();

        var salvata = await _a.Servizio.AggiornaCartellaAsync(
            cartella.Id, cartella.Dati with { Completato = true, DataCompletamento = Oggi, NoteCompletamento = "Pagata il 9/10.\nRicevuta allegata." });

        Assert.Equal("Pagata il 9/10.\nRicevuta allegata.", salvata.Dati.NoteCompletamento);
        Assert.Equal("Pagata il 9/10.\nRicevuta allegata.", (await _a.Servizio.CaricaCartellaAsync(cartella.Id))!.Dati.NoteCompletamento);
    }

    [Fact]
    public async Task ConLaCartellaDaCompletare_LeNoteNonSiSalvano()
    {
        var cartella = await NuovaAsync();

        var salvata = await _a.Servizio.AggiornaCartellaAsync(
            cartella.Id, cartella.Dati with { Completato = false, NoteCompletamento = "non dovrebbe restare" });

        Assert.Null(salvata.Dati.NoteCompletamento);
        Assert.Null((await _a.Servizio.CaricaCartellaAsync(cartella.Id))!.Dati.NoteCompletamento);
    }

    [Fact]
    public async Task RiaprendoLaCartella_LeNoteSparisconoCome_LaDataDiCompletamento()
    {
        var cartella = await NuovaAsync();
        var completata = await _a.Servizio.AggiornaCartellaAsync(
            cartella.Id, cartella.Dati with { Completato = true, DataCompletamento = Oggi, NoteCompletamento = "Fatto" });

        var riaperta = await _a.Servizio.AggiornaCartellaAsync(cartella.Id, completata.Dati with { Completato = false });

        Assert.Null(riaperta.Dati.NoteCompletamento);
        Assert.Null(riaperta.Dati.DataCompletamento);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n")]
    public async Task NoteVuoteOSoloSpazi_NonSiSalvano(string note)
    {
        var cartella = await NuovaAsync();

        var salvata = await _a.Servizio.AggiornaCartellaAsync(
            cartella.Id, cartella.Dati with { Completato = true, DataCompletamento = Oggi, NoteCompletamento = note });

        Assert.Null(salvata.Dati.NoteCompletamento);
    }

    [Fact]
    public async Task CreandoLaCartellaGiaCompletata_ConLeNote_LeConserva()
    {
        var areaId = await _a.Servizio.CreaAreaAsync("Fatture");

        var creata = await _a.Servizio.CreaCartellaConDatiAsync(
            areaId, new DatiCartella("Fattura 7", null, null, true, Oggi, Ricorrenza.Nessuna, "Chiusa in fase di registrazione"), []);

        Assert.Equal("Chiusa in fase di registrazione", creata.Dati.NoteCompletamento);
    }

    [Fact]
    public async Task RinominandoLaCartella_LeNoteRestano()
    {
        var cartella = await NuovaAsync();
        await _a.Servizio.AggiornaCartellaAsync(
            cartella.Id, cartella.Dati with { Completato = true, DataCompletamento = Oggi, NoteCompletamento = "Ok" });

        await _a.Servizio.RinominaCartellaAsync(cartella.Id, "Fattura 1 bis");

        var dopo = (await _a.Servizio.CaricaCartellaAsync(cartella.Id))!.Dati;
        Assert.Equal("Fattura 1 bis", dopo.Titolo);
        Assert.Equal("Ok", dopo.NoteCompletamento);
        Assert.True(dopo.Completato);
    }

    [Fact]
    public async Task ArchiviandoERipristinando_LeNoteRestano()
    {
        var cartella = await NuovaAsync();
        await _a.Servizio.AggiornaCartellaAsync(
            cartella.Id, cartella.Dati with { Completato = true, DataCompletamento = Oggi, NoteCompletamento = "Ok" });

        await _a.Servizio.ArchiviaCartellaAsync(cartella.Id);
        Assert.Equal("Ok", (await _a.Servizio.CaricaCartellaAsync(cartella.Id))!.Dati.NoteCompletamento);

        await _a.Servizio.RipristinaCartellaAsync(cartella.Id);
        Assert.Equal("Ok", (await _a.Servizio.CaricaCartellaAsync(cartella.Id))!.Dati.NoteCompletamento);
    }
}

/// <summary>Un archivio creato prima delle note di completamento (senza la colonna) si aggiorna senza perdere nulla.</summary>
public class NoteCompletamentoMigrazioneTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public async Task UnDatabaseVecchio_SiAggiorna_ELeCartelleCompletateNonHannoNote()
    {
        var percorso = _tmp.Combina("vecchio", "documentale.db");
        Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);

        await using (var vecchio = new AppDbContext(ArchivioDatabase.CreaOpzioni(percorso)))
        {
            vecchio.GetService<IMigrator>().Migrate("IconaArea");
            await vecchio.Database.ExecuteSqlRawAsync("INSERT INTO Aree (Id, Nome, PercorsoRelativo, Ordine) VALUES (1, 'Fatture', 'Fatture', 0)");
            await vecchio.Database.ExecuteSqlRawAsync(
                "INSERT INTO Cartelle (Id, AreaId, Titolo, Ricorrenza, Completato, DataCompletamento, Archiviata, PercorsoRelativo, DataCreazione) " +
                "VALUES (7, 1, 'Pagata', 0, 1, '2026-09-30', 0, 'Fatture/Pagata', '2026-09-01 10:00:00')");
        }

        await using (var nuovo = new AppDbContext(ArchivioDatabase.CreaOpzioni(percorso)))
            await nuovo.Database.MigrateAsync();

        await using var db = new AppDbContext(ArchivioDatabase.CreaOpzioni(percorso));
        var cartella = await db.Cartelle.SingleAsync();
        Assert.Equal("Pagata", cartella.Titolo);
        Assert.True(cartella.Completato);
        Assert.Equal(new DateOnly(2026, 9, 30), cartella.DataCompletamento);
        Assert.Null(cartella.NoteCompletamento);
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_NoteCompletamento"));

        // Ora la cartella completata può avere delle note.
        cartella.NoteCompletamento = "Pagata con bonifico";
        await db.SaveChangesAsync();
        await using var riletto = new AppDbContext(ArchivioDatabase.CreaOpzioni(percorso));
        Assert.Equal("Pagata con bonifico", (await riletto.Cartelle.SingleAsync()).NoteCompletamento);
    }
}

/// <summary>Le note di completamento nel form di una cartella (salvataggio automatico) e nella finestra «Nuova cartella».</summary>
public class NoteCompletamentoFormTests : IDisposable
{
    private readonly ArchivioDiProva _a = new();

    public void Dispose() => _a.Dispose();

    private CartellaFormViewModel Form(CartellaDettaglio dettaglio) =>
        new(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, dettaglio);

    private async Task<DatiCartella> SalvateAsync(CartellaFormViewModel form) => (await _a.Servizio.CaricaCartellaAsync(form.Id))!.Dati;

    [Fact]
    public async Task IlForm_MostraLeNoteGiaSalvate()
    {
        var cartella = await _a.CreaCartellaAsync("Fatture", "P");
        var completata = await _a.Servizio.AggiornaCartellaAsync(
            cartella.Id, cartella.Dati with { Completato = true, DataCompletamento = new DateOnly(2026, 10, 9), NoteCompletamento = "Chiusa" });

        var form = Form(completata);

        Assert.True(form.Completato);
        Assert.Equal("Chiusa", form.NoteCompletamento);
    }

    [Fact]
    public async Task ScrivendoLeNote_SiSalvanoDaSole()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P"));
        form.Completato = true;
        await form.AttendiSalvataggioAsync();

        form.NoteCompletamento = "Pagata, manca solo la ricevuta";
        await form.AttendiSalvataggioAsync();

        Assert.Equal("Pagata, manca solo la ricevuta", (await SalvateAsync(form)).NoteCompletamento);
        Assert.Equal("", form.Errore);
    }

    [Fact]
    public async Task TogliendoCompletato_LeNoteSparisconoDalDatabase_MaNelFormRestano()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P"));
        form.Completato = true;
        form.NoteCompletamento = "Da non perdere";
        await form.AttendiSalvataggioAsync();
        Assert.Equal("Da non perdere", (await SalvateAsync(form)).NoteCompletamento);

        form.Completato = false;
        await form.AttendiSalvataggioAsync();

        Assert.Null((await SalvateAsync(form)).NoteCompletamento);
        Assert.Equal("Da non perdere", form.NoteCompletamento); // la spunta tolta per sbaglio non fa perdere quel che si era scritto

        form.Completato = true;
        await form.AttendiSalvataggioAsync();

        Assert.Equal("Da non perdere", (await SalvateAsync(form)).NoteCompletamento);
    }

    [Fact]
    public async Task SvuotandoLeNote_SiCancellanoDalDatabase()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P"));
        form.Completato = true;
        form.NoteCompletamento = "Qualcosa";
        await form.AttendiSalvataggioAsync();

        form.NoteCompletamento = "  ";
        await form.AttendiSalvataggioAsync();

        Assert.Null((await SalvateAsync(form)).NoteCompletamento);
    }

    [Fact]
    public async Task LaCartellaSuccessiva_DiUnaCheSiRipete_NonCopiaLeNote()
    {
        var areaId = await _a.Servizio.CreaAreaAsync("Fiscale");
        var originale = await _a.Servizio.CreaCartellaConDatiAsync(
            areaId, new DatiCartella("F24", "Versamenti", new DateOnly(2026, 10, 16), false, null, Ricorrenza.Mensile), []);
        var form = Form(originale);

        form.NoteCompletamento = "Versato con la carta aziendale";
        form.Completato = true;
        await form.AttendiSalvataggioAsync();

        var cartelle = (await _a.Servizio.CaricaAlberoAsync()).Single().Cartelle;
        Assert.Equal(2, cartelle.Count);
        var nuova = (await _a.Servizio.CaricaCartellaAsync(cartelle.Single(c => c.Id != originale.Id).Id))!;
        Assert.Null(nuova.Dati.NoteCompletamento);
        Assert.Equal("Versato con la carta aziendale", (await _a.Servizio.CaricaCartellaAsync(originale.Id))!.Dati.NoteCompletamento);
    }

    [Fact]
    public void LaFinestraNuovaCartella_PortaLeNoteSoloSeCompletata()
    {
        var vm = new NuovaCartellaViewModel(_a.Dialog, "Fatture")
        {
            Titolo = "Fattura già chiusa",
            NoteCompletamento = "Pagata alla consegna"
        };
        Assert.Null(vm.Dati.NoteCompletamento); // non è completata: nessuna nota

        vm.Completato = true;
        Assert.Equal("Pagata alla consegna", vm.Dati.NoteCompletamento);

        vm.NoteCompletamento = "   ";
        Assert.Null(vm.Dati.NoteCompletamento);
    }
}

/// <summary>Il campo delle note, disegnato davvero: si vede solo con «Completato» spuntato.</summary>
[Collection("WPF")]
public class NoteCompletamentoVisteTests
{
    private static IEnumerable<T> Discendenti<T>(DependencyObject radice) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(radice); i++)
        {
            var figlio = VisualTreeHelper.GetChild(radice, i);
            if (figlio is T trovato)
                yield return trovato;
            foreach (var nipote in Discendenti<T>(figlio))
                yield return nipote;
        }
    }

    /// <summary>Il campo delle note e il suo contenitore (quello che si nasconde con la spunta).</summary>
    private static (TextBox Campo, FrameworkElement Contenitore) Note(DependencyObject radice)
    {
        var campo = Discendenti<TextBox>(radice).Single(t => System.Windows.Automation.AutomationProperties.GetName(t) == "Note di completamento");
        return (campo, (FrameworkElement)campo.Parent);
    }

    [Fact]
    public async Task CartellaView_LeNoteCompaionoSoloSeCompletata_EIlModuloNonSiSposta()
    {
        using var a = new ArchivioDiProva();
        var cartella = await a.CreaCartellaAsync("Fatture", "Fattura 1");
        var form = new CartellaFormViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, cartella);

        var errori = VisteTests.InSta(() =>
        {
            var vista = new CartellaView { DataContext = form };
            VisteTests.Disegna(vista, 780, 620, "note-completamento-aperta");

            var (campo, contenitore) = Note(vista);
            Assert.Equal(Visibility.Collapsed, contenitore.Visibility);
            var etichette = Discendenti<TextBlock>(vista).Where(t => t.Text == "Titolo").Single();
            var xPrima = campo.TranslatePoint(new Point(0, 0), vista).X;
            var larghezzaEtichette = etichette.ActualWidth;

            form.Completato = true;
            form.NoteCompletamento = "Pagata con bonifico il 9/10.\nRicevuta in cartella.";
            VisteTests.Disegna(vista, 780, 620, "note-completamento-completata");

            Assert.Equal(Visibility.Visible, contenitore.Visibility);
            Assert.Equal("Pagata con bonifico il 9/10.\nRicevuta in cartella.", campo.Text);
            Assert.Equal(4000, campo.MaxLength);
            Assert.True(campo.AcceptsReturn);

            // La colonna delle etichette non cambia: il form non «salta» quando compare il campo.
            Assert.Equal(larghezzaEtichette, etichette.ActualWidth, 1);
            Assert.True(campo.TranslatePoint(new Point(0, 0), vista).X >= xPrima);

            form.Completato = false;
            VisteTests.Disegna(vista, 780, 620, "note-completamento-riaperta");
            Assert.Equal(Visibility.Collapsed, contenitore.Visibility);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public void NuovaCartellaDialog_LeNoteCompaionoSoloSeCompletata()
    {
        using var a = new ArchivioDiProva();
        var vm = new NuovaCartellaViewModel(a.Dialog, "Fatture") { Titolo = "Fattura" };

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new NuovaCartellaDialog(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 560, 560, "nuova-cartella-note");

            var (campo, contenitore) = Note(contenuto);
            Assert.Equal(Visibility.Collapsed, contenitore.Visibility);

            vm.Completato = true;
            vm.NoteCompletamento = "Registrata";
            VisteTests.Disegna(contenuto, 560, 560, "nuova-cartella-note-completata");

            Assert.Equal(Visibility.Visible, contenitore.Visibility);
            Assert.Equal("Registrata", campo.Text);
        });

        Assert.Empty(errori);
    }
}
