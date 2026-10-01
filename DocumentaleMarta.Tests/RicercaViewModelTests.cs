using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

/// <summary>Un servizio di ricerca che fa da spia: ricorda le richieste e può rispondere con calma o con errori.</summary>
public class FintaRicerca(IRicercaService vera) : IRicercaService
{
    private readonly object _blocco = new();
    private readonly List<string> _richieste = [];

    public IReadOnlyList<string> Richieste { get { lock (_blocco) return [.. _richieste]; } }

    /// <summary>Per una ricerca precisa: quanto aspettare prima di rispondere.</summary>
    public Dictionary<string, TimeSpan> Attese { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Exception? Errore { get; set; }

    public List<FiltriRicerca?> FiltriRicevuti { get; } = [];

    public async Task<EsitoRicerca> CercaAsync(string testo, FiltriRicerca? filtri = null, int massimo = 1000)
    {
        lock (_blocco)
        {
            _richieste.Add(testo);
            FiltriRicevuti.Add(filtri);
        }
        if (Attese.TryGetValue(testo, out var attesa))
            await Task.Delay(attesa);
        if (Errore is not null)
            throw Errore;
        return await vera.CercaAsync(testo, filtri, massimo);
    }
}

/// <summary>Uno stato dell'indicizzazione deciso dal test.</summary>
public class FintoMonitor : IMonitorIndicizzazione
{
    public StatoCodaIndicizzazione Stato { get; private set; } = StatoCodaIndicizzazione.Inattivo;
    public event Action? Cambiato;

    public void Imposta(StatoCodaIndicizzazione stato)
    {
        Stato = stato;
        Cambiato?.Invoke();
    }
}

public class RicercaViewModelTests : IDisposable
{
    private readonly FintoEstrattore _estrattore = new(".txt");
    private readonly ArchivioDiProva _a;
    private readonly FintaRicerca _ricerca;
    private readonly FintoMonitor _monitor = new();
    private readonly ImpostazioniApp _impostazioni;
    private readonly MainViewModel _vm;

