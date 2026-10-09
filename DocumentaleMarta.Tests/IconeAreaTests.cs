using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DocumentaleMarta.Tests;

/// <summary>L'elenco delle icone tra cui scegliere per un'area: un elenco chiuso di simboli dei font icone di Windows.</summary>
public class IconeAreaTests
{
    [Fact]
    public void Disponibili_HannoCodiciEDescrizioniUnici()
    {
        Assert.Equal(IconeArea.Disponibili.Count, IconeArea.Disponibili.Select(i => i.Codice).Distinct().Count());
        Assert.Equal(
            IconeArea.Disponibili.Count,
            IconeArea.Disponibili.Select(i => i.Descrizione).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(IconeArea.Disponibili, i => Assert.False(string.IsNullOrWhiteSpace(i.Descrizione)));
    }

    [Fact]
    public void Disponibili_OgniGlifoEUnSoloCarattereDelFontDelleIcone()
    {
        // Un simbolo del font di Windows è un solo carattere dell'area privata: niente emoji (coppie surrogate, a colori).
        Assert.All(IconeArea.Disponibili, i =>
        {
            Assert.Equal(1, i.Glifo.Length);
            Assert.InRange(i.Glifo[0], '', '');
            Assert.Equal(i.Codice, ((int)i.Glifo[0]).ToString("X4"));
        });
    }

    [Fact]
    public void Disponibili_LaGrigliaHaLeRigheComplete() =>
        Assert.Equal(0, IconeArea.Disponibili.Count % 9); // nove riquadri per riga nella finestra

    [Fact]
    public void Disponibili_ContengonoLaPredefinita_ChePerDiPiuELIconaCheLeAreeAvevanoPrima()
    {
        Assert.True(IconeArea.EValida(IconeArea.Predefinita));
        Assert.Equal(NodoAlberoViewModel.IconaArea, IconeArea.Glifo(IconeArea.Predefinita));
        Assert.Equal(IconeArea.Predefinita, IconeArea.Disponibili[0].Codice); // la prima della griglia
    }

    [Fact]
    public void Disponibili_NonRipetonoLeIconeDegliAltriNodiDellAlbero()
    {
        // Radice, Scadenze, cartelle, completate e archivio hanno un simbolo loro: un'area non deve poterlo imitare.
        var degliAltri = new[]
        {
            NodoAlberoViewModel.IconaRadice, NodoAlberoViewModel.IconaScadenze, NodoAlberoViewModel.IconaCartella,
            NodoAlberoViewModel.IconaCartellaCompletata, NodoAlberoViewModel.IconaArchivio
        };

        Assert.DoesNotContain(IconeArea.Disponibili, i => degliAltri.Contains(i.Glifo));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ZZZZ")]
    [InlineData("E000")]
    [InlineData("1F4C1")] // un'emoji non è tra le icone proposte
    public void Glifo_SenzaCodiceOCodiceSconosciuto_DaLIconaPredefinita(string? codice)
    {
        Assert.False(IconeArea.EValida(codice));
        Assert.Equal(NodoAlberoViewModel.IconaArea, IconeArea.Glifo(codice));
        Assert.Null(IconeArea.DaSalvare(codice));
    }

    [Fact]
    public void Codice_NonDistingueLeMaiuscole()
    {
        Assert.True(IconeArea.EValida("eb44"));
        Assert.Equal(IconeArea.Glifo("EB44"), IconeArea.Glifo(" eb44 "));
        Assert.Equal("EB44", IconeArea.DaSalvare("eb44"));
    }

    [Fact]
    public void DaSalvare_PerLaPredefinita_DaNull()
    {
        // Un'area con l'icona predefinita e un'area che non ne ha mai scelta una sono la stessa cosa.
        Assert.Null(IconeArea.DaSalvare(IconeArea.Predefinita));
        Assert.Null(IconeArea.DaSalvare(IconeArea.Predefinita.ToLowerInvariant()));
    }
}

/// <summary>Il servizio salva e legge l'icona delle aree, rifiutando quelle fuori elenco.</summary>
public class IconeAreaServiceTests : IDisposable
{
    private readonly ArchivioDiProva _a = new();

    public void Dispose() => _a.Dispose();

    private async Task<string?> IconaDellAreaAsync(int areaId) =>
        (await _a.Servizio.CaricaAlberoAsync()).Single(n => n.Id == areaId).Icona;

    [Fact]
    public async Task CreaArea_ConIcona_LaSalvaELaRestituisceLAlbero()
    {
        var id = await _a.Servizio.CreaAreaAsync("Fatture", "E8EF");

        Assert.Equal("E8EF", await IconaDellAreaAsync(id));
        using var db = _a.Factory.CreateDbContext();
        Assert.Equal("E8EF", db.Aree.Single(a => a.Id == id).Icona);
    }

    [Fact]
    public async Task CreaArea_SenzaIcona_NonLaHa() =>
        Assert.Null(await IconaDellAreaAsync(await _a.Servizio.CreaAreaAsync("Fatture")));

    [Fact]
    public async Task CreaArea_ConLIconaPredefinita_SalvaNull() =>
        Assert.Null(await IconaDellAreaAsync(await _a.Servizio.CreaAreaAsync("Fatture", IconeArea.Predefinita)));

    [Fact]
    public async Task CreaArea_IconaInMinuscolo_SiSalvaInMaiuscolo() =>
        Assert.Equal("E8EF", await IconaDellAreaAsync(await _a.Servizio.CreaAreaAsync("Fatture", "e8ef")));

    [Theory]
    [InlineData("ZZZZ")]
    [InlineData("E000")]
    [InlineData("<script>")]
    public async Task CreaArea_IconaSconosciuta_RifiutataSenzaCreareNulla(string icona)
    {
        var ex = await Assert.ThrowsAsync<ArchivioException>(() => _a.Servizio.CreaAreaAsync("Fatture", icona));

        Assert.Contains("icona", ex.Message);
        Assert.Empty(await _a.Servizio.CaricaAlberoAsync());
        Assert.False(Directory.Exists(_a.Fisico("Fatture")));
    }

    [Fact]
    public async Task ImpostaIcona_LaCambiaELaAzzera()
    {
        var id = await _a.Servizio.CreaAreaAsync("Fatture");

        await _a.Servizio.ImpostaIconaAreaAsync(id, "EA5E");
        Assert.Equal("EA5E", await IconaDellAreaAsync(id));

        await _a.Servizio.ImpostaIconaAreaAsync(id, "e94c");
        Assert.Equal("E94C", await IconaDellAreaAsync(id));

        await _a.Servizio.ImpostaIconaAreaAsync(id, null);
        Assert.Null(await IconaDellAreaAsync(id));
    }

    [Fact]
    public async Task ImpostaIcona_StessaIcona_NonFaNulla()
    {
        var id = await _a.Servizio.CreaAreaAsync("Fatture", "EA5E");

        await _a.Servizio.ImpostaIconaAreaAsync(id, "EA5E");

        Assert.Equal("EA5E", await IconaDellAreaAsync(id));
    }

    [Fact]
    public async Task ImpostaIcona_IconaSconosciuta_RifiutataEResta_QuellaDiPrima()
    {
        var id = await _a.Servizio.CreaAreaAsync("Fatture", "EA5E");

        await Assert.ThrowsAsync<ArchivioException>(() => _a.Servizio.ImpostaIconaAreaAsync(id, "ZZZZ"));

        Assert.Equal("EA5E", await IconaDellAreaAsync(id));
    }

    [Fact]
    public async Task ImpostaIcona_AreaInesistente_Eccezione()
    {
        var ex = await Assert.ThrowsAsync<ArchivioException>(() => _a.Servizio.ImpostaIconaAreaAsync(999, "EA5E"));

        Assert.Contains("non esiste più", ex.Message);
    }

    [Fact]
    public async Task ImpostaIcona_NonTocca_CartelleEFile()
    {
        var cartella = await _a.CreaCartellaAsync("Fatture", "Fattura 1", "a.pdf");
        var areaId = (await _a.Servizio.CaricaAlberoAsync()).Single().Id;

        await _a.Servizio.ImpostaIconaAreaAsync(areaId, "E8EF");

        var area = (await _a.Servizio.CaricaAlberoAsync()).Single();
        Assert.Equal("Fattura 1", area.Cartelle.Single().Titolo);
        Assert.Equal(1, area.Cartelle.Single().NumeroDocumenti);
        Assert.True(File.Exists(_a.Fisico("Fatture", "Fattura 1", "a.pdf")));
        Assert.Equal(cartella.Id, area.Cartelle.Single().Id);
    }

    [Fact]
    public async Task RinominaArea_MantieneLIcona()
    {
        var id = await _a.Servizio.CreaAreaAsync("Vecchia", "E94C");

        await _a.Servizio.RinominaAreaAsync(id, "Nuova");

        Assert.Equal("E94C", await IconaDellAreaAsync(id));
    }
}

/// <summary>Un archivio creato prima delle icone (senza la colonna) si aggiorna senza perdere nulla.</summary>
public class IconeAreaMigrazioneTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public async Task UnDatabaseVecchio_SiAggiorna_ELeAreeEsistentiHannoLIconaPredefinita()
    {
        var percorso = _tmp.Combina("vecchio", "documentale.db");
        Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);

        // Il database com'era prima delle icone: ultima migrazione precedente, con un'area dentro.
        await using (var vecchio = new AppDbContext(ArchivioDatabase.CreaOpzioni(percorso)))
        {
            vecchio.GetService<IMigrator>().Migrate("CartelleArchiviate");
            await vecchio.Database.ExecuteSqlRawAsync("INSERT INTO Aree (Id, Nome, PercorsoRelativo, Ordine) VALUES (1, 'Fatture', 'Fatture', 0)");
        }

        // L'app si avvia con la nuova versione.
        await using (var nuovo = new AppDbContext(ArchivioDatabase.CreaOpzioni(percorso)))
            await nuovo.Database.MigrateAsync();

        await using var db = new AppDbContext(ArchivioDatabase.CreaOpzioni(percorso));
        var area = await db.Aree.SingleAsync();
        Assert.Equal("Fatture", area.Nome);
        Assert.Null(area.Icona);
        Assert.Equal(IconeArea.Predefinita, IconeArea.Risolvi(area.Icona).Codice);
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_IconaArea"));

        // E ora si può scegliere un'icona.
        area.Icona = "E8EF";
        await db.SaveChangesAsync();
        await using var riletto = new AppDbContext(ArchivioDatabase.CreaOpzioni(percorso));
        Assert.Equal("E8EF", (await riletto.Aree.SingleAsync()).Icona);
    }
}

