using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

/// <summary>Il filtro "Archiviate" della ricerca avanzata: solo i documenti delle cartelle nell'"Archivio completati".</summary>
public class RicercaArchiviateServiceTests : IDisposable
{
    private static readonly DateOnly Oggi = new(2026, 10, 1);

    private readonly ArchivioDiProva _a = new();
    private int _fatture, _inps;

    public void Dispose() => _a.Dispose();

    /// <summary>
    /// Fatture: "Aperta" (aperta.pdf, scade tra 10 giorni), "Chiusa" (chiusa.docx, completata), "Archiviata" (archiviata.pdf, completata e archiviata).
    /// INPS: "Contributi" (contributi.pdf, completata e archiviata).
    /// </summary>
    private async Task PreparaAsync()
    {
        _fatture = await _a.Servizio.CreaAreaAsync("Fatture");
        _inps = await _a.Servizio.CreaAreaAsync("INPS");
        await _a.CreaCartellaInAreaAsync(_fatture, "Aperta", ["aperta.pdf"], Oggi.AddDays(10));
        await Chiudi(await _a.CreaCartellaInAreaAsync(_fatture, "Chiusa", ["chiusa.docx"]), archivia: false);
        await Chiudi(await _a.CreaCartellaInAreaAsync(_fatture, "Archiviata", ["archiviata.pdf"]), archivia: true);
        await Chiudi(await _a.CreaCartellaInAreaAsync(_inps, "Contributi", ["contributi.pdf"]), archivia: true);
    }

    private async Task Chiudi(CartellaDettaglio c, bool archivia)
    {
        await _a.Servizio.AggiornaCartellaAsync(c.Id, c.Dati with { Completato = true, DataCompletamento = Oggi });
        if (archivia)
            await _a.Servizio.ArchiviaCartellaAsync(c.Id);
    }

    private async Task<List<string>> NomiAsync(string testo, FiltriRicerca? filtri) =>
        (await _a.Ricerca.CercaAsync(testo, filtri)).Risultati.Select(r => r.Documento.NomeFile).Order().ToList();

    [Fact]
    public async Task Archiviate_SoloIDocumentiDelleCartelleArchiviate()
    {
        await PreparaAsync();

        Assert.Equal(["archiviata.pdf", "contributi.pdf"], await NomiAsync("", new FiltriRicerca(Stato: StatoCartella.Archiviata)));
    }

    [Fact]
    public async Task IlFiltroDaSolo_BastaPerCercare_ComeGliAltri()
    {
        await PreparaAsync();

        var filtri = new FiltriRicerca(Stato: StatoCartella.Archiviata);

        Assert.True(filtri.HaFiltri);
        Assert.NotEmpty((await _a.Ricerca.CercaAsync("", filtri)).Risultati);
    }

    [Fact]
    public async Task Archiviate_SiCombinaConLeParole_ConLArea_ConITipiDiFile()
    {
        await PreparaAsync();

        Assert.Equal(["archiviata.pdf"], await NomiAsync("archiviata", new FiltriRicerca(Stato: StatoCartella.Archiviata)));
        Assert.Empty(await NomiAsync("chiusa", new FiltriRicerca(Stato: StatoCartella.Archiviata))); // completata ma non archiviata
        Assert.Equal(["contributi.pdf"], await NomiAsync("", new FiltriRicerca(AreaId: _inps, Stato: StatoCartella.Archiviata)));
        Assert.Equal(["archiviata.pdf", "contributi.pdf"],
            await NomiAsync("", new FiltriRicerca(Categorie: CategoriaFile.Pdf, Stato: StatoCartella.Archiviata)));
        Assert.Empty(await NomiAsync("", new FiltriRicerca(Categorie: CategoriaFile.Word, Stato: StatoCartella.Archiviata)));
    }

    [Fact]
    public async Task Completate_ComprendeAncheLeArchiviate_Aperte_NeLeEsclude()
    {
        await PreparaAsync();

        Assert.Equal(["archiviata.pdf", "chiusa.docx", "contributi.pdf"],
            await NomiAsync("", new FiltriRicerca(Stato: StatoCartella.Completata)));
        Assert.Equal(["aperta.pdf"], await NomiAsync("", new FiltriRicerca(Stato: StatoCartella.Aperta)));
        // Senza il filtro sullo stato (qui solo sul tipo di file) si trovano tutte, archiviate comprese.
        Assert.Equal(["aperta.pdf", "archiviata.pdf", "chiusa.docx", "contributi.pdf"],
            await NomiAsync("", new FiltriRicerca(Categorie: CategoriaFile.Pdf | CategoriaFile.Word)));
    }

    [Fact]
    public async Task SenzaCartelleArchiviate_NonTrovaNulla()
    {
        var area = await _a.Servizio.CreaAreaAsync("Fatture");
        await _a.CreaCartellaInAreaAsync(area, "Aperta", ["a.pdf"]);

        Assert.Empty(await NomiAsync("", new FiltriRicerca(Stato: StatoCartella.Archiviata)));
    }

