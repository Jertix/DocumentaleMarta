using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

public class FiltriRicercaViewModelTests
{
    /// <summary>Oggi = 1 ottobre 2026; arancione entro 30 giorni, rosso entro 7.</summary>
    private static AlertService Avvisi() => new(30, 7, true, new TempoFisso(new DateTime(2026, 10, 1, 10, 0, 0)));

    private static readonly DateOnly Oggi = new(2026, 10, 1);

    // ---------- Stato di partenza e contatori ----------

    [Fact]
    public void Partenza_NessunFiltro_ContenutoAttivo()
    {
        var f = new FiltriRicercaViewModel();

        Assert.Equal(0, f.NumeroAttivi);
        Assert.Equal("Ricerca avanzata", f.TestoPulsante);
        Assert.Equal(CategoriaFile.Nessuna, f.Categorie);
        Assert.Same(AreaOpzione.Tutte, f.Area);
        Assert.Equal(StatoRicerca.Qualsiasi, f.Stato.Valore);
        Assert.True(f.CercaNelContenuto);
        Assert.False(f.ToFiltri(Avvisi()).HaFiltri);
    }

    [Fact]
    public void TipiDiFile_SiCombinano_ECountanoComeUnSoloGruppo()
    {
        var f = new FiltriRicercaViewModel { Pdf = true, Word = true };

        Assert.Equal(CategoriaFile.Pdf | CategoriaFile.Word, f.Categorie);
        Assert.Equal(1, f.NumeroAttivi);
        Assert.Equal("Ricerca avanzata (1)", f.TestoPulsante);
    }

    [Fact]
    public void IGruppiSiContanoSeparatamente()
    {
        var f = new FiltriRicercaViewModel { Excel = true, Immagini = true, Area = new AreaOpzione(3, "INPS") };
        Assert.Equal(2, f.NumeroAttivi);

        f.Stato = StatoOpzione.Tutte[1];
        Assert.Equal(3, f.NumeroAttivi);

        f.ScadenzaDal = new DateTime(2026, 1, 1);
        f.ScadenzaAl = new DateTime(2026, 12, 31);
        Assert.Equal(4, f.NumeroAttivi); // dal e al insieme sono un solo gruppo
        Assert.Equal("Ricerca avanzata (4)", f.TestoPulsante);
    }

    [Fact]
    public void LInterruttoreDelContenuto_NonContaComeFiltro()
    {
        var f = new FiltriRicercaViewModel { CercaNelContenuto = false };

        Assert.Equal(0, f.NumeroAttivi);
        Assert.False(f.ToFiltri(Avvisi()).CercaNelContenuto);
    }

    [Fact]
    public void Menu_LaVoceNullaDiventaLaVoceDiPartenza()
    {
        var f = new FiltriRicercaViewModel { Area = new AreaOpzione(3, "INPS"), Stato = StatoOpzione.Tutte[2] };

        f.Area = null!;
        f.Stato = null!;

        Assert.Same(AreaOpzione.Tutte, f.Area);
        Assert.Equal(StatoRicerca.Qualsiasi, f.Stato.Valore);
    }

    // ---------- Traduzione nei filtri del servizio ----------

    [Fact]
    public void ToFiltri_TipiAreaContenuto_PassanoCosiCome()
    {
        var f = new FiltriRicercaViewModel { Pdf = true, Immagini = true, Area = new AreaOpzione(7, "Fatture"), CercaNelContenuto = false };

        var filtri = f.ToFiltri(Avvisi());

        Assert.Equal(CategoriaFile.Pdf | CategoriaFile.Immagini, filtri.Categorie);
        Assert.Equal(7, filtri.AreaId);
        Assert.False(filtri.CercaNelContenuto);
    }

