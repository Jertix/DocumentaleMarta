using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

/// <summary>Avvisi di scadenza: icone dell'albero, nodo "Scadenze", colori delle righe, riepilogo all'avvio.</summary>
public class AvvisiViewModelTests : IDisposable
{
    private readonly ArchivioDiProva _a = new();
    private readonly TempoFisso _tempo = new(new DateTime(2026, 10, 1, 9, 0, 0));
    private readonly ImpostazioniApp _impostazioni;
    private readonly AlertService _avvisi;
    private readonly MainViewModel _vm;

    public AvvisiViewModelTests()
    {
        _impostazioni = new ImpostazioniApp { PercorsoRadice = _a.Radice };
        _avvisi = new AlertService(_impostazioni, _tempo);
        _vm = new MainViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni, _avvisi);
    }

    public void Dispose() => _a.Dispose();

    private static DateOnly Tra(int giorni) => new DateOnly(2026, 10, 1).AddDays(giorni);

    private NodoAlberoViewModel Radice => _vm.Radici.Single();
    private NodoAlberoViewModel Scadenze => Radice.Figli.Single(f => f.Tipo == TipoNodo.Scadenze);
    private NodoAlberoViewModel Area(string nome) => Radice.Aree.Single(a => a.Nome == nome);
    private NodoAlberoViewModel Cartella(string area, string titolo) => Area(area).Figli.Single(c => c.Nome == titolo);

    /// <summary>
    /// Fatture: "Urgente" tra 3 giorni (rosso), "Prossima" tra 20 (arancione), "Lontana" tra 90, "Senza" senza scadenza.
    /// INPS: "Vecchia" scaduta da 5 giorni (rosso) ma completata, "Libera" senza scadenze.
    /// </summary>
    private async Task PreparaAsync()
    {
        var fatture = await _a.Servizio.CreaAreaAsync("Fatture");
        var inps = await _a.Servizio.CreaAreaAsync("INPS");
        await _a.CreaCartellaInAreaAsync(fatture, "Urgente", ["u.pdf"], Tra(3));
        await _a.CreaCartellaInAreaAsync(fatture, "Prossima", ["p.pdf"], Tra(20));
        await _a.CreaCartellaInAreaAsync(fatture, "Lontana", [], Tra(90));
        await _a.CreaCartellaInAreaAsync(fatture, "Senza", []);
        var vecchia = await _a.CreaCartellaInAreaAsync(inps, "Vecchia", [], Tra(-5));
        await _a.Servizio.AggiornaCartellaAsync(vecchia.Id, vecchia.Dati with { Completato = true, DataCompletamento = Tra(-6) });
        await _a.CreaCartellaInAreaAsync(inps, "Libera", []);
        _impostazioni.RiepilogoAvvio = false; // il riepilogo si prova nei test dedicati
        await _vm.InizializzaAsync();
        await _vm.CaricamentoElencoCompletato;
    }

    // ---------- Icone dell'albero ----------

    [Fact]
    public async Task OgniCartellaHaLoStatoDellaSuaScadenza()
    {
        await PreparaAsync();

        Assert.Equal(StatoAvviso.Rosso, Cartella("Fatture", "Urgente").Avviso);
        Assert.Equal(StatoAvviso.Arancione, Cartella("Fatture", "Prossima").Avviso);
        Assert.Equal(StatoAvviso.Nessuno, Cartella("Fatture", "Lontana").Avviso);
        Assert.Equal(StatoAvviso.Nessuno, Cartella("Fatture", "Senza").Avviso);
        Assert.Equal(StatoAvviso.Nessuno, Cartella("INPS", "Vecchia").Avviso); // scaduta ma completata
        Assert.Equal(StatoAvviso.Nessuno, Cartella("INPS", "Libera").Avviso);
    }

    [Fact]
    public async Task LeAreeELaRadiceMostranoIlPiuGraveTraQuelloCheContengono()
    {
        await PreparaAsync();

        Assert.Equal(StatoAvviso.Rosso, Area("Fatture").Avviso);   // rosso > arancione
        Assert.Equal(StatoAvviso.Nessuno, Area("INPS").Avviso);
        Assert.Equal(StatoAvviso.Rosso, Radice.Avviso);
        Assert.Equal(StatoAvviso.Rosso, Scadenze.Avviso);
    }

    [Fact]
    public async Task SeC_ESoloArancione_LAreaELaRadiceSonoArancioni()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        await _a.CreaCartellaInAreaAsync(area, "P", [], Tra(15));
        _impostazioni.RiepilogoAvvio = false;
        await _vm.InizializzaAsync();

        Assert.Equal(StatoAvviso.Arancione, Area("A").Avviso);
        Assert.Equal(StatoAvviso.Arancione, Radice.Avviso);
    }

    [Fact]
    public async Task IlSuggerimentoDellIconaSpiegaQuantoManca()
    {
        await PreparaAsync();

        Assert.Equal("Scade tra 3 giorni", Cartella("Fatture", "Urgente").DescrizioneAvviso);
        Assert.Equal("Scade tra 20 giorni", Cartella("Fatture", "Prossima").DescrizioneAvviso);
        Assert.Equal("", Cartella("Fatture", "Lontana").DescrizioneAvviso);
        Assert.Equal("2 cartelle con scadenze da controllare", Area("Fatture").DescrizioneAvviso);
        Assert.Equal("2 cartelle con scadenze da controllare", Radice.DescrizioneAvviso);
        Assert.Equal("", Area("INPS").DescrizioneAvviso);
    }

    [Fact]
    public async Task UnaSolaCartellaInAvviso_IlSuggerimentoEAlSingolare()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        await _a.CreaCartellaInAreaAsync(area, "P", [], Tra(2));
        _impostazioni.RiepilogoAvvio = false;
        await _vm.InizializzaAsync();

        Assert.Equal("1 cartella con una scadenza da controllare", Area("A").DescrizioneAvviso);
    }

    [Fact]
    public async Task IlNodoScadenzeStaInCima_ConIlNumeroDiCartelleInAvviso()
    {
        await PreparaAsync();

        Assert.Same(Scadenze, Radice.Figli[0]);
        Assert.Equal(2, Scadenze.NumeroAvvisi);
        Assert.Equal("Scadenze (2)", Scadenze.Testo);
        Assert.Equal(TipoNodo.Scadenze, Scadenze.Tipo);
    }

    [Fact]
    public async Task SenzaAvvisi_IlNodoScadenzeNonHaIlNumero()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        await _a.CreaCartellaInAreaAsync(area, "Lontana", [], Tra(200));
        _impostazioni.RiepilogoAvvio = false;
        await _vm.InizializzaAsync();

        Assert.Equal("Scadenze", Scadenze.Testo);
        Assert.Equal(StatoAvviso.Nessuno, Scadenze.Avviso);
    }

    [Fact]
    public async Task AvvisiDisattivati_NonCeIlNodoScadenze_NeIcone()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        await _a.CreaCartellaInAreaAsync(area, "Urgente", [], Tra(1));
        var spenti = new ImpostazioniApp { PercorsoRadice = _a.Radice, AvvisiAttivi = false };
        var vm = new MainViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, spenti, new AlertService(spenti, _tempo));

        await vm.InizializzaAsync();

        Assert.DoesNotContain(vm.Radici.Single().Figli, f => f.Tipo == TipoNodo.Scadenze);
        Assert.Equal(StatoAvviso.Nessuno, vm.Radici.Single().Avviso);
        Assert.Equal(StatoAvviso.Nessuno, vm.Radici.Single().Aree.Single().Avviso);
        Assert.Empty(_a.Dialog.Conferme); // e nemmeno il riepilogo all'avvio
    }

    [Fact]
    public async Task UnAreaChiamataScadenze_ConvivConIlNodoSpeciale_ESoloUnaAreaPuoEssereCosi()
    {
        _impostazioni.RiepilogoAvvio = false;
        await _vm.InizializzaAsync();

        _a.Dialog.RispondiTesto("Scadenze");
        await _vm.NuovaAreaCommand.ExecuteAsync(null);

        Assert.Empty(_a.Dialog.ErroriValidazione); // il nodo speciale non conta come "area già esistente"
        Assert.Equal("Scadenze", Assert.Single(Radice.Aree).Nome);
        Assert.Equal(TipoNodo.Scadenze, Radice.Figli[0].Tipo);

        _a.Dialog.RispondiTesto("scadenze");
        await _vm.NuovaAreaCommand.ExecuteAsync(null);

        Assert.Contains("Esiste già", Assert.Single(_a.Dialog.ErroriValidazione)); // ma due aree uguali no
    }

    // ---------- Aggiornamenti sul posto ----------

    [Fact]
    public async Task ModificandoLaScadenzaDalForm_LeIconeSiAggiornanoSenzaRicostruireLAlbero()
    {
        await PreparaAsync();
        var nodo = Cartella("Fatture", "Lontana");
        nodo.IsSelected = true;
        await _vm.CaricamentoFormCompletato;

        _vm.FormCartella!.DataScadenza = new DateTime(2026, 10, 3); // tra 2 giorni
        await _vm.FormCartella.AttendiSalvataggioAsync();

        Assert.Same(nodo, Cartella("Fatture", "Lontana"));
        Assert.Equal(StatoAvviso.Rosso, nodo.Avviso);
        Assert.Equal("Scade tra 2 giorni", nodo.DescrizioneAvviso);
        Assert.Equal(3, Scadenze.NumeroAvvisi);
        Assert.Equal("Scadenze (3)", Scadenze.Testo);
    }

    [Fact]
    public async Task SpuntandoCompletato_LAvvisoDellaCartellaSparisce_EQuelloDellAreaScende()
    {
        await PreparaAsync();
        Cartella("Fatture", "Urgente").IsSelected = true;
        await _vm.CaricamentoFormCompletato;
        Assert.Equal(StatoAvviso.Rosso, Area("Fatture").Avviso);

        _vm.FormCartella!.Completato = true;
        await _vm.FormCartella.AttendiSalvataggioAsync();

        Assert.Equal(StatoAvviso.Nessuno, Cartella("Fatture", "Urgente").Avviso);
        Assert.Equal(StatoAvviso.Arancione, Area("Fatture").Avviso); // resta "Prossima"
        Assert.Equal(StatoAvviso.Arancione, Radice.Avviso);
        Assert.Equal(1, Scadenze.NumeroAvvisi);
    }

    [Fact]
    public async Task TogliendoLaScadenza_LAvvisoSparisce()
    {
        await PreparaAsync();
        Cartella("Fatture", "Urgente").IsSelected = true;
        await _vm.CaricamentoFormCompletato;

        _vm.FormCartella!.CancellaScadenzaCommand.Execute(null);
        await _vm.FormCartella.AttendiSalvataggioAsync();

        Assert.Equal(StatoAvviso.Nessuno, Cartella("Fatture", "Urgente").Avviso);
    }

    [Fact]
    public async Task CambiandoIlGiorno_AlRitornoNellaFinestraGliStatiSiRifanno()
    {
        await PreparaAsync();
        Assert.Equal(StatoAvviso.Arancione, Cartella("Fatture", "Prossima").Avviso); // tra 20 giorni

        _tempo.Adesso = new DateTime(2026, 10, 15, 8, 0, 0); // due settimane dopo: ora ne mancano 6
        _vm.ControllaCambioData();

        Assert.Equal(StatoAvviso.Rosso, Cartella("Fatture", "Prossima").Avviso);
        Assert.Equal("Scade tra 6 giorni", Cartella("Fatture", "Prossima").DescrizioneAvviso);
    }

    [Fact]
    public async Task StessoGiorno_ControllaCambioDataNonRicostruisceLAlbero()
    {
        await PreparaAsync();
        var prima = Cartella("Fatture", "Urgente");

        _tempo.Adesso = new DateTime(2026, 10, 1, 23, 0, 0);
        _vm.ControllaCambioData();

        Assert.Same(prima, Cartella("Fatture", "Urgente"));
    }

    // ---------- Nodo "Scadenze" ----------

    [Fact]
    public async Task SelezionandoScadenze_SiVedonoLeCartelleInAvviso_DallaPiuUrgente()
    {
        await PreparaAsync();

        Scadenze.IsSelected = true;
        await _vm.CaricamentoElencoCompletato;

        var elenco = _vm.ElencoScadenze!;
        Assert.Equal(["Urgente", "Prossima"], elenco.Righe.Select(r => r.Titolo));
        Assert.Equal([StatoAvviso.Rosso, StatoAvviso.Arancione], elenco.Righe.Select(r => r.Avviso));
        Assert.Equal(["Scade tra 3 giorni", "Scade tra 20 giorni"], elenco.Righe.Select(r => r.Quando));
        Assert.All(elenco.Righe, r => Assert.Equal("Fatture", r.NomeArea));
        Assert.Equal(1, elenco.Righe[0].NumeroDocumenti);
        Assert.Null(_vm.ElencoDocumenti);
        Assert.Null(_vm.FormCartella);
        Assert.True(_vm.RiepilogoVisibile);
    }

    [Fact]
    public async Task LaScadenzeNonMostraLeCartelleCompletateNeQuelleLontane()
    {
        await PreparaAsync();

        Scadenze.IsSelected = true;
        await _vm.CaricamentoElencoCompletato;

        Assert.DoesNotContain(_vm.ElencoScadenze!.Righe, r => r.Titolo is "Vecchia" or "Lontana" or "Senza");
    }

    [Fact]
    public async Task ScadenzeInclude_LeCartelleScadute_ComeRosse()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        await _a.CreaCartellaInAreaAsync(area, "Scaduta", [], Tra(-2));
        _impostazioni.RiepilogoAvvio = false;
        await _vm.InizializzaAsync();

        Scadenze.IsSelected = true;
        await _vm.CaricamentoElencoCompletato;

        var riga = Assert.Single(_vm.ElencoScadenze!.Righe);
        Assert.Equal(StatoAvviso.Rosso, riga.Avviso);
        Assert.Equal("Scaduta da 2 giorni", riga.Quando);
    }

    [Fact]
    public async Task IlRiepilogoDelNodoScadenze_ElencaLeCategorie()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        await _a.CreaCartellaInAreaAsync(area, "Scaduta", [], Tra(-2));
        await _a.CreaCartellaInAreaAsync(area, "Rossa", [], Tra(5));
        await _a.CreaCartellaInAreaAsync(area, "Arancione 1", [], Tra(15));
        await _a.CreaCartellaInAreaAsync(area, "Arancione 2", [], Tra(25));
        _impostazioni.RiepilogoAvvio = false;
        await _vm.InizializzaAsync();

        Scadenze.IsSelected = true;

        Assert.Equal("Promemoria", _vm.TipoDettaglio);
        Assert.Equal("Scadenze", _vm.TitoloDettaglio);
        Assert.Equal(
            "1 cartella scaduta  ·  1 cartella in scadenza entro 7 giorni  ·  2 cartelle in scadenza entro 30 giorni",
            _vm.RiepilogoDettaglio);
        Assert.Equal("", _vm.PercorsoDettaglio);
    }

    [Fact]
    public async Task ScadenzeVuoto_DiceCheNonCeNulla()
    {
        await _a.Servizio.CreaAreaAsync("A");
        _impostazioni.RiepilogoAvvio = false;
        await _vm.InizializzaAsync();

        Scadenze.IsSelected = true;
        await _vm.CaricamentoElencoCompletato;

        Assert.Equal("Nessuna scadenza in arrivo.", _vm.RiepilogoDettaglio);
        Assert.Empty(_vm.ElencoScadenze!.Righe);
        Assert.Contains("30 giorni", _vm.ElencoScadenze.TestoVuoto);
    }

    [Fact]
    public async Task ScadenzeNonHaUnaCartellaSulDisco_NonSiPuoApriInEsplora_NeModificare()
    {
        await PreparaAsync();

        Scadenze.IsSelected = true;

        Assert.False(_vm.HaPercorsoFisico);
        Assert.False(_vm.ApriInEsploraCommand.CanExecute(null));
        Assert.False(_vm.RinominaCommand.CanExecute(null));
        Assert.False(_vm.EliminaCommand.CanExecute(null));
        Assert.False(_vm.NuovaCartellaCommand.CanExecute(null));
        Assert.True(_vm.AggiornaCommand.CanExecute(null));
    }

    [Fact]
    public async Task VaiAllaCartella_DallElencoScadenze_SelezionaLaCartellaEApreIlForm()
    {
        await PreparaAsync();
        Scadenze.IsSelected = true;
        await _vm.CaricamentoElencoCompletato;
        Assert.False(Area("Fatture").IsExpanded);

        _vm.ElencoScadenze!.Righe.Single(r => r.Titolo == "Prossima").VaiAllaCartellaCommand.Execute(null);
        await _vm.CaricamentoFormCompletato;

        Assert.Same(Cartella("Fatture", "Prossima"), _vm.NodoSelezionato);
        Assert.True(Area("Fatture").IsExpanded);
        Assert.Equal("Prossima", _vm.FormCartella!.Titolo);
        Assert.Null(_vm.ElencoScadenze);
    }

    [Fact]
    public async Task IlNodoScadenzeSelezionato_RestaSelezionatoDopoUnAggiornamento()
    {
        await PreparaAsync();
        Scadenze.IsSelected = true;
        await _vm.CaricamentoElencoCompletato;

        await _vm.AggiornaCommand.ExecuteAsync(null);
        await _vm.CaricamentoElencoCompletato;

        Assert.Same(Scadenze, _vm.NodoSelezionato);
        Assert.NotNull(_vm.ElencoScadenze);
    }

    // ---------- Colori delle righe ----------

    [Fact]
    public async Task LeRigheDeiDocumenti_PortanoLUrgenzaDellaScadenzaDellaLoroCartella()
    {
        await PreparaAsync();
        // La radice è già selezionata: il suo elenco è stato caricato dall'avvio.
        Radice.IsSelected = true;
        await _vm.CaricamentoElencoCompletato;

        var righe = _vm.ElencoDocumenti!.Documenti;

        Assert.Equal(StatoAvviso.Rosso, righe.Single(d => d.NomeFile == "u.pdf").Avviso);
        Assert.Equal(StatoAvviso.Arancione, righe.Single(d => d.NomeFile == "p.pdf").Avviso);
    }

    [Fact]
    public async Task LeRigheDiUnaCartellaCompletata_NonHannoColore()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        var d = await _a.CreaCartellaInAreaAsync(area, "Chiusa", ["x.pdf"], Tra(-10));
        await _a.Servizio.AggiornaCartellaAsync(d.Id, d.Dati with { Completato = true, DataCompletamento = Tra(-11) });
        var elenco = new ElencoDocumentiViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, null, _avvisi);

        await elenco.CaricaAsync();

        Assert.Equal(StatoAvviso.Nessuno, elenco.Documenti.Single().Avviso);
    }

    [Fact]
    public async Task SenzaServizioAvvisi_LeRigheNonHannoColore()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        await _a.CreaCartellaInAreaAsync(area, "Urgente", ["x.pdf"], Tra(-1));
        var elenco = new ElencoDocumentiViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, null);

        await elenco.CaricaAsync();

        Assert.Equal(StatoAvviso.Nessuno, elenco.Documenti.Single().Avviso);
    }

    // ---------- Testo accanto al campo Scadenza del form ----------

    [Fact]
    public async Task IlForm_MostraQuantoManca_EAggiornaAlCambiareDellaData()
    {
        var d = await _a.CreaCartellaAsync("A", "P");
        var form = new CartellaFormViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, d, _avvisi);
        Assert.Equal("", form.TestoScadenza);
        Assert.Equal(StatoAvviso.Nessuno, form.StatoScadenza);

        form.DataScadenza = new DateTime(2026, 10, 4);
        Assert.Equal("Scade tra 3 giorni", form.TestoScadenza);
        Assert.Equal(StatoAvviso.Rosso, form.StatoScadenza);

        form.DataScadenza = new DateTime(2026, 10, 21);
        Assert.Equal(StatoAvviso.Arancione, form.StatoScadenza);

        form.DataScadenza = new DateTime(2027, 1, 1);
        Assert.Equal(StatoAvviso.Nessuno, form.StatoScadenza);
        Assert.Equal("Scade tra 92 giorni", form.TestoScadenza); // il testo resta, ma grigio

        form.DataScadenza = null;
        Assert.Equal("", form.TestoScadenza);
    }

    [Fact]
    public async Task IlForm_DiUnaCartellaCompletata_NonMostraAvvisi()
    {
        var d = await _a.CreaCartellaAsync("A", "P");
        var form = new CartellaFormViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, d, _avvisi);
        form.DataScadenza = new DateTime(2026, 10, 2);
        Assert.Equal(StatoAvviso.Rosso, form.StatoScadenza);

        form.Completato = true;

        Assert.Equal(StatoAvviso.Nessuno, form.StatoScadenza);
        Assert.Equal("", form.TestoScadenza);

        form.Completato = false;
        Assert.Equal(StatoAvviso.Rosso, form.StatoScadenza);
    }

    [Fact]
    public async Task IlForm_SenzaServizioAvvisi_NonMostraNulla()
    {
        var d = await _a.CreaCartellaAsync("A", "P");
        var form = new CartellaFormViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, d);

        form.DataScadenza = new DateTime(2026, 10, 2);

        Assert.Equal("", form.TestoScadenza);
    }

    // ---------- Riepilogo all'avvio ----------

    [Fact]
    public async Task All_Avvio_SeCeNeSonoLeScadenze_ElencaLeCategorie_EOffreLElenco()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        await _a.CreaCartellaInAreaAsync(area, "Scaduta 1", [], Tra(-2));
        await _a.CreaCartellaInAreaAsync(area, "Scaduta 2", [], Tra(-9));
        await _a.CreaCartellaInAreaAsync(area, "Rossa", [], Tra(0));
        await _a.CreaCartellaInAreaAsync(area, "Arancione", [], Tra(12));
        await _a.CreaCartellaInAreaAsync(area, "Lontana", [], Tra(100));
        _a.Dialog.RispostaConferma = false;

        await _vm.InizializzaAsync();

        var messaggio = Assert.Single(_a.Dialog.Conferme);
        Assert.Contains("2 cartelle scadute", messaggio);
        Assert.Contains("1 cartella in scadenza entro 7 giorni", messaggio);
        Assert.Contains("1 cartella in scadenza entro 30 giorni", messaggio);
        Assert.Contains("Vuoi vedere l'elenco?", messaggio);
        Assert.Same(Radice, _vm.NodoSelezionato); // ha risposto di no
    }

    [Fact]
    public async Task All_Avvio_RispondendoSi_SiVaAlNodoScadenze()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        await _a.CreaCartellaInAreaAsync(area, "Rossa", [], Tra(3));
        _a.Dialog.RispostaConferma = true;

        await _vm.InizializzaAsync();
        await _vm.CaricamentoElencoCompletato;

        Assert.Same(Scadenze, _vm.NodoSelezionato);
        Assert.Equal(["Rossa"], _vm.ElencoScadenze!.Righe.Select(r => r.Titolo));
    }

    [Fact]
    public async Task All_Avvio_SenzaScadenze_NonDiceNulla()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        await _a.CreaCartellaInAreaAsync(area, "Lontana", [], Tra(100));
        var seconda = await _a.Servizio.CreaAreaAsync("B");
        var chiusa = await _a.CreaCartellaInAreaAsync(seconda, "Chiusa", [], Tra(-50));
        await _a.Servizio.AggiornaCartellaAsync(chiusa.Id, chiusa.Dati with { Completato = true, DataCompletamento = Tra(-51) });

        await _vm.InizializzaAsync();

        Assert.Empty(_a.Dialog.Conferme);
    }

    [Fact]
    public async Task All_Avvio_ConIlRiepilogoDisattivato_NonDiceNulla()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        await _a.CreaCartellaInAreaAsync(area, "Rossa", [], Tra(1));
        _impostazioni.RiepilogoAvvio = false;

        await _vm.InizializzaAsync();

        Assert.Empty(_a.Dialog.Conferme);
        Assert.Equal(StatoAvviso.Rosso, Radice.Avviso); // le icone ci sono comunque
    }

    [Fact]
    public async Task IlRiepilogoSoloAllAvvio_NonAdOgniAggiornamento()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        await _a.CreaCartellaInAreaAsync(area, "Rossa", [], Tra(1));
        _a.Dialog.RispostaConferma = false;
        await _vm.InizializzaAsync();

        await _vm.AggiornaCommand.ExecuteAsync(null);

        Assert.Single(_a.Dialog.Conferme);
    }

    [Fact]
    public async Task AlRiepilogo_LaSogliaRossaDiUnGiornoSiScriveAlSingolare()
    {
        var impostazioni = new ImpostazioniApp { PercorsoRadice = _a.Radice, SogliaRossaGiorni = 1, SogliaArancioneGiorni = 10 };
        var vm = new MainViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, impostazioni, new AlertService(impostazioni, _tempo));
        var area = await _a.Servizio.CreaAreaAsync("A");
        await _a.CreaCartellaInAreaAsync(area, "Rossa", [], Tra(1));
        _a.Dialog.RispostaConferma = false;

        await vm.InizializzaAsync();

        Assert.Contains("in scadenza entro 1 giorno\n", Assert.Single(_a.Dialog.Conferme) + "\n");
    }
}