    [Fact]
    public async Task IRisultati_DiconoSeLaCartellaEArchiviata()
    {
        await PreparaAsync();

        var risultati = (await _a.Ricerca.CercaAsync("pdf", new FiltriRicerca())).Risultati.ToDictionary(r => r.Documento.NomeFile, r => r.Documento);

        Assert.True(risultati["archiviata.pdf"].CartellaArchiviata);
        Assert.True(risultati["contributi.pdf"].CartellaArchiviata);
        Assert.False(risultati["aperta.pdf"].CartellaArchiviata);
    }

    [Fact]
    public async Task AncheGliElenchiDiAreeERadice_DiconoSeLaCartellaEArchiviata()
    {
        await PreparaAsync();

        var elenco = (await _a.Servizio.CaricaDocumentiAsync(null)).ToDictionary(d => d.NomeFile);

        Assert.True(elenco["archiviata.pdf"].CartellaArchiviata);
        Assert.False(elenco["chiusa.docx"].CartellaArchiviata);
        Assert.False(elenco["aperta.pdf"].CartellaArchiviata);
    }

    [Fact]
    public async Task RipristinandoUnaCartella_NonRisultaPiuArchiviata()
    {
        await PreparaAsync();
        var cartella = (await _a.Servizio.CaricaAlberoAsync()).Single(a => a.Nome == "INPS").Cartelle.Single();

        await _a.Servizio.RipristinaCartellaAsync(cartella.Id);

        Assert.Equal(["archiviata.pdf"], await NomiAsync("", new FiltriRicerca(Stato: StatoCartella.Archiviata)));
    }
}

public class FiltroArchiviateViewModelTests
{
    private static AlertService Avvisi() => new(30, 7, true, new TempoFisso(new DateTime(2026, 10, 1, 10, 0, 0)));

    [Fact]
    public void IlMenuDelloStato_HaLaVoceArchiviate_InFondo()
    {
        Assert.Equal(
            ["Qualsiasi", "Non completate", "Completate", "In scadenza", "Scadute", "Archiviate"],
            StatoOpzione.Tutte.Select(s => s.Testo));
    }

    [Fact]
    public void ToFiltri_Archiviate_DiventaLoStatoArchiviata_SenzaDate()
    {
        var f = new FiltriRicercaViewModel { Stato = StatoOpzione.Tutte.Single(s => s.Valore == StatoRicerca.Archiviate) };

        var filtri = f.ToFiltri(Avvisi());

        Assert.Equal(StatoCartella.Archiviata, filtri.Stato);
        Assert.Null(filtri.ScadenzaDal);
        Assert.Null(filtri.ScadenzaAl);
        Assert.True(filtri.HaFiltri);
    }

    [Fact]
    public void Archiviate_ContaComeUnFiltro_EIlPulsanteLoDice()
    {
        var f = new FiltriRicercaViewModel { Stato = StatoOpzione.Tutte.Single(s => s.Valore == StatoRicerca.Archiviate) };

        Assert.Equal(1, f.NumeroAttivi);
        Assert.Equal("Ricerca avanzata (1)", f.TestoPulsante);
    }

    [Fact]
    public void Azzerando_LaVoceTornaAQualsiasi()
    {
        var f = new FiltriRicercaViewModel { Stato = StatoOpzione.Tutte.Single(s => s.Valore == StatoRicerca.Archiviate) };

        f.AzzeraCommand.Execute(null);

        Assert.Equal(StatoRicerca.Qualsiasi, f.Stato.Valore);
        Assert.Equal(0, f.NumeroAttivi);
    }
}

public class FiltroArchiviateNelProgrammaTests : IDisposable
{
    private static readonly DateOnly Oggi = new(2026, 10, 1);

    private readonly ArchivioDiProva _a = new();
    private readonly MainViewModel _vm;

