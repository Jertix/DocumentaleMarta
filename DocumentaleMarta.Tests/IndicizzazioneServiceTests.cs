using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;

namespace DocumentaleMarta.Tests;

public class IndicizzazioneServiceTests : IDisposable
{
    private readonly FintoEstrattore _estrattore = new(".txt", ".pdf");
    private readonly ArchivioDiProva _a;

    public IndicizzazioneServiceTests() => _a = new ArchivioDiProva(_estrattore);

    public void Dispose() => _a.Dispose();

    private IndicizzazioneService Indicizzazione => _a.Indicizzazione!;

    /// <summary>Attende la fine del lavoro in background, ma senza restare appesi per sempre se qualcosa non va.</summary>
    private Task AttendiAsync() => Indicizzazione.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));

    // ---------- Il lavoro di base ----------

    [Fact]
    public async Task UnDocumentoAllegato_VieneLettoInBackground_ELoTestoEntraNellIndice()
    {
        var d = await _a.CreaCartellaAsync("Fatture", "Pratica", "fattura.txt");
        await AttendiAsync();

        var id = d.Documenti.Single().Id;
        Assert.Equal(StatoIndicizzazione.Indicizzato, _a.StatoDi(id));
        Assert.Equal("contenuto di fattura.txt", _a.TestoIndicizzato(id));
    }

    [Fact]
    public async Task AllegandoAUnaCartellaEsistente_IDocumentiVengonoLetti()
    {
        var d = await _a.CreaCartellaAsync("Fatture", "Pratica");
        var nuovi = await _a.Servizio.AllegaDocumentiAsync(d.Id, [_a.Tmp.CreaFile("s/uno.txt"), _a.Tmp.CreaFile("s/due.txt")]);
        await AttendiAsync();

        Assert.All(nuovi, n => Assert.Equal(StatoIndicizzazione.Indicizzato, _a.StatoDi(n.Id)));
        Assert.Equal(2, _a.RigheNellIndice());
    }

    [Fact]
    public async Task IDocumentiSiLeggonoUnoAllaVolta_NellOrdineInCuiSonoArrivati()
    {
        await _a.CreaCartellaAsync("A", "C", "primo.txt", "secondo.txt", "terzo.txt");
        await AttendiAsync();

        Assert.Equal(["primo.txt", "secondo.txt", "terzo.txt"], _estrattore.Letti);
    }

    [Fact]
    public async Task TestoVuoto_IlDocumentoEIndicizzatoMaNonHaRigheNellIndice()
    {
        _estrattore.Logica = (_, _) => Task.FromResult("   ");
        var d = await _a.CreaCartellaAsync("A", "C", "vuoto.txt");
        await AttendiAsync();

        Assert.Equal(StatoIndicizzazione.Indicizzato, _a.StatoDi(d.Documenti.Single().Id));
        Assert.Equal(0, _a.RigheNellIndice());
    }

    [Fact]
    public async Task TipoDiFileNonSupportato_StatoNonSupportato()
    {
        var d = await _a.CreaCartellaAsync("A", "C", "vecchio.doc");
        await AttendiAsync();

        Assert.Equal(StatoIndicizzazione.NonSupportato, _a.StatoDi(d.Documenti.Single().Id));
        Assert.Empty(_estrattore.Letti);
        Assert.Equal(0, _a.RigheNellIndice());
    }

    [Fact]
    public async Task TestoTroppoLungo_SiIndicizzaSoloLaPrimaParte()
    {
        _estrattore.Logica = (_, _) => Task.FromResult(new string('a', 2_500_000));
        var d = await _a.CreaCartellaAsync("A", "C", "enorme.txt");
        await AttendiAsync();

        Assert.Equal(2_000_000, _a.TestoIndicizzato(d.Documenti.Single().Id)!.Length);
    }

    [Fact]
    public async Task UnIdCheNonEsiste_VieneIgnorato_EGliAltriProseguono()
    {
        Indicizzazione.Accoda([9999]);
        var d = await _a.CreaCartellaAsync("A", "C", "buono.txt");
        await AttendiAsync();

        Assert.Equal(StatoIndicizzazione.Indicizzato, _a.StatoDi(d.Documenti.Single().Id));
    }

    // ---------- Errori ----------

    [Fact]
    public async Task UnErroreDiLettura_StatoErrore_EGliAltriDocumentiProseguono()
    {
        _estrattore.Logica = (p, _) => Path.GetFileName(p) == "rotto.txt"
            ? throw new InvalidDataException("file rovinato")
            : Task.FromResult("ok");
        var d = await _a.CreaCartellaAsync("A", "C", "rotto.txt", "sano.txt");
        await AttendiAsync();

        Assert.Equal(StatoIndicizzazione.Errore, _a.StatoDi(d.Documenti[0].Id));
        Assert.Equal(StatoIndicizzazione.Indicizzato, _a.StatoDi(d.Documenti[1].Id));
        Assert.Null(_a.TestoIndicizzato(d.Documenti[0].Id));
    }

    [Fact]
    public async Task FileSparitoDalDisco_StatoErrore()
    {
        _estrattore.Logica = (p, _) => Task.FromResult(File.ReadAllText(p)); // come farebbe un estrattore vero
        var d = await _a.CreaCartellaAsync("A", "C", "x.txt");
        await AttendiAsync();
        File.Delete(_a.Fisico(d.Documenti.Single().PercorsoRelativo));

        Indicizzazione.Accoda([d.Documenti.Single().Id]);
        await AttendiAsync();

        Assert.Equal(StatoIndicizzazione.Errore, _a.StatoDi(d.Documenti.Single().Id));
    }

    [Fact]
    public async Task AlProssimoAvvio_ILeggiamoDiNuovoIDocumentiInErrore_EQuelliDaFare_MaNonGliAltri()
    {
        _estrattore.Logica = (p, _) => Path.GetFileName(p) == "rotto.txt"
            ? throw new InvalidDataException("momentaneo")
            : Task.FromResult("ok");
        var d = await _a.CreaCartellaAsync("A", "C", "rotto.txt", "sano.txt", "altro.doc");
        await AttendiAsync();
        var lettiPrima = _estrattore.Letti.Count;
        _estrattore.Logica = (_, _) => Task.FromResult("ora funziona");

        await Indicizzazione.AccodaPendentiAsync();
        await AttendiAsync();

        Assert.Equal(StatoIndicizzazione.Indicizzato, _a.StatoDi(d.Documenti[0].Id));
        Assert.Equal("ora funziona", _a.TestoIndicizzato(d.Documenti[0].Id));
        Assert.Equal("ok", _a.TestoIndicizzato(d.Documenti[1].Id)); // quello già fatto non si rilegge
        Assert.Equal(lettiPrima + 1, _estrattore.Letti.Count);
    }

    // ---------- OCR non disponibile ----------

    [Fact]
    public async Task SeServeLOcrEnonCe_IlDocumentoRestaDaFare_EloStatoLoDice()
    {
        _estrattore.Logica = (_, _) => throw new OcrNonDisponibileException("manca la lingua");
        var d = await _a.CreaCartellaAsync("A", "C", "scansione.pdf");
        await AttendiAsync();

        var id = d.Documenti.Single().Id;
        Assert.Equal(StatoIndicizzazione.DaIndicizzare, _a.StatoDi(id));
        Assert.Null(_a.TestoIndicizzato(id));
        Assert.Equal(1, Indicizzazione.Stato.InAttesaOcr);
        Assert.Equal(0, Indicizzazione.Stato.InCoda);
    }

    [Fact]
    public async Task QuandoLOcrTornaDisponibile_ILDocumentoSiLegge()
    {
        _estrattore.Logica = (_, _) => throw new OcrNonDisponibileException("manca la lingua");
        var d = await _a.CreaCartellaAsync("A", "C", "scansione.pdf");
        await AttendiAsync();
        _estrattore.Logica = (_, _) => Task.FromResult("testo della scansione");

        await Indicizzazione.AccodaPendentiAsync(); // all'avvio successivo
        await AttendiAsync();

        var id = d.Documenti.Single().Id;
        Assert.Equal(StatoIndicizzazione.Indicizzato, _a.StatoDi(id));
        Assert.Equal("testo della scansione", _a.TestoIndicizzato(id));
        Assert.Equal(0, Indicizzazione.Stato.InAttesaOcr);
    }

    // ---------- Stato e coda ----------

    [Fact]
    public async Task LoStatoMostraIlFileInLavorazioneEQuantiNeRestano_EAlTermineTornaInattivo()
    {
        var inLavorazione = new TaskCompletionSource();
        var rilascia = new TaskCompletionSource();
        StatoCodaIndicizzazione? durante = null;
        _estrattore.Logica = async (_, _) =>
        {
            durante = Indicizzazione.Stato;
            inLavorazione.TrySetResult();
            await rilascia.Task;
            return "ok";
        };

        await _a.CreaCartellaAsync("A", "C", "primo.txt", "secondo.txt");
        await inLavorazione.Task.WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(new StatoCodaIndicizzazione(2, "primo.txt", 0), durante);
        Assert.Equal(2, Indicizzazione.Stato.InCoda);

        rilascia.SetResult();
        await AttendiAsync();

        Assert.Equal(StatoCodaIndicizzazione.Inattivo, Indicizzazione.Stato);
    }

    [Fact]
    public async Task LoStatoCambiaENotifica_AdOgniPasso()
    {
        var notifiche = 0;
        Indicizzazione.Cambiato += () => Interlocked.Increment(ref notifiche);

        await _a.CreaCartellaAsync("A", "C", "uno.txt");
        await AttendiAsync();

        Assert.True(notifiche >= 3, $"Attese almeno 3 notifiche (accodato, in lavorazione, finito), arrivate {notifiche}.");
    }

    [Fact]
    public async Task LoStessoDocumentoAccodatoDueVolte_SiLeggeUnaVoltaSola()
    {
        var inLavorazione = new TaskCompletionSource();
        var rilascia = new TaskCompletionSource();
        _estrattore.Logica = async (_, _) => { inLavorazione.TrySetResult(); await rilascia.Task; return "ok"; };
        var d = await _a.CreaCartellaAsync("A", "C", "uno.txt");
        await inLavorazione.Task.WaitAsync(TimeSpan.FromSeconds(20));
        var id = d.Documenti.Single().Id;

        Indicizzazione.Accoda([id]); // è già in lavorazione
        Indicizzazione.Accoda([id, id]);
        rilascia.SetResult();
        await AttendiAsync();

        Assert.Single(_estrattore.Letti);
    }

    [Fact]
    public async Task UnDocumentoGiaLetto_RiaccodatoEsplicitamente_SostituisceIlTesto()
    {
        var d = await _a.CreaCartellaAsync("A", "C", "uno.txt");
        await AttendiAsync();
        var id = d.Documenti.Single().Id;
        _estrattore.Logica = (_, _) => Task.FromResult("testo aggiornato");

        Indicizzazione.Accoda([id]);
        await AttendiAsync();

        Assert.Equal("testo aggiornato", _a.TestoIndicizzato(id));
        Assert.Equal(1, _a.RigheNellIndice()); // una sola riga per documento
    }

    // ---------- Eliminazioni ----------

    [Fact]
    public async Task DocumentoEliminatoMentreLoSiLegge_NienteRigheOrfaneNellIndice_EIlServizioProsegue()
    {
        var inLavorazione = new TaskCompletionSource();
        var rilascia = new TaskCompletionSource();
        _estrattore.Logica = async (p, _) =>
        {
            if (Path.GetFileName(p) == "primo.txt")
            {
                inLavorazione.TrySetResult();
                await rilascia.Task;
            }
            return "ok";
        };
        var d = await _a.CreaCartellaAsync("A", "C", "primo.txt", "secondo.txt");
        await inLavorazione.Task.WaitAsync(TimeSpan.FromSeconds(20));

        await _a.Servizio.EliminaDocumentoAsync(d.Documenti[0].Id); // sparisce mentre lo si sta leggendo
        rilascia.SetResult();
        await AttendiAsync();

        Assert.Null(_a.TestoIndicizzato(d.Documenti[0].Id));
        Assert.Equal("ok", _a.TestoIndicizzato(d.Documenti[1].Id));
        Assert.Equal(1, _a.RigheNellIndice());
    }

    [Fact]
    public async Task EliminandoDocumentoCartellaOArea_IlTestoSparisceDallIndice()
    {
        var d1 = await _a.CreaCartellaAsync("Area1", "C1", "a.txt", "b.txt");
        var d2 = await _a.CreaCartellaAsync("Area2", "C2", "c.txt");
        await AttendiAsync();
        Assert.Equal(3, _a.RigheNellIndice());

        await _a.Servizio.EliminaDocumentoAsync(d1.Documenti[0].Id);
        Assert.Equal(2, _a.RigheNellIndice());

        await _a.Servizio.EliminaCartellaAsync(d1.Id);
        Assert.Equal(1, _a.RigheNellIndice());

        await _a.Servizio.EliminaAreaAsync(d2.AreaId);
        Assert.Equal(0, _a.RigheNellIndice());
    }

    // ---------- Chiusura ----------

    [Fact]
    public async Task Dispose_MentreUnDocumentoEInLettura_LoAnnullaENonResta_InSospeso()
    {
        var inLavorazione = new TaskCompletionSource();
        var annullato = new TaskCompletionSource();
        _estrattore.Logica = async (_, cancellation) =>
        {
            inLavorazione.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, cancellation); } // rispetta l'annullamento, come gli estrattori veri
            catch (OperationCanceledException) { annullato.TrySetResult(); throw; }
            return "";
        };
        var d = await _a.CreaCartellaAsync("A", "C", "lungo.txt");
        await inLavorazione.Task.WaitAsync(TimeSpan.FromSeconds(20));

        Indicizzazione.Dispose();

        await annullato.Task.WaitAsync(TimeSpan.FromSeconds(20));
        // Un documento interrotto dalla chiusura non diventa "errore": al prossimo avvio si riprova.
        Assert.Equal(StatoIndicizzazione.DaIndicizzare, _a.StatoDi(d.Documenti.Single().Id));
    }

    [Fact]
    public void Dispose_SiPuoChiamareDueVolte()
    {
        Indicizzazione.Dispose();
        Indicizzazione.Dispose();
    }

    [Fact]
    public void SenzaAvviare_Accoda_NonFaNulla_EDisposeNonBlocca()
    {
        using var senzaAvvio = new IndicizzazioneService(_a.Factory, _a.Files, [_estrattore]);

        senzaAvvio.Accoda([1, 2]);

        Assert.Equal(2, senzaAvvio.Stato.InCoda);
    }
}