    public RicercaViewModelTests()
    {
        _estrattore.Logica = (p, _) => Task.FromResult(
            Path.GetFileName(p) == "preventivo.txt" ? "cancello scorrevole in acciaio zincato" : "testo generico");
        _a = new ArchivioDiProva(_estrattore);
        _ricerca = new FintaRicerca(_a.Ricerca);
        _impostazioni = new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false };
        _vm = new MainViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni,
            new AlertService(_impostazioni), _ricerca, _monitor, new FintoOcr(disponibile: false))
        {
            RitardoRicerca = TimeSpan.Zero
        };
    }

    public void Dispose() => _a.Dispose();

    private NodoAlberoViewModel Radice => _vm.Radici.Single();
    private NodoAlberoViewModel Area(string nome) => Radice.Aree.Single(a => a.Nome == nome);
    private NodoAlberoViewModel Cartella(string area, string titolo) => Area(area).Figli.Single(c => c.Nome == titolo);

    /// <summary>Fatture/"Fattura Rossi": preventivo.txt, note.txt. INPS/"Contributi": f24.txt.</summary>
    private async Task PreparaAsync()
    {
        var fatture = await _a.Servizio.CreaAreaAsync("Fatture");
        var inps = await _a.Servizio.CreaAreaAsync("INPS");
        await _a.CreaCartellaInAreaAsync(fatture, "Fattura Rossi", ["preventivo.txt", "note.txt"]);
        await _a.CreaCartellaInAreaAsync(inps, "Contributi", ["f24.txt"]);
        await _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));
        await _vm.InizializzaAsync();
        await _vm.CaricamentoElencoCompletato;
    }

    private async Task CercaAsync(string testo)
    {
        _vm.TestoRicerca = testo;
        await _vm.RicercaCompletata;
    }

    // ---------- Fare una ricerca ----------

    [Fact]
    public async Task ScrivendoNelCampo_SiMostranoIRisultati_ConLIntestazioneDellaRicerca()
    {
        await PreparaAsync();

        await CercaAsync("zincato");

        var risultati = _vm.ElencoDocumenti!;
        Assert.True(risultati.IsRicerca);
        Assert.Equal(["preventivo.txt"], risultati.Documenti.Select(d => d.NomeFile));
        Assert.Contains("zincato", risultati.Documenti[0].Trovato);
        Assert.True(_vm.InRicerca);
        Assert.Equal("Ricerca", _vm.TipoDettaglio);
        Assert.Equal("Risultati", _vm.TitoloDettaglio);
        Assert.Equal("1 documento trovato per «zincato»", _vm.RiepilogoDettaglio);
        Assert.Equal("", _vm.PercorsoDettaglio);
        Assert.True(_vm.RiepilogoVisibile);
    }

    [Fact]
    public async Task NessunRisultato_LElencoDiceCosaNonHaTrovato()
    {
        await PreparaAsync();

        await CercaAsync("xyzzyx");

        Assert.Empty(_vm.ElencoDocumenti!.Documenti);
        Assert.Equal("Nessun documento trovato per «xyzzyx».", _vm.ElencoDocumenti.TestoVuoto);
        Assert.Equal("0 documenti trovati per «xyzzyx»", _vm.RiepilogoDettaglio);
    }

    [Fact]
    public async Task InRicerca_NonSiPuoApriInEsplora_PerchéNonCeUnaCartella()
    {
        await PreparaAsync();

        await CercaAsync("zincato");

        Assert.False(_vm.HaPercorsoFisico);
        Assert.False(_vm.ApriInEsploraCommand.CanExecute(null));
    }

    [Fact]
    public async Task LaRicercaCoprePerfinoIlFormDiUnaCartellaSelezionata()
    {
        await PreparaAsync();
        Cartella("INPS", "Contributi").IsSelected = true;
        await _vm.CaricamentoFormCompletato;
        Assert.NotNull(_vm.FormCartella);

        await CercaAsync("zincato");

        Assert.Null(_vm.FormCartella);
        Assert.True(_vm.RiepilogoVisibile);
        Assert.Same(Cartella("INPS", "Contributi"), _vm.NodoSelezionato); // la selezione dell'albero non cambia
    }

    [Fact]
    public async Task SvuotandoIlCampo_TornaIlPannelloDellElementoSelezionato()
    {
        await PreparaAsync();
        Area("INPS").IsSelected = true;
        await _vm.CaricamentoElencoCompletato;
        await CercaAsync("zincato");
        Assert.True(_vm.InRicerca);

        _vm.TestoRicerca = "";

        Assert.False(_vm.InRicerca);
        await _vm.CaricamentoElencoCompletato;
        Assert.Equal(["f24.txt"], _vm.ElencoDocumenti!.Documenti.Select(d => d.NomeFile)); // i documenti dell'area INPS
        Assert.Equal("Area", _vm.TipoDettaglio);
    }

    [Fact]
    public async Task SvuotandoIlCampoConUnaCartellaSelezionata_TornaIlForm()
    {
        await PreparaAsync();
        Cartella("INPS", "Contributi").IsSelected = true;
        await _vm.CaricamentoFormCompletato;
        await CercaAsync("zincato");

        _vm.PulisciRicercaCommand.Execute(null);
        await _vm.CaricamentoFormCompletato;

        Assert.Equal("", _vm.TestoRicerca);
        Assert.Equal("Contributi", _vm.FormCartella!.Titolo);
        Assert.Null(_vm.ElencoDocumenti);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TestoVuotoOSoloSpazi_NonFaPartireNessunaRicerca(string testo)
    {
        await PreparaAsync();

        _vm.TestoRicerca = testo;
        await _vm.RicercaCompletata;

        Assert.Empty(_ricerca.Richieste);
        Assert.False(_vm.TestoRicercaAttivo);
        Assert.False(_vm.InRicerca);
    }

    [Fact]
    public async Task IlTestoCercatoSiPassaSenzaSpaziAiLati()
    {
        await PreparaAsync();

        await CercaAsync("  zincato  ");

        Assert.Equal(["zincato"], _ricerca.Richieste);
    }

    // ---------- Attesa mentre si scrive e ricerche sovrapposte ----------

    [Fact]
    public async Task MentreSiScrive_SiCercaSoloDopoUnaBrevePausa_NonAdOgniLettera()
    {
        await PreparaAsync();
        _vm.RitardoRicerca = TimeSpan.FromMilliseconds(250);

        _vm.TestoRicerca = "z";
        _vm.TestoRicerca = "zi";
        _vm.TestoRicerca = "zin";
        _vm.TestoRicerca = "zinc";
        await _vm.RicercaCompletata;

        Assert.Equal(["zinc"], _ricerca.Richieste);
        Assert.Equal(["preventivo.txt"], _vm.ElencoDocumenti!.Documenti.Select(d => d.NomeFile));
    }

    [Fact]
    public async Task InvioOIlPulsanteCercaSubito_SenzaAspettare()
    {
        await PreparaAsync();
        _vm.RitardoRicerca = TimeSpan.FromMinutes(5); // se aspettasse, il test non finirebbe

        _vm.TestoRicerca = "zincato";
        await _vm.CercaCommand.ExecuteAsync(null);

        Assert.Equal(["zincato"], _ricerca.Richieste);
        Assert.True(_vm.InRicerca);
    }

    [Fact]
    public async Task UnaRicercaPiuLentaNonSovrascriveQuellaPiuRecente()
    {
        await PreparaAsync();
        _ricerca.Attese["zincato"] = TimeSpan.FromMilliseconds(400);

        _vm.TestoRicerca = "zincato";                       // lenta, parte subito
        var prima = _vm.RicercaCompletata;
        _vm.TestoRicerca = "f24";                           // veloce, parte dopo
        await _vm.RicercaCompletata;
        await prima;
        await Task.Delay(600); // il tempo che la vecchia finirebbe

        Assert.Equal("f24", _vm.TestoRicerca);
        Assert.Equal(["f24.txt"], _vm.ElencoDocumenti!.Documenti.Select(d => d.NomeFile));
        Assert.Equal("1 documento trovato per «f24»", _vm.RiepilogoDettaglio);
    }

    [Fact]
    public async Task SeLaRicercaSiSvuotaMentreÈInCorso_NonCompareNessunRisultato()
    {
        await PreparaAsync();
        _ricerca.Attese["zincato"] = TimeSpan.FromMilliseconds(300);

        _vm.TestoRicerca = "zincato";
        _vm.TestoRicerca = "";
        await Task.Delay(500);

        Assert.False(_vm.InRicerca);
    }

    // ---------- Uscire dalla ricerca ----------

    [Fact]
    public async Task ScegliendoUnAltroElementoDellAlbero_LaRicercaFinisce()
    {
        await PreparaAsync();
        await CercaAsync("zincato");

        Area("INPS").IsSelected = true;
        await _vm.CaricamentoElencoCompletato;

        Assert.Equal("", _vm.TestoRicerca);
        Assert.False(_vm.InRicerca);
        Assert.Equal("INPS", _vm.TitoloDettaglio);
        Assert.Equal(["f24.txt"], _vm.ElencoDocumenti!.Documenti.Select(d => d.NomeFile));
    }

    [Fact]
    public async Task DoppioClicSuUnRisultato_FinisceLaRicerca_EApreLaCartellaDelDocumento()
    {
        await PreparaAsync();
        await CercaAsync("zincato");
        var riga = _vm.ElencoDocumenti!.Documenti.Single();

        riga.VaiAllaCartellaCommand.Execute(null);
        await _vm.CaricamentoFormCompletato;

        Assert.Equal("", _vm.TestoRicerca);
        Assert.False(_vm.InRicerca);
        Assert.Same(Cartella("Fatture", "Fattura Rossi"), _vm.NodoSelezionato);
        Assert.True(Area("Fatture").IsExpanded);
        Assert.Equal("Fattura Rossi", _vm.FormCartella!.Titolo);
    }

    [Fact]
    public async Task DoppioClicSuUnRisultato_DellaCartellaGiaSelezionata_ApreComunqueIlForm()
    {
        await PreparaAsync();
        Cartella("Fatture", "Fattura Rossi").IsSelected = true;
        await _vm.CaricamentoFormCompletato;
        await CercaAsync("zincato");

        _vm.ElencoDocumenti!.Documenti.Single().VaiAllaCartellaCommand.Execute(null);
        await _vm.CaricamentoFormCompletato;

        Assert.False(_vm.InRicerca);
        Assert.Equal("Fattura Rossi", _vm.FormCartella!.Titolo);
    }

    // ---------- Durante la ricerca ----------

    [Fact]
    public async Task RileggendoLAlbero_LaRicercaResta_EIRisultatiSiAggiornano()
    {
        await PreparaAsync();
        await CercaAsync("zincato");
        await _a.Servizio.AllegaDocumentiAsync(
            Cartella("INPS", "Contributi").Id, [_a.Tmp.CreaFile("s/altro zincato.txt")]);

        await _vm.AggiornaCommand.ExecuteAsync(null);
        await _vm.CaricamentoElencoCompletato;

        Assert.Equal("zincato", _vm.TestoRicerca);
        Assert.True(_vm.InRicerca);
        Assert.Equal(["altro zincato.txt", "preventivo.txt"], _vm.ElencoDocumenti!.Documenti.Select(d => d.NomeFile).OrderBy(n => n));
    }

    [Fact]
    public async Task EliminandoUnRisultato_SiAggiornanoIContatoriEIlRiepilogo()
    {
        await PreparaAsync();
        await CercaAsync("txt");
        var prima = _vm.ElencoDocumenti!.Documenti.Count;
        var riga = _vm.ElencoDocumenti.Documenti.First(d => d.NomeFile == "f24.txt");

        await riga.EliminaCommand.ExecuteAsync(null);

        Assert.Equal(prima - 1, _vm.ElencoDocumenti.Documenti.Count);
        Assert.Equal(0, Cartella("INPS", "Contributi").NumeroDocumenti);
        Assert.Contains($"{prima - 1} documenti trovati", _vm.RiepilogoDettaglio);
    }

    [Fact]
    public async Task UnErroreDellaRicerca_SiMostraSenzaChiudereIlProgramma()
    {
        await PreparaAsync();
        _ricerca.Errore = new InvalidOperationException("database occupato");

        await CercaAsync("zincato");

        Assert.Contains("database occupato", Assert.Single(_a.Dialog.Errori));
        Assert.False(_vm.InRicerca);
    }

    [Fact]
    public async Task ConTroppiRisultati_IlRiepilogoSuggerisceDiRestringere()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        var nomi = Enumerable.Range(1, 3).Select(i => $"doc{i}.txt").ToArray();
        await _a.CreaCartellaInAreaAsync(area, "C", nomi);
        await _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));
        await _vm.InizializzaAsync();

        var risultati = new ElencoDocumentiViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, null, null,
            new LimitatoreDiRicerca(_a.Ricerca, massimo: 2), "doc");
        await risultati.CaricaAsync();

        Assert.True(risultati.Troncato);
        Assert.Equal(2, risultati.Documenti.Count);
    }

    // ---------- Senza servizio di ricerca ----------

    [Fact]
    public async Task SenzaServizioDiRicerca_IlCampoNonCompare_EScrivereNonFaNulla()
    {
        var vm = new MainViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni);
        await vm.InizializzaAsync();

        vm.TestoRicerca = "qualcosa";

        Assert.False(vm.RicercaDisponibile);
        Assert.False(vm.InRicerca);
    }

    [Fact]
    public void ConIlServizio_IlCampoCompare() => Assert.True(_vm.RicercaDisponibile);

    // ---------- Stato della lettura in background ----------

    [Fact]
    public async Task LoStatoDellaLettura_SiVedeNellaBarra_ESparisceQuandoFinisce()
    {
        await PreparaAsync();
        Assert.Equal("", _vm.TestoIndicizzazione);

        _monitor.Imposta(new StatoCodaIndicizzazione(1, "fattura.pdf", 0));
        Assert.Equal("Lettura del testo: «fattura.pdf»", _vm.TestoIndicizzazione);

        _monitor.Imposta(new StatoCodaIndicizzazione(4, "fattura.pdf", 0));
        Assert.Equal("Lettura del testo: «fattura.pdf» e altri 3 in coda", _vm.TestoIndicizzazione);
        Assert.Contains("la ricerca non trova ancora il loro contenuto", _vm.DettaglioIndicizzazione);

        _monitor.Imposta(new StatoCodaIndicizzazione(2, null, 0));
        Assert.Equal("Lettura del testo: 2 documenti in coda", _vm.TestoIndicizzazione);

        _monitor.Imposta(StatoCodaIndicizzazione.Inattivo);
        Assert.Equal("", _vm.TestoIndicizzazione);
        Assert.Equal("", _vm.DettaglioIndicizzazione);
    }

    [Fact]
    public async Task SeManca_LOcr_LaBarraDiceQuantiDocumentiAspettano_ELaCausa()
    {
        await PreparaAsync();

        _monitor.Imposta(new StatoCodaIndicizzazione(0, null, 1));
        Assert.Equal("1 documento da leggere con il riconoscimento del testo (OCR), non disponibile", _vm.TestoIndicizzazione);

        _monitor.Imposta(new StatoCodaIndicizzazione(0, null, 5));
        Assert.Equal("5 documenti da leggere con il riconoscimento del testo (OCR), non disponibile", _vm.TestoIndicizzazione);
        Assert.Contains("lingua italiana", _vm.DettaglioIndicizzazione);
    }

    [Fact]
    public async Task LaLetturaInCorso_HaLaPrecedenzaSuiDocumentiInAttesaDellOcr()
    {
        await PreparaAsync();

        _monitor.Imposta(new StatoCodaIndicizzazione(2, "a.txt", 3));

        Assert.StartsWith("Lettura del testo", _vm.TestoIndicizzazione);
    }

    [Fact]
    public async Task SenzaMonitor_LaBarraRestaVuota()
    {
        var vm = new MainViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni);
        await vm.InizializzaAsync();

        Assert.Equal("", vm.TestoIndicizzazione);
    }
}

/// <summary>Una ricerca che impone un massimo di risultati più basso, per provare il messaggio di "troppi risultati".</summary>
public class LimitatoreDiRicerca(IRicercaService vera, int massimo) : IRicercaService
{
    public Task<EsitoRicerca> CercaAsync(string testo, FiltriRicerca? filtri = null, int _ = 1000) => vera.CercaAsync(testo, filtri, massimo);
}