/// <summary>I dati della finestra di scelta dell'icona.</summary>
public class AreaDialogViewModelTests
{
    private static string? NomeObbligatorio(string testo) => testo.Trim().Length == 0 ? "Scrivi un nome." : null;

    [Fact]
    public void NuovaArea_ChiedeNome_PartedallIconaPredefinita_EOkSiAttivaConUnNomeValido()
    {
        var modello = new AreaDialogViewModel("Nuova area", null, NomeObbligatorio);

        Assert.True(modello.ChiedeNome);
        Assert.Equal(IconeArea.Predefinita, modello.IconaSelezionata.Codice);
        Assert.Null(modello.CodiceIcona);
        Assert.False(modello.PuoConfermare);

        modello.Nome = "  Fatture ";

        Assert.True(modello.PuoConfermare);
        Assert.Equal("Fatture", modello.NomeConfermato);
    }

    [Fact]
    public void ErroreNome_ComparesoloDopoCheSiESrittoQualcosa()
    {
        var modello = new AreaDialogViewModel("Nuova area", null, NomeObbligatorio);
        Assert.Equal("", modello.ErroreNome); // campo appena aperto: nessun rimprovero

        modello.Nome = "   ";
        Assert.Equal("Scrivi un nome.", modello.ErroreNome);
        Assert.False(modello.PuoConfermare);

        modello.Nome = "INPS";
        Assert.Equal("", modello.ErroreNome);
    }