    [Theory]
    [InlineData(StatoRicerca.Qualsiasi, StatoCartella.Qualsiasi)]
    [InlineData(StatoRicerca.NonCompletate, StatoCartella.Aperta)]
    [InlineData(StatoRicerca.Completate, StatoCartella.Completata)]
    public void ToFiltri_StatiSemplici(StatoRicerca scelto, StatoCartella atteso)
    {
        var f = new FiltriRicercaViewModel { Stato = StatoOpzione.Tutte.Single(s => s.Valore == scelto) };

        var filtri = f.ToFiltri(Avvisi());

        Assert.Equal(atteso, filtri.Stato);
        Assert.Null(filtri.ScadenzaDal);
        Assert.Null(filtri.ScadenzaAl);
    }

    [Fact]
    public void ToFiltri_InScadenza_EdAperteEntroLaSogliaArancione()
    {
        var f = new FiltriRicercaViewModel { Stato = StatoOpzione.Tutte.Single(s => s.Valore == StatoRicerca.InScadenza) };

        var filtri = f.ToFiltri(Avvisi());

        Assert.Equal(StatoCartella.Aperta, filtri.Stato);
        Assert.Equal(Oggi, filtri.ScadenzaDal);
        Assert.Equal(Oggi.AddDays(30), filtri.ScadenzaAl);
    }

    [Fact]
    public void ToFiltri_Scadute_EdAperteConScadenzaPassata()
    {
        var f = new FiltriRicercaViewModel { Stato = StatoOpzione.Tutte.Single(s => s.Valore == StatoRicerca.Scadute) };

        var filtri = f.ToFiltri(Avvisi());

        Assert.Equal(StatoCartella.Aperta, filtri.Stato);
        Assert.Null(filtri.ScadenzaDal);
        Assert.Equal(Oggi.AddDays(-1), filtri.ScadenzaAl);
    }

    [Fact]
    public void ToFiltri_InScadenzaConDateScelte_ValgonoEntrambe()
    {
        var f = new FiltriRicercaViewModel
        {
            Stato = StatoOpzione.Tutte.Single(s => s.Valore == StatoRicerca.InScadenza),
            ScadenzaDal = new DateTime(2026, 10, 10),   // più tardi di oggi: vale questa
            ScadenzaAl = new DateTime(2026, 12, 31)     // più tardi della soglia: vale la soglia
        };

        var filtri = f.ToFiltri(Avvisi());

        Assert.Equal(new DateOnly(2026, 10, 10), filtri.ScadenzaDal);
        Assert.Equal(Oggi.AddDays(30), filtri.ScadenzaAl);
    }

    [Fact]
    public void ToFiltri_ScaduteConDataAlPiuVecchia_VaLaDataScelta()
    {
        var f = new FiltriRicercaViewModel
        {
            Stato = StatoOpzione.Tutte.Single(s => s.Valore == StatoRicerca.Scadute),
            ScadenzaAl = new DateTime(2026, 8, 31)
        };

        Assert.Equal(new DateOnly(2026, 8, 31), f.ToFiltri(Avvisi()).ScadenzaAl);
    }

    [Fact]
    public void ToFiltri_SoloDateScelte()
    {
        var f = new FiltriRicercaViewModel { ScadenzaDal = new DateTime(2026, 1, 1, 15, 0, 0), ScadenzaAl = new DateTime(2026, 6, 30) };

        var filtri = f.ToFiltri(Avvisi());

        Assert.Equal(new DateOnly(2026, 1, 1), filtri.ScadenzaDal);
        Assert.Equal(new DateOnly(2026, 6, 30), filtri.ScadenzaAl);
        Assert.Equal(StatoCartella.Qualsiasi, filtri.Stato);
    }

    // ---------- Notifiche ----------

    [Fact]
    public void OgniModificaDellUtente_AvvisaUnaVolta()
    {
        var f = new FiltriRicercaViewModel();
        var avvisi = 0;
        f.Cambiati += () => avvisi++;

        f.Pdf = true;
        f.Area = new AreaOpzione(1, "A");
        f.ScadenzaDal = new DateTime(2026, 1, 1);

        Assert.Equal(3, avvisi);
    }