    public FiltroArchiviateNelProgrammaTests()
    {
        var impostazioni = new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false };
        _vm = new MainViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, impostazioni,
            new AlertService(impostazioni, new TempoFisso(new DateTime(2026, 10, 1, 9, 0, 0))), _a.Ricerca)
        { RitardoRicerca = TimeSpan.Zero };
    }

    public void Dispose() => _a.Dispose();

    private async Task PreparaAsync()
    {
        var area = await _a.Servizio.CreaAreaAsync("Fatture");
        await _a.CreaCartellaInAreaAsync(area, "Aperta", ["aperta.pdf"]);
        var chiusa = await _a.CreaCartellaInAreaAsync(area, "Chiusa", ["chiusa.pdf"]);
        await _a.Servizio.AggiornaCartellaAsync(chiusa.Id, chiusa.Dati with { Completato = true, DataCompletamento = Oggi });
        var arch = await _a.CreaCartellaInAreaAsync(area, "Archiviata", ["archiviata.pdf"]);
        await _a.Servizio.AggiornaCartellaAsync(arch.Id, arch.Dati with { Completato = true, DataCompletamento = Oggi });
        await _a.Servizio.ArchiviaCartellaAsync(arch.Id);
        await _vm.InizializzaAsync();
    }

    [Fact]
    public async Task ScegliendoArchiviate_LaRicercaPartePerConto_SuTutto_ESiVedonoSoloLeArchiviate()
    {
        await PreparaAsync();

        _vm.Filtri.Stato = StatoOpzione.Tutte.Single(s => s.Valore == StatoRicerca.Archiviate);
        await _vm.RicercaCompletata;

        Assert.True(_vm.InRicerca);
        var riga = Assert.Single(_vm.ElencoDocumenti!.Documenti);
        Assert.Equal("archiviata.pdf", riga.NomeFile);
        Assert.True(riga.CartellaArchiviata);
        Assert.Equal("1 documento trovato con i filtri scelti", _vm.RiepilogoDettaglio);
    }

    [Fact]
    public async Task ConLeParole_SiCercaSoloTraLeArchiviate()
    {
        await PreparaAsync();
        _vm.Filtri.Stato = StatoOpzione.Tutte.Single(s => s.Valore == StatoRicerca.Archiviate);
        await _vm.RicercaCompletata;

        _vm.TestoRicerca = "chiusa";
        await _vm.RicercaCompletata;

        Assert.Empty(_vm.ElencoDocumenti!.Documenti);
        Assert.Contains("Nessun documento trovato per «chiusa» con i filtri scelti", _vm.ElencoDocumenti.TestoVuoto);

        _vm.TestoRicerca = "archiviata";
        await _vm.RicercaCompletata;
        Assert.Equal(["archiviata.pdf"], _vm.ElencoDocumenti.Documenti.Select(d => d.NomeFile));
    }

    [Fact]
    public async Task IlSuggerimentoDelMenu_SpiegaCheCosaSonoLeArchiviate()
    {
        await PreparaAsync();

        Assert.Contains("«Archiviate»", _vm.SuggerimentoStatoRicerca);
        Assert.Contains("«Archivio completati»", _vm.SuggerimentoStatoRicerca);
        Assert.Contains("«Completate»: anche quelle archiviate", _vm.SuggerimentoStatoRicerca);
    }

    [Fact]
    public async Task IlDoppioClicSuUnRisultatoArchiviato_PortaNellArchivio()
    {
        await PreparaAsync();
        _vm.Filtri.Stato = StatoOpzione.Tutte.Single(s => s.Valore == StatoRicerca.Archiviate);
        await _vm.RicercaCompletata;

        _vm.ElencoDocumenti!.Documenti.Single().VaiAllaCartellaCommand.Execute(null);
        await _vm.CaricamentoFormCompletato;

        Assert.True(_vm.NodoSelezionato!.Archiviata);
        Assert.Equal("Archiviata", _vm.NodoSelezionato.Nome);
        Assert.True(_vm.FormCartella!.Archiviata);
    }
}

[Collection("WPF")]
public class FiltroArchiviateVisteTests
{
    private static IEnumerable<T> Tutti<T>(DependencyObject? radice) where T : DependencyObject
    {
        if (radice is null)
            yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(radice); i++)
        {
            var figlio = VisualTreeHelper.GetChild(radice, i);
            if (figlio is T trovato)
                yield return trovato;
            foreach (var discendente in Tutti<T>(figlio))
                yield return discendente;
        }
    }

    [Fact]
    public async Task LaGriglia_MostraLaScatolaDArchivioSoloAccantoAlleCartelleArchiviate()
    {
        using var a = new ArchivioDiProva();
        var area = await a.Servizio.CreaAreaAsync("Fatture");
        await a.CreaCartellaInAreaAsync(area, "Aperta", ["aperta.pdf"]);
        var chiusa = await a.CreaCartellaInAreaAsync(area, "Archiviata", ["archiviata.pdf"]);
        await a.Servizio.AggiornaCartellaAsync(chiusa.Id, chiusa.Dati with { Completato = true, DataCompletamento = new DateOnly(2026, 10, 1) });
        await a.Servizio.ArchiviaCartellaAsync(chiusa.Id);
        var elenco = new ElencoDocumentiViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, null);
        await elenco.CaricaAsync();

        var errori = VisteTests.InSta(() =>
        {
            var vista = new ElencoView { DataContext = elenco };
            VisteTests.Disegna(vista, 900, 300, "griglia-con-archiviate");

            var scatole = Tutti<TextBlock>(vista).Where(t => t.Text == NodoAlberoViewModel.IconaArchivio).ToList();
            Assert.Equal(2, Tutti<TextBlock>(vista).Count(t => t.Text == "Archiviata" || t.Text == "Aperta"));
            // Il TextBlock c'è per ogni riga ma si vede solo dove la cartella è archiviata.
            Assert.Equal(2, scatole.Count);
            var visibile = Assert.Single(scatole, t => t.Visibility == Visibility.Visible);
            Assert.Equal("Cartella archiviata in «Archivio completati»", visibile.ToolTip);
        });

        Assert.Empty(errori);
    }
}