    [Fact]
    public void CambioIcona_NonChiedeNome_EParteDallIconaAttuale()
    {
        var modello = new AreaDialogViewModel("Cambia icona", "e8ef", messaggioIcona: "Scegli:");

        Assert.False(modello.ChiedeNome);
        Assert.True(modello.PuoConfermare);
        Assert.Equal("E8EF", modello.IconaSelezionata.Codice);
        Assert.Equal("E8EF", modello.CodiceIcona);
        Assert.Equal("Contabilità", modello.DescrizioneIconaSelezionata);
        Assert.Equal("Scegli:", modello.MessaggioIcona);
    }

    [Fact]
    public void IconaAttualeSconosciuta_ParteDallaPredefinita()
    {
        var modello = new AreaDialogViewModel("Cambia icona", "ZZZZ");

        Assert.Equal(IconeArea.Predefinita, modello.IconaSelezionata.Codice);
        Assert.Null(modello.CodiceIcona);
    }

    [Fact]
    public void ScegliendoUnIcona_CodiceEDescrizioneSiAggiornano_ENotificano()
    {
        var modello = new AreaDialogViewModel("Nuova area", null, NomeObbligatorio);
        var notificate = new List<string?>();
        ((INotifyPropertyChanged)modello).PropertyChanged += (_, e) => notificate.Add(e.PropertyName);

        modello.IconaSelezionata = IconeArea.Disponibili.Single(i => i.Codice == "EA5E");

        Assert.Equal("EA5E", modello.CodiceIcona);
        Assert.Equal("Automezzi", modello.DescrizioneIconaSelezionata);
        Assert.Contains(nameof(AreaDialogViewModel.DescrizioneIconaSelezionata), notificate);
    }