    [Fact]
    public void Azzera_RimetteTuttoAPosto_EAvvisaUnaVolta()
    {
        var f = new FiltriRicercaViewModel
        {
            Pdf = true, Area = new AreaOpzione(1, "A"), Stato = StatoOpzione.Tutte[2],
            ScadenzaDal = new DateTime(2026, 1, 1), CercaNelContenuto = false
        };
        var avvisi = 0;
        f.Cambiati += () => avvisi++;

        f.AzzeraCommand.Execute(null);

        Assert.Equal(0, f.NumeroAttivi);
        Assert.True(f.CercaNelContenuto);
        Assert.Null(f.ScadenzaDal);
        Assert.Equal(1, avvisi);
    }

    [Fact]
    public void Azzera_Silenzioso_NonAvvisaNessuno()
    {
        var f = new FiltriRicercaViewModel { Pdf = true };
        var avvisi = 0;
        f.Cambiati += () => avvisi++;

        f.Azzera(notifica: false);

        Assert.False(f.Pdf);
        Assert.Equal(0, avvisi);
    }

    // ---------- Elenco delle aree ----------

    [Fact]
    public void AggiornaAree_MettePrimaTutte_ENonAvvisaSeNonCambiaNulla()
    {
        var f = new FiltriRicercaViewModel();
        var avvisi = 0;
        f.Cambiati += () => avvisi++;

        f.AggiornaAree([(1, "Fatture"), (2, "INPS")]);

        Assert.Equal(["Tutte le aree", "Fatture", "INPS"], f.Aree.Select(a => a.Nome));
        Assert.Equal(0, avvisi);
    }

    [Fact]
    public void AggiornaAree_LAreaSceltaRestaScelta_AncheSeRinominata()
    {
        var f = new FiltriRicercaViewModel();
        f.AggiornaAree([(1, "Fatture"), (2, "INPS")]);
        f.Area = f.Aree.Single(a => a.Id == 2);
        var avvisi = 0;
        f.Cambiati += () => avvisi++;

        f.AggiornaAree([(1, "Fatture"), (2, "Previdenza")]);

        Assert.Equal(2, f.Area.Id);
        Assert.Equal("Previdenza", f.Area.Nome);
        Assert.Equal(0, avvisi);
        Assert.Equal(1, f.NumeroAttivi);
    }

    [Fact]
    public void AggiornaAree_SeLAreaSceltaSparisce_SiTornaATutte_EAvvisa()
    {
        var f = new FiltriRicercaViewModel();
        f.AggiornaAree([(1, "Fatture"), (2, "INPS")]);
        f.Area = f.Aree.Single(a => a.Id == 2);
        var avvisi = 0;
        f.Cambiati += () => avvisi++;

        f.AggiornaAree([(1, "Fatture")]);

        Assert.Same(AreaOpzione.Tutte, f.Area);
        Assert.Equal(1, avvisi);
        Assert.Equal(0, f.NumeroAttivi);
    }
}

public class RicercaAvanzataViewModelTests : IDisposable
{
    private static readonly DateOnly Oggi = new(2026, 10, 1);

    private readonly FintoEstrattore _estrattore = new(".pdf", ".docx", ".xlsx", ".jpg", ".txt");
    private readonly ArchivioDiProva _a;
    private readonly FintaRicerca _ricerca;
    private readonly ImpostazioniApp _impostazioni;
    private readonly MainViewModel _vm;