    [Fact]
    public void LaPredefinitaRiscelta_DaCodiceNull() // tornare alla libreria = nessuna icona scelta
    {
        var modello = new AreaDialogViewModel("Cambia icona", "EA5E");

        modello.IconaSelezionata = IconeArea.Disponibili.Single(i => i.Codice == IconeArea.Predefinita);

        Assert.Null(modello.CodiceIcona);
    }
}

/// <summary>Nuova area con icona e «Cambia icona» nella finestra principale (ViewModel).</summary>
public class IconeAreaViewModelTests : IDisposable
{
    private readonly ArchivioDiProva _a = new();
    private readonly MainViewModel _vm;

    public IconeAreaViewModelTests()
    {
        _vm = new MainViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell,
            new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false });
    }

    public void Dispose() => _a.Dispose();

    private NodoAlberoViewModel Radice => _vm.Radici.Single();
    private NodoAlberoViewModel Area(string nome) => Radice.Aree.Single(n => n.Nome == nome);

    private static IconaArea Icona(string codice) => IconeArea.Disponibili.Single(i => i.Codice == codice);

    [Fact]
    public async Task NuovaArea_ConIconaScelta_LaSalvaELaMostraSubito()
    {
        await _vm.InizializzaAsync();
        _a.Dialog.RispondiAreaDialog(m =>
        {
            m.Nome = "Fatture";
            m.IconaSelezionata = Icona("E8EF");
        });

        await _vm.NuovaAreaCommand.ExecuteAsync(null);

        var fatture = Area("Fatture");
        Assert.Same(fatture, _vm.NodoSelezionato);
        Assert.Equal("E8EF", fatture.CodiceIconaArea);
        Assert.Equal("", fatture.Icona);
        Assert.Equal("", _vm.IconaDettaglio);
        Assert.Equal("E8EF", (await _a.Servizio.CaricaAlberoAsync()).Single().Icona);
    }

    [Fact]
    public async Task NuovaArea_SenzaScegliereLIcona_UsaQuellaPredefinita()
    {
        await _vm.InizializzaAsync();
        _a.Dialog.RispondiTesto("Fatture"); // la finestra parte già con la libreria selezionata

        await _vm.NuovaAreaCommand.ExecuteAsync(null);

        var fatture = Area("Fatture");
        Assert.Null(fatture.CodiceIconaArea);
        Assert.Equal(NodoAlberoViewModel.IconaArea, fatture.Icona);
        Assert.Null((await _a.Servizio.CaricaAlberoAsync()).Single().Icona);
    }

    [Fact]
    public async Task NuovaArea_LaFinestraChiedeNomeEIcona_ConLaLibreriaSelezionata()
    {
        await _vm.InizializzaAsync();

        await _vm.NuovaAreaCommand.ExecuteAsync(null); // l'utente annulla

        var finestra = _a.Dialog.UltimaAreaDialog!;
        Assert.Equal("Nuova area", finestra.Titolo);
        Assert.True(finestra.ChiedeNome);
        Assert.Equal(IconeArea.Predefinita, finestra.IconaSelezionata.Codice);
        Assert.Empty(Radice.Aree);
    }

    [Fact]
    public async Task NuovaArea_NomeGiaUsato_LaFinestraNonSiPuoConfermare()
    {
        await _a.Servizio.CreaAreaAsync("Fatture");
        await _vm.InizializzaAsync();
        _a.Dialog.RispondiAreaDialog(m => m.Nome = "fatture");

        await _vm.NuovaAreaCommand.ExecuteAsync(null);

        Assert.Single(Radice.Aree);
        Assert.Contains("Esiste già", Assert.Single(_a.Dialog.ErroriValidazione));
    }

    [Fact]
    public async Task CambiaIcona_AggiornaNodoEDettaglio_ELaSalva_RestandoSullArea()
    {
        await _a.Servizio.CreaAreaAsync("Fatture");
        await _vm.InizializzaAsync();
        Area("Fatture").IsSelected = true;
        await _vm.CaricamentoElencoCompletato;
        Assert.True(_vm.CambiaIconaCommand.CanExecute(null));
        Assert.Equal(NodoAlberoViewModel.IconaArea, _vm.IconaDettaglio);
        _a.Dialog.RispondiAreaDialog(m => m.IconaSelezionata = Icona("EA5E"));

        await _vm.CambiaIconaCommand.ExecuteAsync(null);

        Assert.Equal("EA5E", Area("Fatture").CodiceIconaArea);
        Assert.Equal("", Area("Fatture").Icona);
        Assert.Same(Area("Fatture"), _vm.NodoSelezionato);
        Assert.Equal("", _vm.IconaDettaglio);
        Assert.Equal("EA5E", (await _a.Servizio.CaricaAlberoAsync()).Single().Icona);
    }

    [Fact]
    public async Task CambiaIcona_LaFinestraNonChiedeIlNome_EParteDallIconaAttuale()
    {
        await _a.Servizio.CreaAreaAsync("Fatture", "E94C");
        await _vm.InizializzaAsync();
        Area("Fatture").IsSelected = true;

        await _vm.CambiaIconaCommand.ExecuteAsync(null); // l'utente annulla

        var finestra = _a.Dialog.UltimaAreaDialog!;
        Assert.False(finestra.ChiedeNome);
        Assert.Equal("Cambia icona", finestra.Titolo);
        Assert.Equal("E94C", finestra.IconaSelezionata.Codice);
        Assert.Contains("Fatture", finestra.MessaggioIcona);
    }

    [Fact]
    public async Task CambiaIcona_Annullata_NonCambiaNulla()
    {
        await _a.Servizio.CreaAreaAsync("Fatture", "E94C");
        await _vm.InizializzaAsync();
        Area("Fatture").IsSelected = true;
        _a.Dialog.RispondiAreaDialog(null);

        await _vm.CambiaIconaCommand.ExecuteAsync(null);

        Assert.Equal("E94C", (await _a.Servizio.CaricaAlberoAsync()).Single().Icona);
        Assert.Equal("E94C", Area("Fatture").CodiceIconaArea);
        Assert.Empty(_a.Dialog.Errori);
    }

    [Fact]
    public async Task CambiaIcona_TornandoAllaLibreria_AzzeraLIcona()
    {
        await _a.Servizio.CreaAreaAsync("Fatture", "E94C");
        await _vm.InizializzaAsync();
        Area("Fatture").IsSelected = true;
        _a.Dialog.RispondiAreaDialog(m => m.IconaSelezionata = Icona(IconeArea.Predefinita));

        await _vm.CambiaIconaCommand.ExecuteAsync(null);

        Assert.Null((await _a.Servizio.CaricaAlberoAsync()).Single().Icona);
        Assert.Equal(NodoAlberoViewModel.IconaArea, Area("Fatture").Icona);
    }

    [Fact]
    public async Task CambiaIcona_ESoloPerLeAree()
    {
        await _a.CreaCartellaAsync("Fatture", "Fattura 1", "a.pdf");
        await _vm.InizializzaAsync();

        Assert.True(_vm.RadiceSelezionata);
        Assert.False(_vm.CambiaIconaCommand.CanExecute(null));
        Assert.Equal("", _vm.IconaDettaglio);

        Area("Fatture").IsSelected = true;
        Assert.True(_vm.CambiaIconaCommand.CanExecute(null));

        Area("Fatture").Figli.Single().IsSelected = true;
        await _vm.CaricamentoFormCompletato;
        Assert.False(_vm.CambiaIconaCommand.CanExecute(null));
        Assert.Equal("", _vm.IconaDettaglio);
    }

    [Fact]
    public async Task IlGruppoDellArea_NellArchivio_HaLaStessaIcona()
    {
        var areaId = await _a.Servizio.CreaAreaAsync("Fatture", "E8EF");
        var cartella = await _a.CreaCartellaInAreaAsync(areaId, "Pagata", ["a.pdf"]);
        await _a.Servizio.AggiornaCartellaAsync(
            cartella.Id, cartella.Dati with { Completato = true, DataCompletamento = new DateOnly(2026, 10, 1) });
        await _a.Servizio.ArchiviaCartellaAsync(cartella.Id);

        await _vm.InizializzaAsync();

        var gruppo = Radice.Figli.Single(n => n.Tipo == TipoNodo.Archivio).Figli.Single();
        Assert.Equal(TipoNodo.AreaArchivio, gruppo.Tipo);
        Assert.Equal("", gruppo.Icona);

        gruppo.IsSelected = true;
        Assert.Equal("", _vm.IconaDettaglio); // l'intestazione mostra l'icona anche lì...
        Assert.False(_vm.CambiaIconaCommand.CanExecute(null)); // ...ma si cambia dall'area vera
    }

    [Fact]
    public async Task RinominareLArea_NonCambiaLIcona()
    {
        await _a.Servizio.CreaAreaAsync("Vecchia", "E94C");
        await _vm.InizializzaAsync();
        Area("Vecchia").IsSelected = true;
        _a.Dialog.RispondiTesto("Nuova");

        await _vm.RinominaCommand.ExecuteAsync(null);

        Assert.Equal("", Area("Nuova").Icona);
        Assert.Equal("E94C", (await _a.Servizio.CaricaAlberoAsync()).Single().Icona);
    }

    [Fact]
    public async Task UnCodiceStranoNelDatabase_NonImpedisceDiAprireLArchivio()
    {
        var id = await _a.Servizio.CreaAreaAsync("Fatture");
        using (var db = _a.Factory.CreateDbContext())
        {
            db.Aree.Single(x => x.Id == id).Icona = "XXXX"; // per esempio da un backup di una versione futura
            db.SaveChanges();
        }

        await _vm.InizializzaAsync();

        Assert.Equal(NodoAlberoViewModel.IconaArea, Area("Fatture").Icona);
    }
}