    public RicercaAvanzataViewModelTests()
    {
        _estrattore.Logica = (p, _) => Task.FromResult("contratto " + Path.GetFileNameWithoutExtension(p));
        _a = new ArchivioDiProva(_estrattore);
        _ricerca = new FintaRicerca(_a.Ricerca);
        _impostazioni = new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false };
        _vm = new MainViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni,
            new AlertService(30, 7, true, new TempoFisso(new DateTime(2026, 10, 1, 9, 0, 0))), _ricerca)
        {
            RitardoRicerca = TimeSpan.Zero
        };
    }

    public void Dispose() => _a.Dispose();

    private NodoAlberoViewModel Radice => _vm.Radici.Single();
    private NodoAlberoViewModel Area(string nome) => Radice.Aree.Single(a => a.Nome == nome);

    /// <summary>
    /// Fatture: "Rossi" (scade tra 5 giorni) con a.pdf e b.docx; "Bianchi" (tra 20) con c.xlsx; "Lontana" (tra 90) con f.pdf.
    /// INPS: "Contributi" (senza scadenza) con d.pdf; "Vecchia" (completata, scaduta) con e.jpg.
    /// </summary>
    private async Task PreparaAsync()
    {
        var fatture = await _a.Servizio.CreaAreaAsync("Fatture");
        var inps = await _a.Servizio.CreaAreaAsync("INPS");
        await _a.CreaCartellaInAreaAsync(fatture, "Rossi", ["a.pdf", "b.docx"], Oggi.AddDays(5));
        await _a.CreaCartellaInAreaAsync(fatture, "Bianchi", ["c.xlsx"], Oggi.AddDays(20));
        await _a.CreaCartellaInAreaAsync(fatture, "Lontana", ["f.pdf"], Oggi.AddDays(90));
        await _a.CreaCartellaInAreaAsync(inps, "Contributi", ["d.pdf"]);
        var vecchia = await _a.CreaCartellaInAreaAsync(inps, "Vecchia", ["e.jpg"], Oggi.AddDays(-30));
        await _a.Servizio.AggiornaCartellaAsync(vecchia.Id, vecchia.Dati with { Completato = true, DataCompletamento = Oggi.AddDays(-31) });
        await _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));
        await _vm.InizializzaAsync();
        await _vm.CaricamentoElencoCompletato;
    }

    private async Task<string[]> NomiAsync()
    {
        await _vm.RicercaCompletata;
        return _vm.ElencoDocumenti!.Documenti.Select(d => d.NomeFile).OrderBy(n => n, StringComparer.Ordinal).ToArray();
    }

    // ---------- Solo filtri ----------

    [Fact]
    public async Task ScegliendoUnFiltro_SenzaScrivereNulla_SiMostranoIDocumentiCheLoRispettano()
    {
        await PreparaAsync();

        _vm.Filtri.Pdf = true;

        Assert.Equal(["a.pdf", "d.pdf", "f.pdf"], await NomiAsync());
        Assert.True(_vm.InRicerca);
        Assert.True(_vm.RicercaAttiva);
        Assert.False(_vm.TestoRicercaAttivo);
        Assert.Equal("Risultati", _vm.TitoloDettaglio);
        Assert.Equal("3 documenti trovati con i filtri scelti", _vm.RiepilogoDettaglio);
    }

    [Fact]
    public async Task IFiltriArrivanoAlServizioNelFormatoGiusto()
    {
        await PreparaAsync();

        _vm.Filtri.Word = true;
        _vm.Filtri.Area = _vm.Filtri.Aree.Single(a => a.Nome == "Fatture");
        await _vm.RicercaCompletata;

        var filtri = _ricerca.FiltriRicevuti.Last()!;
        Assert.Equal(CategoriaFile.Word, filtri.Categorie);
        Assert.Equal(_vm.Filtri.Area.Id, filtri.AreaId);
        Assert.Equal(["b.docx"], await NomiAsync());
    }

    [Fact]
    public async Task PiuFiltriInsieme_SiRestringono()
    {
        await PreparaAsync();

        _vm.Filtri.Pdf = true;
        _vm.Filtri.Area = _vm.Filtri.Aree.Single(a => a.Nome == "INPS");

        Assert.Equal(["d.pdf"], await NomiAsync());
    }

    [Fact]
    public async Task Stato_InScadenza_SoloCartelleAperteEntroLaSoglia()
    {
        await PreparaAsync();

        _vm.Filtri.Stato = _vm.Filtri.Stati.Single(s => s.Valore == StatoRicerca.InScadenza);

        Assert.Equal(["a.pdf", "b.docx", "c.xlsx"], await NomiAsync()); // Rossi (+5) e Bianchi (+20); non Lontana (+90)
    }

    [Fact]
    public async Task Stato_Scadute_NonIncludeLeCartelleCompletate()
    {
        await PreparaAsync();

        _vm.Filtri.Stato = _vm.Filtri.Stati.Single(s => s.Valore == StatoRicerca.Scadute);

        Assert.Empty(await NomiAsync()); // "Vecchia" è scaduta ma completata
        Assert.Equal("Nessun documento corrisponde ai filtri scelti.", _vm.ElencoDocumenti!.TestoVuoto);
    }

    [Fact]
    public async Task Stato_Completate()
    {
        await PreparaAsync();

        _vm.Filtri.Stato = _vm.Filtri.Stati.Single(s => s.Valore == StatoRicerca.Completate);

        Assert.Equal(["e.jpg"], await NomiAsync());
    }

    // ---------- Filtri e parole ----------

    [Fact]
    public async Task ParoleEFiltriInsieme()
    {
        await PreparaAsync();

        _vm.TestoRicerca = "contratto";
        await _vm.RicercaCompletata;
        Assert.Equal(6, (await NomiAsync()).Length);

        _vm.Filtri.Excel = true;

        Assert.Equal(["c.xlsx"], await NomiAsync());
        Assert.Equal("1 documento trovato per «contratto» con i filtri scelti", _vm.RiepilogoDettaglio);
        Assert.Equal("contratto", _ricerca.Richieste.Last());
    }

    [Fact]
    public async Task SenzaParoleENessunResultato_ILTestoVuotoDiceCheNienteCorrispondeAiFiltri_ElConParole()
    {
        await PreparaAsync();
        _vm.TestoRicerca = "xyzzyx";
        await _vm.RicercaCompletata;
        _vm.Filtri.Pdf = true;
        await _vm.RicercaCompletata;

        Assert.Equal("Nessun documento trovato per «xyzzyx» con i filtri scelti.", _vm.ElencoDocumenti!.TestoVuoto);
    }

    [Fact]
    public async Task CercareSoloNeiNomi_ILContenutoNonSiCerca()
    {
        await PreparaAsync();
        _vm.TestoRicerca = "contratto";
        await _vm.RicercaCompletata;

        _vm.Filtri.CercaNelContenuto = false;
        await _vm.RicercaCompletata;

        Assert.Empty(await NomiAsync()); // "contratto" sta solo dentro i documenti
    }

    // ---------- Uscire dalla ricerca ----------

    [Fact]
    public async Task AzzerandoIFiltri_SenzaParole_SiTornaAllaVistaNormale()
    {
        await PreparaAsync();
        Area("Fatture").IsSelected = true;
        await _vm.CaricamentoElencoCompletato;
        _vm.Filtri.Pdf = true;
        await _vm.RicercaCompletata;
        Assert.True(_vm.InRicerca);

        _vm.Filtri.AzzeraCommand.Execute(null);
        await _vm.CaricamentoElencoCompletato;

        Assert.False(_vm.InRicerca);
        Assert.False(_vm.RicercaAttiva);
        Assert.Equal("Fatture", _vm.TitoloDettaglio);
    }

    [Fact]
    public async Task SvuotandoIlCampo_ConUnFiltroAttivo_LaRicercaRestaSoloConIFiltri()
    {
        await PreparaAsync();
        _vm.TestoRicerca = "contratto";
        await _vm.RicercaCompletata;
        _vm.Filtri.Pdf = true;
        await _vm.RicercaCompletata;

        _vm.PulisciRicercaCommand.Execute(null);

        Assert.True(_vm.InRicerca);
        Assert.Equal(["a.pdf", "d.pdf", "f.pdf"], await NomiAsync());
        Assert.Equal("3 documenti trovati con i filtri scelti", _vm.RiepilogoDettaglio);
    }

    [Fact]
    public async Task ScegliendoUnAltroElementoDellAlbero_FinisceLaRicerca_EIFiltriSiAzzerano()
    {
        await PreparaAsync();
        _vm.TestoRicerca = "contratto";
        _vm.Filtri.Pdf = true;
        _vm.Filtri.Area = _vm.Filtri.Aree.Single(a => a.Nome == "INPS");
        await _vm.RicercaCompletata;

        Area("Fatture").IsSelected = true;
        await _vm.CaricamentoElencoCompletato;

        Assert.Equal("", _vm.TestoRicerca);
        Assert.Equal(0, _vm.Filtri.NumeroAttivi);
        Assert.False(_vm.Filtri.Pdf);
        Assert.False(_vm.RicercaAttiva);
        Assert.False(_vm.InRicerca);
        Assert.Equal("Fatture", _vm.TitoloDettaglio);
    }

    [Fact]
    public async Task DoppioClicSuUnRisultatoDeiFiltri_ApreLaCartella_EFinisceLaRicerca()
    {
        await PreparaAsync();
        _vm.Filtri.Excel = true;
        await _vm.RicercaCompletata;

        _vm.ElencoDocumenti!.Documenti.Single().VaiAllaCartellaCommand.Execute(null);
        await _vm.CaricamentoFormCompletato;

        Assert.Equal("Bianchi", _vm.FormCartella!.Titolo);
        Assert.False(_vm.RicercaAttiva);
        Assert.Equal(0, _vm.Filtri.NumeroAttivi);
    }

    [Fact]
    public async Task RileggendoLAlbero_FiltriERicercaRestano()
    {
        await PreparaAsync();
        _vm.Filtri.Pdf = true;
        await _vm.RicercaCompletata;

        await _vm.AggiornaCommand.ExecuteAsync(null);
        await _vm.CaricamentoElencoCompletato;

        Assert.True(_vm.Filtri.Pdf);
        Assert.True(_vm.InRicerca);
        Assert.Equal(["a.pdf", "d.pdf", "f.pdf"], await NomiAsync());
    }

    // ---------- Il menu delle aree ----------

    [Fact]
    public async Task IlMenuDelleAree_SiAggiornaConLAlbero()
    {
        await PreparaAsync();
        Assert.Equal(["Tutte le aree", "Fatture", "INPS"], _vm.Filtri.Aree.Select(a => a.Nome));

        _a.Dialog.RispondiTesto("Agenzia Entrate");
        await _vm.NuovaAreaCommand.ExecuteAsync(null);

        Assert.Equal(["Tutte le aree", "Agenzia Entrate", "Fatture", "INPS"], _vm.Filtri.Aree.Select(a => a.Nome));
    }

    [Fact]
    public async Task UnAreaEliminataMentreSiFiltraPerQuellArea_IFiltriSiAllentanoELaRicercaSiRifa()
    {
        await PreparaAsync();
        _vm.Filtri.Area = _vm.Filtri.Aree.Single(a => a.Nome == "INPS");
        await _vm.RicercaCompletata;
        Assert.Equal(["d.pdf", "e.jpg"], await NomiAsync());

        await _a.Servizio.EliminaAreaAsync(Area("INPS").Id);
        await _vm.AggiornaCommand.ExecuteAsync(null);
        await _vm.RicercaCompletata;
        await _vm.CaricamentoElencoCompletato;

        Assert.Same(AreaOpzione.Tutte, _vm.Filtri.Area);
        Assert.False(_vm.RicercaAttiva);
        Assert.False(_vm.InRicerca);
    }

    // ---------- Il pannello ----------

    [Fact]
    public void IlPannelloSiApreESiChiude_SenzaCambiareLaRicerca()
    {
        Assert.False(_vm.PannelloFiltriAperto);

        _vm.PannelloFiltriAperto = true;

        Assert.True(_vm.PannelloFiltriAperto);
        Assert.Empty(_ricerca.Richieste);
        Assert.False(_vm.InRicerca);
    }

    [Fact]
    public void ILSuggerimentoDelloStato_CitaLaSogliaDelleImpostazioni() =>
        Assert.Contains("entro 30 giorni", _vm.SuggerimentoStatoRicerca);
}