/// <summary>La finestra delle icone e l'intestazione dell'area, disegnate davvero (senza mostrarle).</summary>
[Collection("WPF")]
public class IconeAreaVisteTests
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

    [Fact]
    public void AreaDialog_NuovaArea_SiDisegna_ConLaGrigliaDelleIcone_SenzaErroriDiBinding()
    {
        var modello = new AreaDialogViewModel("Nuova area", null, t => t.Trim().Length == 0 ? "Scrivi un nome." : null)
        {
            Nome = "Fatture"
        };

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new AreaDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 470, 420, "nuova-area-icone");

            var elenco = Discendenti<ListBox>(contenuto).Single();
            Assert.Equal(IconeArea.Disponibili.Count, elenco.Items.Count);
            Assert.Same(modello.IconaSelezionata, elenco.SelectedItem);

            // Nove riquadri per riga: la griglia va a capo e occupa cinque righe.
            var righe = Discendenti<ListBoxItem>(elenco)
                .Select(i => Math.Round(i.TranslatePoint(new Point(0, 0), elenco).Y))
                .Distinct()
                .Count();
            Assert.Equal(IconeArea.Disponibili.Count / 9, righe);

            // Ogni riquadro ha il suo nome per i lettori di schermo e per il suggerimento.
            Assert.All(Discendenti<ListBoxItem>(elenco), i =>
                Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(i))));

            // Il nome si vede.
            var casella = Discendenti<TextBox>(contenuto).Single();
            Assert.Equal("Fatture", casella.Text);
            Assert.Equal(Visibility.Visible, ((FrameworkElement)casella.Parent).Visibility);

            // Scegliere un riquadro cambia l'icona del modello.
            var automezzi = IconeArea.Disponibili.Single(i => i.Codice == "EA5E");
            elenco.SelectedItem = automezzi;
            Assert.Equal("EA5E", modello.CodiceIcona);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public void AreaDialog_CambioIcona_NonMostraIlNome_SenzaErroriDiBinding()
    {
        var modello = new AreaDialogViewModel("Cambia icona", "E94C", messaggioIcona: "Scegli l'icona per l'area «Fatture»:");

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new AreaDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 470, 360, "cambia-icona");

            var casella = Discendenti<TextBox>(contenuto).Single();
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)casella.Parent).Visibility);
            Assert.Equal("E94C", ((IconaArea)Discendenti<ListBox>(contenuto).Single().SelectedItem).Codice);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_ConUnAreaSelezionata_MostraLIconaNelTitolo_EIlPulsanteCambiaIcona()
    {
        using var a = new ArchivioDiProva();
        await a.Servizio.CreaAreaAsync("Fatture", "E8EF");
        await a.Servizio.CreaAreaAsync("INPS");
        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell,
            new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false });
        await vm.InizializzaAsync();

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;

            // Radice selezionata: nell'albero c'è l'icona dell'area, ma non nell'intestazione, e il pulsante non c'è.
            VisteTests.Disegna(contenuto, 1100, 680, "finestra-radice-icone");
            Assert.Single(Discendenti<TextBlock>(contenuto), t => t.Text == "");
            Assert.All(
                Discendenti<Button>(contenuto).Where(b => b.Content is "Cambia icona…"),
                b => Assert.NotEqual(Visibility.Visible, b.Visibility));

            vm.Radici[0].Figli.Single(n => n.Nome == "Fatture").IsSelected = true;
            VisteTests.Disegna(contenuto, 1100, 680, "finestra-area-icone");

            // Area selezionata: l'icona sta nell'albero e prima del titolo, e il pulsante c'è.
            Assert.Equal(2, Discendenti<TextBlock>(contenuto).Count(t => t.Text == ""));
            var pulsante = Discendenti<Button>(contenuto).Single(b => b.Content is "Cambia icona…");
            Assert.Equal(Visibility.Visible, pulsante.Visibility);
        });

        Assert.Empty(errori);
    }
}
