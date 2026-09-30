using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.App.ViewModels;

/// <param name="avvisi">Decide quali scadenze segnalare; se manca lo si crea con le soglie delle impostazioni.</param>
/// <param name="ricerca">Senza, il campo di ricerca non compare.</param>
/// <param name="monitor">Lo stato della lettura dei documenti in background, per la barra in fondo alla finestra.</param>
/// <param name="ocr">Serve solo a spiegare perché manca il riconoscimento del testo.</param>
public partial class MainViewModel(
    IArchivioService archivio,
    IArchivioFileService files,
    IDialogService dialog,
    IShellService shell,
    ImpostazioniApp impostazioni,
    AlertService? avvisi = null,
    IRicercaService? ricerca = null,
    IMonitorIndicizzazione? monitor = null,
    IOcr? ocr = null) : ObservableObject
{
    private readonly AlertService _avvisi = avvisi ?? new AlertService(impostazioni);

    private bool _caricamentoInCorso;
    private bool _ripristinandoSelezione;
    private DateOnly _dataUltimoCalcolo;

    /// <summary>Contiene sempre e solo la radice "Tutti i documenti" (il TreeView vuole una lista).</summary>
    public ObservableCollection<NodoAlberoViewModel> Radici { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaSelezione), nameof(RadiceSelezionata), nameof(AreaSelezionata),
        nameof(PuoCreareCartella), nameof(PuoModificare), nameof(HaPercorsoFisico), nameof(RiepilogoVisibile),
        nameof(TipoDettaglio), nameof(TitoloDettaglio), nameof(RiepilogoDettaglio), nameof(PercorsoDettaglio))]
    [NotifyCanExecuteChangedFor(nameof(NuovaCartellaCommand), nameof(RinominaCommand),
        nameof(EliminaCommand), nameof(ApriInEsploraCommand))]
    private NodoAlberoViewModel? _nodoSelezionato;

    // ---------- Cosa c'è nel pannello di destra ----------

    /// <summary>Il form della cartella selezionata; null quando è selezionato altro.</summary>
    [ObservableProperty]
    private CartellaFormViewModel? _formCartella;

    /// <summary>
    /// L'elenco dei documenti della radice (tutti) o dell'area selezionata, oppure i risultati della ricerca;
    /// null quando è selezionato altro.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InRicerca), nameof(RiepilogoVisibile), nameof(HaPercorsoFisico),
        nameof(TipoDettaglio), nameof(TitoloDettaglio), nameof(RiepilogoDettaglio), nameof(PercorsoDettaglio))]
    [NotifyCanExecuteChangedFor(nameof(ApriInEsploraCommand))]
    private ElencoDocumentiViewModel? _elencoDocumenti;

    /// <summary>L'elenco delle scadenze, quando è selezionato il nodo "Scadenze"; null altrimenti.</summary>
    [ObservableProperty]
    private ScadenzeViewModel? _elencoScadenze;

    /// <summary>L'intestazione con il riepilogo si vede per radice, aree e "Scadenze" (non per le cartelle, che hanno il loro form).</summary>
    public bool RiepilogoVisibile => InRicerca || NodoSelezionato is { Tipo: not TipoNodo.Cartella };

    private Task _caricamentoForm = Task.CompletedTask;
    private Task _caricamentoElenco = Task.CompletedTask;

    /// <summary>Completa quando il form della selezione corrente è pronto (serve ai test).</summary>
    public Task CaricamentoFormCompletato => _caricamentoForm;

    /// <summary>Completa quando l'elenco (documenti o scadenze) della selezione corrente è pronto (serve ai test).</summary>
    public Task CaricamentoElencoCompletato => _caricamentoElenco;

    partial void OnNodoSelezionatoChanged(NodoAlberoViewModel? value)
    {
        FormCartella = null;
        ElencoDocumenti = null;
        ElencoScadenze = null;
        _caricamentoForm = Task.CompletedTask;
        _caricamentoElenco = Task.CompletedTask;

        // Se c'è una ricerca in corso (es. l'albero è stato riletto) si mostrano di nuovo i suoi risultati.
        if (TestoRicercaAttivo && value is not null)
        {
            _caricamentoElenco = _ricerca = EseguiRicercaAsync(TestoRicerca.Trim(), TimeSpan.Zero);
            return;
        }

        CaricaPannello(value);
    }

    /// <summary>Carica il pannello di destra che spetta al nodo: il form di una cartella, la griglia di un'area o l'elenco delle scadenze.</summary>
    private void CaricaPannello(NodoAlberoViewModel? nodo)
    {
        switch (nodo?.Tipo)
        {
            case TipoNodo.Cartella:
                _caricamentoForm = CaricaFormAsync(nodo);
                break;
            case TipoNodo.Radice or TipoNodo.Area:
                _caricamentoElenco = CaricaElencoAsync(nodo);
                break;
            case TipoNodo.Scadenze:
                _caricamentoElenco = CaricaScadenzeAsync(nodo);
                break;
        }
    }

    private async Task CaricaElencoAsync(NodoAlberoViewModel nodo)
    {
        try
        {
            var elenco = new ElencoDocumentiViewModel(
                archivio, files, dialog, shell, nodo.Tipo == TipoNodo.Area ? nodo.Id : null, _avvisi);
            await elenco.CaricaAsync();
            if (!ReferenceEquals(NodoSelezionato, nodo) || TestoRicercaAttivo)
                return; // nel frattempo l'utente ha scelto un altro elemento o ha iniziato una ricerca

            CollegaElenco(elenco);
            ElencoDocumenti = elenco;
        }
        catch (Exception ex)
        {
            // Come per il form: nessuno attende questo compito, quindi l'errore va mostrato qui.
            dialog.MostraErrore($"Non è stato possibile caricare i documenti: {ex.Message}");
        }
    }

    private async Task CaricaScadenzeAsync(NodoAlberoViewModel nodo)
    {
        try
        {
            var elenco = new ScadenzeViewModel(archivio, _avvisi);
            await elenco.CaricaAsync();
            if (!ReferenceEquals(NodoSelezionato, nodo) || TestoRicercaAttivo)
                return;

            elenco.VaiAllaCartellaRichiesto += VaiAllaCartella;
            ElencoScadenze = elenco;
        }
        catch (Exception ex)
        {
            dialog.MostraErrore($"Non è stato possibile caricare le scadenze: {ex.Message}");
        }
    }

    private async Task CaricaFormAsync(NodoAlberoViewModel nodo)
    {
        try
        {
            var dettaglio = await archivio.CaricaCartellaAsync(nodo.Id);
            if (!ReferenceEquals(NodoSelezionato, nodo) || TestoRicercaAttivo)
                return; // nel frattempo l'utente ha scelto un altro elemento o ha iniziato una ricerca

            if (dettaglio is null)
            {
                dialog.MostraErrore("La cartella non esiste più nell'archivio.");
                await RicaricaAsync();
                return;
            }

            var form = new CartellaFormViewModel(archivio, files, dialog, shell, dettaglio, _avvisi);
            form.TitoloSalvato += (titolo, percorso) => nodo.Rinomina(titolo, percorso);
            form.NumeroDocumentiCambiato += numero => nodo.ImpostaNumeroDocumenti(numero);
            form.DatiSalvati += dati =>
            {
                // Scadenza o "completato" sono cambiati: le icone di avviso dell'albero vanno rifatte.
                nodo.ImpostaScadenza(dati.DataScadenza, dati.Completato);
                RicalcolaAvvisi();
            };
            form.RicaricaRichiesta += () => _ = RicaricaSicuraAsync();
            FormCartella = form;
        }
        catch (Exception ex)
        {
            // Nessuno attende questo compito (parte da un cambio di selezione): l'errore va mostrato qui o andrebbe perso.
            dialog.MostraErrore($"Non è stato possibile aprire la cartella: {ex.Message}");
        }
    }

    /// <summary>Seleziona nell'albero la cartella indicata, aprendo i rami che la contengono.</summary>
    private void VaiAllaCartella(int cartellaId)
    {
        var nodo = Radici.SelectMany(r => r.ConDiscendenti())
            .FirstOrDefault(n => n.Chiave == NodoAlberoViewModel.CreaChiave(TipoNodo.Cartella, cartellaId));
        if (nodo is null)
        {
            // L'elenco mostrava un elemento di una cartella che nell'albero non c'è più.
            _ = RicaricaSicuraAsync();
            return;
        }

        // Dai risultati di una ricerca si va alla cartella: la ricerca finisce. Se la cartella è già quella selezionata
        // la selezione non cambia e il pannello va ricaricato da qui.
        if (TestoRicercaAttivo)
            EsciDallaRicerca(ricarica: nodo.IsSelected);

        for (var p = nodo.Padre; p is not null; p = p.Padre)
            p.IsExpanded = true;
        nodo.IsSelected = true;
    }

    private void OnDocumentoEliminato(int cartellaId)
    {
        var nodo = Radici.SelectMany(r => r.ConDiscendenti())
            .FirstOrDefault(n => n.Chiave == NodoAlberoViewModel.CreaChiave(TipoNodo.Cartella, cartellaId));
        nodo?.ImpostaNumeroDocumenti(Math.Max(0, nodo.NumeroDocumenti - 1));

        // Il riepilogo sopra la griglia ("N documenti") dipende dai contatori dei nodi.
        OnPropertyChanged(nameof(RiepilogoDettaglio));
    }

    private async Task RicaricaSicuraAsync()
    {
        try { await RicaricaAsync(); }
        catch (Exception ex) { dialog.MostraErrore($"Non è stato possibile aggiornare l'elenco: {ex.Message}"); }
    }

    // ---------- Intestazione e piè di pagina ----------

    public string TestoAzienda
    {
        get
        {
            var a = impostazioni.Azienda;
            var parti = new[]
            {
                a.RagioneSociale,
                Se(a.CodiceFiscale, "C.F. "),
                Se(a.PartitaIva, "P.IVA "),
                a.Indirizzo
            };
            return string.Join("  -  ", parti.Where(p => !string.IsNullOrWhiteSpace(p)));

            static string Se(string valore, string prefisso) =>
                string.IsNullOrWhiteSpace(valore) ? "" : prefisso + valore;
        }
    }

    public string DescrizioneAzienda => impostazioni.Azienda.Descrizione;

    // ---------- Cosa si può fare con la selezione (governa menu e pulsanti) ----------

    public bool HaSelezione => NodoSelezionato is not null;
    public bool RadiceSelezionata => NodoSelezionato?.Tipo == TipoNodo.Radice;
    public bool AreaSelezionata => NodoSelezionato?.Tipo == TipoNodo.Area;

    /// <summary>Da un'area o da una sua cartella si può aggiungere una cartella (nella stessa area).</summary>
    public bool PuoCreareCartella => NodoSelezionato?.Tipo is TipoNodo.Area or TipoNodo.Cartella;

    public bool PuoModificare => NodoSelezionato?.Tipo is TipoNodo.Area or TipoNodo.Cartella;

    /// <summary>Il nodo "Scadenze" non corrisponde a nessuna cartella su disco.</summary>
    public bool HaPercorsoFisico => !InRicerca && NodoSelezionato is { Tipo: not TipoNodo.Scadenze };

    // ---------- Intestazione del pannello di destra ----------

    public string TipoDettaglio => InRicerca ? "Ricerca" : NodoSelezionato?.Tipo switch
    {
        TipoNodo.Radice => "Archivio",
        TipoNodo.Scadenze => "Promemoria",
        TipoNodo.Area => "Area",
        TipoNodo.Cartella => "Cartella",
        _ => ""
    };

    public string TitoloDettaglio => InRicerca ? "Risultati" : NodoSelezionato?.Nome ?? "";

    public string RiepilogoDettaglio => ElencoDocumenti is { IsRicerca: true } risultati
        ? RiepilogoRicerca(risultati)
        : NodoSelezionato switch
    {
        { Tipo: TipoNodo.Radice } n =>
            $"{Conta(n.Aree.Count(), "area", "aree")}  ·  {Conta(n.NumeroCartelle, "cartella", "cartelle")}  ·  {Conta(n.NumeroDocumenti, "documento", "documenti")}",
        { Tipo: TipoNodo.Area } n =>
            $"{Conta(n.NumeroCartelle, "cartella", "cartelle")}  ·  {Conta(n.NumeroDocumenti, "documento", "documenti")}",
        { Tipo: TipoNodo.Cartella } n => Conta(n.NumeroDocumenti, "documento", "documenti"),
        { Tipo: TipoNodo.Scadenze } => DescriviAvvisi(ContaAvvisi()) is { Count: > 0 } righe
            ? string.Join("  ·  ", righe)
            : "Nessuna scadenza in arrivo.",
        _ => ""
    };

    public string PercorsoDettaglio =>
        !InRicerca && NodoSelezionato is { Tipo: not TipoNodo.Scadenze } n ? files.PercorsoAssoluto(n.PercorsoRelativo) : "";

    // ---------- Caricamento ----------

    /// <summary>Carica l'albero e, se le impostazioni lo prevedono, segnala le scadenze in arrivo.</summary>
    public Task InizializzaAsync() => EseguiAsync(async () =>
    {
        SeguiIndicizzazione();
        await RicaricaAsync();
        MostraRiepilogoAvvio();
    });

    /// <summary>
    /// Rilegge l'albero dal database conservando i rami aperti. Seleziona <paramref name="chiaveDaSelezionare"/>
    /// (o, se manca, la selezione corrente; se non esiste più, la radice).
    /// </summary>
    private async Task RicaricaAsync(string? chiaveDaSelezionare = null)
    {
        var aree = await archivio.CaricaAlberoAsync();

        var espansi = Radici.SelectMany(r => r.ConDiscendenti()).Where(n => n.IsExpanded).Select(n => n.Chiave).ToHashSet();
        var primoCaricamento = Radici.Count == 0;
        var chiave = chiaveDaSelezionare ?? NodoSelezionato?.Chiave;

        _caricamentoInCorso = true;
        NodoAlberoViewModel radice;
        try
        {
            radice = new NodoAlberoViewModel(TipoNodo.Radice, 0, impostazioni.NomeRadice, "", null, Seleziona);

            // Il nodo "Scadenze" sta in cima, prima delle aree, e non c'è se gli avvisi sono disattivati.
            if (_avvisi.Attivo)
                radice.Figli.Add(new NodoAlberoViewModel(TipoNodo.Scadenze, 0, "Scadenze", "", radice, Seleziona));

            foreach (var area in aree)
            {
                var nodoArea = new NodoAlberoViewModel(TipoNodo.Area, area.Id, area.Nome, area.PercorsoRelativo, radice, Seleziona);
                foreach (var cartella in area.Cartelle)
                {
                    var nodoCartella = new NodoAlberoViewModel(
                        TipoNodo.Cartella, cartella.Id, cartella.Titolo, cartella.PercorsoRelativo, nodoArea, Seleziona);
                    nodoCartella.ImpostaNumeroDocumenti(cartella.NumeroDocumenti);
                    nodoCartella.ImpostaScadenza(cartella.DataScadenza, cartella.Completato);
                    nodoArea.Figli.Add(nodoCartella);
                }
                radice.Figli.Add(nodoArea);
            }

            foreach (var nodo in radice.ConDiscendenti())
                nodo.IsExpanded = espansi.Contains(nodo.Chiave);
            if (primoCaricamento)
                radice.IsExpanded = true;

            Radici.Clear();
            Radici.Add(radice);
            RicalcolaAvvisi();
        }
        finally
        {
            _caricamentoInCorso = false;
        }

        var daSelezionare = radice.ConDiscendenti().FirstOrDefault(n => n.Chiave == chiave) ?? radice;
        for (var p = daSelezionare.Padre; p is not null; p = p.Padre)
            p.IsExpanded = true;

        // I nodi sono nuovi, quindi IsSelected passa sempre da false a true e scatta Seleziona.
        // Non è l'utente che sceglie un altro elemento: una ricerca in corso non deve finire.
        _ripristinandoSelezione = true;
        try
        {
            NodoSelezionato = null;
            daSelezionare.IsSelected = true;
        }
        finally
        {
            _ripristinandoSelezione = false;
        }
    }

    private void Seleziona(NodoAlberoViewModel nodo)
    {
        if (_caricamentoInCorso)
            return;

        // Chi sceglie un altro elemento dell'albero ha finito di cercare.
        if (TestoRicercaAttivo && !_ripristinandoSelezione)
            EsciDallaRicerca(ricarica: false);

        var precedente = NodoSelezionato;
        NodoSelezionato = nodo;
        if (precedente is not null && !ReferenceEquals(precedente, nodo))
            precedente.IsSelected = false;
    }

    // ---------- Avvisi di scadenza ----------

    /// <summary>
    /// Rifà le icone di avviso dell'albero: ogni cartella ha il suo stato, ogni area e la radice il più grave
    /// tra quelli che contengono, e il nodo "Scadenze" il conteggio totale.
    /// </summary>
    private void RicalcolaAvvisi()
    {
        _dataUltimoCalcolo = _avvisi.Oggi;
        if (Radice is not { } radice)
            return;

        var statoRadice = StatoAvviso.Nessuno;
        var cartelleInAvviso = 0;

        foreach (var area in radice.Aree)
        {
            var statoArea = StatoAvviso.Nessuno;
            var inAvvisoNellArea = 0;

            foreach (var cartella in area.Figli)
            {
                var stato = _avvisi.Valuta(cartella.DataScadenza, cartella.Completato);
                cartella.Avviso = stato;
                cartella.DescrizioneAvviso = stato == StatoAvviso.Nessuno ? "" : _avvisi.Descrivi(cartella.DataScadenza!.Value);

                statoArea = AlertService.Peggiore(statoArea, stato);
                if (stato != StatoAvviso.Nessuno)
                    inAvvisoNellArea++;
            }

            area.Avviso = statoArea;
            area.DescrizioneAvviso = DescrizioneRiassuntivaAvvisi(inAvvisoNellArea);
            statoRadice = AlertService.Peggiore(statoRadice, statoArea);
            cartelleInAvviso += inAvvisoNellArea;
        }

        radice.Avviso = statoRadice;
        radice.DescrizioneAvviso = DescrizioneRiassuntivaAvvisi(cartelleInAvviso);

        if (radice.Figli.FirstOrDefault(f => f.Tipo == TipoNodo.Scadenze) is { } scadenze)
        {
            scadenze.Avviso = statoRadice;
            scadenze.NumeroAvvisi = cartelleInAvviso;
            scadenze.DescrizioneAvviso = DescrizioneRiassuntivaAvvisi(cartelleInAvviso);
        }

        OnPropertyChanged(nameof(RiepilogoDettaglio));
    }

    /// <summary>
    /// Da chiamare quando la finestra torna in primo piano: se nel frattempo è cambiato il giorno (il PC è rimasto
    /// acceso per la notte) gli stati delle scadenze vanno rifatti.
    /// </summary>
    public void ControllaCambioData()
    {
        if (Radici.Count > 0 && _avvisi.Oggi != _dataUltimoCalcolo)
            _ = RicaricaSicuraAsync();
    }

    private readonly record struct ConteggioAvvisi(int Scadute, int InScadenzaRossa, int InScadenzaArancione)
    {
        public int Totale => Scadute + InScadenzaRossa + InScadenzaArancione;
    }

    private ConteggioAvvisi ContaAvvisi()
    {
        int scadute = 0, rosse = 0, arancioni = 0;
        foreach (var cartella in Radici.SelectMany(r => r.Aree).SelectMany(a => a.Figli))
        {
            switch (cartella.Avviso)
            {
                case StatoAvviso.Rosso when cartella.DataScadenza is { } data && _avvisi.Giorni(data) < 0:
                    scadute++;
                    break;
                case StatoAvviso.Rosso:
                    rosse++;
                    break;
                case StatoAvviso.Arancione:
                    arancioni++;
                    break;
            }
        }
        return new ConteggioAvvisi(scadute, rosse, arancioni);
    }

    /// <summary>"2 cartelle scadute", "1 cartella in scadenza entro 7 giorni"... una riga per ogni categoria non vuota.</summary>
    private List<string> DescriviAvvisi(ConteggioAvvisi conteggio)
    {
        var righe = new List<string>();
        if (conteggio.Scadute > 0)
            righe.Add(Conta(conteggio.Scadute, "cartella scaduta", "cartelle scadute"));
        if (conteggio.InScadenzaRossa > 0)
            righe.Add($"{Conta(conteggio.InScadenzaRossa, "cartella", "cartelle")} in scadenza entro {Giorni(_avvisi.SogliaRossaGiorni)}");
        if (conteggio.InScadenzaArancione > 0)
            righe.Add($"{Conta(conteggio.InScadenzaArancione, "cartella", "cartelle")} in scadenza entro {Giorni(_avvisi.SogliaArancioneGiorni)}");
        return righe;
    }

    private static string DescrizioneRiassuntivaAvvisi(int cartelle) => cartelle switch
    {
        0 => "",
        1 => "1 cartella con una scadenza da controllare",
        _ => $"{cartelle} cartelle con scadenze da controllare"
    };

    /// <summary>All'apertura: se ci sono scadenze da controllare lo dice e offre di andare all'elenco.</summary>
    private void MostraRiepilogoAvvio()
    {
        if (!_avvisi.Attivo || !impostazioni.RiepilogoAvvio)
            return;

        var righe = DescriviAvvisi(ContaAvvisi());
        if (righe.Count == 0)
            return;

        var messaggio = "Ci sono scadenze da controllare:\n\n"
                        + string.Join("\n", righe.Select(r => "•  " + r))
                        + "\n\nVuoi vedere l'elenco?";
        if (dialog.Conferma("Scadenze", messaggio))
            VaiAlleScadenze();
    }

    private void VaiAlleScadenze()
    {
        if (Radice?.Figli.FirstOrDefault(f => f.Tipo == TipoNodo.Scadenze) is { } nodo)
            nodo.IsSelected = true;
    }

    // ---------- Ricerca ----------

    /// <summary>Il campo di ricerca compare solo se c'è un servizio di ricerca.</summary>
    public bool RicercaDisponibile => ricerca is not null;

    /// <summary>Quanto si aspetta, dopo l'ultimo carattere digitato, prima di cercare (per non cercare a ogni tasto).</summary>
    public TimeSpan RitardoRicerca { get; set; } = TimeSpan.FromMilliseconds(350);

    /// <summary>Quello che l'utente ha scritto nel campo di ricerca.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TestoRicercaAttivo))]
    private string _testoRicerca = "";

    /// <summary>C'è qualcosa da cercare (il campo non è vuoto né fatto di soli spazi).</summary>
    public bool TestoRicercaAttivo => !string.IsNullOrWhiteSpace(TestoRicerca);

    /// <summary>Sono mostrati i risultati di una ricerca.</summary>
    public bool InRicerca => ElencoDocumenti is { IsRicerca: true };

    private CancellationTokenSource? _ricercaInCorso;
    private bool _sopprimiRicerca;
    private Task _ricerca = Task.CompletedTask;

    /// <summary>Completa quando l'ultima ricerca avviata ha mostrato i suoi risultati (serve ai test).</summary>
    public Task RicercaCompletata => _ricerca;

    partial void OnTestoRicercaChanged(string value)
    {
        if (_sopprimiRicerca)
            return;

        AnnullaRicercaInCorso();
        if (!TestoRicercaAttivo)
        {
            // Campo svuotato: si torna a mostrare ciò che spetta all'elemento selezionato.
            ElencoDocumenti = null;
            CaricaPannello(NodoSelezionato);
            return;
        }

        _ricerca = EseguiRicercaAsync(value.Trim(), RitardoRicerca);
    }

    /// <summary>Cerca subito, senza aspettare (tasto Invio o pulsante "Cerca").</summary>
    [RelayCommand]
    private Task CercaAsync()
    {
        AnnullaRicercaInCorso();
        if (!TestoRicercaAttivo)
            return Task.CompletedTask;

        return _ricerca = EseguiRicercaAsync(TestoRicerca.Trim(), TimeSpan.Zero);
    }

    [RelayCommand]
    private void PulisciRicerca() => TestoRicerca = "";

    private void AnnullaRicercaInCorso()
    {
        _ricercaInCorso?.Cancel();
        _ricercaInCorso = null;
    }

    private async Task EseguiRicercaAsync(string testo, TimeSpan attesa)
    {
        if (ricerca is null)
            return;

        // Una sola ricerca alla volta conta: quella più recente. (Il CancellationTokenSource non ha risorse da rilasciare.)
        AnnullaRicercaInCorso();
        var annullamento = new CancellationTokenSource();
        _ricercaInCorso = annullamento;
        var token = annullamento.Token;

        try
        {
            if (attesa > TimeSpan.Zero)
                await Task.Delay(attesa, token);

            var risultati = new ElencoDocumentiViewModel(archivio, files, dialog, shell, null, _avvisi, ricerca, testo);
            await risultati.CaricaAsync();
            if (token.IsCancellationRequested)
                return; // nel frattempo il testo è cambiato: vale la ricerca più recente

            FormCartella = null;
            ElencoScadenze = null;
            CollegaElenco(risultati);
            ElencoDocumenti = risultati;
        }
        catch (OperationCanceledException)
        {
            // Ricerca sostituita da una più recente.
        }
        catch (Exception ex)
        {
            // Come per gli altri caricamenti in background: nessuno attende questo compito, l'errore si mostra qui.
            dialog.MostraErrore($"La ricerca non è riuscita: {ex.Message}");
        }
        finally
        {
            if (ReferenceEquals(_ricercaInCorso, annullamento))
                _ricercaInCorso = null;
        }
    }

    /// <summary>Svuota il campo di ricerca senza far partire altro; se richiesto ricarica il pannello dell'elemento selezionato.</summary>
    private void EsciDallaRicerca(bool ricarica)
    {
        AnnullaRicercaInCorso();

        _sopprimiRicerca = true;
        try { TestoRicerca = ""; }
        finally { _sopprimiRicerca = false; }

        if (ElencoDocumenti is { IsRicerca: true })
            ElencoDocumenti = null;
        if (ricarica)
            CaricaPannello(NodoSelezionato);
    }

    private string RiepilogoRicerca(ElencoDocumentiViewModel risultati)
    {
        var testo = $"{Conta(risultati.Documenti.Count, "documento trovato", "documenti trovati")} per «{risultati.TestoRicerca}»";
        return risultati.Troncato ? testo + " (ce ne sono altri: scrivi più parole per restringere la ricerca)" : testo;
    }

    /// <summary>Gli eventi delle griglie (doppio clic, eliminazione...) sono gli stessi per documenti di un'area e risultati di ricerca.</summary>
    private void CollegaElenco(ElencoDocumentiViewModel elenco)
    {
        elenco.VaiAllaCartellaRichiesto += VaiAllaCartella;
        elenco.DocumentoEliminato += OnDocumentoEliminato;
        elenco.RicaricaRichiesta += () => _ = RicaricaSicuraAsync();
    }

    // ---------- Stato della lettura dei documenti in background ----------

    /// <summary>Il messaggio nella barra in fondo alla finestra; vuoto quando non c'è nulla da dire.</summary>
    [ObservableProperty]
    private string _testoIndicizzazione = "";

    /// <summary>Spiegazione più lunga, per il suggerimento che compare passando il mouse sul messaggio.</summary>
    [ObservableProperty]
    private string _dettaglioIndicizzazione = "";

    private bool _indicizzazioneSeguita;
    private SynchronizationContext? _contestoInterfaccia;

    private void SeguiIndicizzazione()
    {
        if (monitor is null || _indicizzazioneSeguita)
            return;
        _indicizzazioneSeguita = true;

        // Lo stato cambia su un thread in background: si torna al thread dell'interfaccia prima di toccare le proprietà.
        _contestoInterfaccia = SynchronizationContext.Current;
        monitor.Cambiato += () =>
        {
            if (_contestoInterfaccia is null)
                AggiornaIndicizzazione();
            else
                _contestoInterfaccia.Post(_ => AggiornaIndicizzazione(), null);
        };
        AggiornaIndicizzazione();
    }

    private void AggiornaIndicizzazione()
    {
        if (monitor is null)
            return;

        var stato = monitor.Stato;
        if (stato.InCoda > 0)
        {
            TestoIndicizzazione = stato.InElaborazione is { } nome
                ? stato.InCoda > 1
                    ? $"Lettura del testo: «{nome}» e altri {stato.InCoda - 1} in coda"
                    : $"Lettura del testo: «{nome}»"
                : $"Lettura del testo: {Conta(stato.InCoda, "documento", "documenti")} in coda";
            DettaglioIndicizzazione = "I documenti appena allegati si leggono in background: finché non hanno finito " +
                                      "la ricerca non trova ancora il loro contenuto (il nome del file sì).";
        }
        else if (stato.InAttesaOcr > 0)
        {
            TestoIndicizzazione = $"{Conta(stato.InAttesaOcr, "documento", "documenti")} da leggere con il riconoscimento del testo (OCR), non disponibile";
            DettaglioIndicizzazione = ocr?.MotivoNonDisponibile ?? "Il riconoscimento del testo (OCR) non è disponibile su questo PC.";
        }
        else
        {
            TestoIndicizzazione = "";
            DettaglioIndicizzazione = "";
        }
    }

    // ---------- Comandi ----------

    [RelayCommand]
    private async Task NuovaAreaAsync()
    {
        var nome = dialog.ChiediTesto(
            "Nuova area", "Nome della nuova area (ad esempio Fatture o INPS):", "",
            testo => ErroreNomeArea(testo, escludi: null));
        if (nome is null)
            return;

        await EseguiAsync(async () =>
        {
            var id = await archivio.CreaAreaAsync(nome);
            await RicaricaAsync(NodoAlberoViewModel.CreaChiave(TipoNodo.Area, id));
        });
    }

    [RelayCommand(CanExecute = nameof(PuoCreareCartella))]
    private async Task NuovaCartellaAsync()
    {
        var area = NodoSelezionato?.Tipo == TipoNodo.Area ? NodoSelezionato : NodoSelezionato?.Padre;
        if (area is null)
            return;

        // Se la creazione fallisce (es. un file scelto non si riesce a leggere) la finestra si riapre con
        // gli stessi dati, così l'utente non deve riscrivere tutto.
        var modello = new NuovaCartellaViewModel(dialog, area.Nome);
        while (dialog.MostraNuovaCartella(modello))
        {
            try
            {
                var creata = await archivio.CreaCartellaConDatiAsync(area.Id, modello.Dati, modello.PercorsiFile);
                await RicaricaAsync(NodoAlberoViewModel.CreaChiave(TipoNodo.Cartella, creata.Id));
                return;
            }
            catch (ArchivioException ex)
            {
                dialog.MostraErrore(ex.Message);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                dialog.MostraErrore($"Non è stato possibile creare la cartella: {ex.Message}\n\nNon è stato creato nulla.");
            }
        }
    }

    [RelayCommand(CanExecute = nameof(PuoModificare))]
    private async Task RinominaAsync()
    {
        if (NodoSelezionato is not { Tipo: TipoNodo.Area or TipoNodo.Cartella } nodo)
            return;

        var eArea = nodo.Tipo == TipoNodo.Area;
        var nuovo = dialog.ChiediTesto(
            eArea ? "Rinomina area" : "Rinomina cartella",
            eArea ? "Nuovo nome dell'area:" : "Nuovo titolo della cartella:",
            nodo.Nome,
            testo => eArea
                ? ErroreNomeArea(testo, escludi: nodo)
                : ValidazioneNomi.Errore(testo, ValidazioneNomi.LunghezzaMassimaTitolo));
        if (nuovo is null)
            return;

        await EseguiAsync(async () =>
        {
            if (eArea)
                await archivio.RinominaAreaAsync(nodo.Id, nuovo);
            else
                await archivio.RinominaCartellaAsync(nodo.Id, nuovo);
            await RicaricaAsync(nodo.Chiave);
        });
    }

    [RelayCommand(CanExecute = nameof(PuoModificare))]
    private async Task EliminaAsync()
    {
        if (NodoSelezionato is not { Tipo: TipoNodo.Area or TipoNodo.Cartella } nodo)
            return;

        var eArea = nodo.Tipo == TipoNodo.Area;
        if (!dialog.Conferma(eArea ? "Elimina area" : "Elimina cartella", MessaggioEliminazione(nodo)))
            return;

        await EseguiAsync(async () =>
        {
            if (eArea)
                await archivio.EliminaAreaAsync(nodo.Id);
            else
                await archivio.EliminaCartellaAsync(nodo.Id);

            var successivo = nodo.Padre ?? Radici.FirstOrDefault();
            await RicaricaAsync(successivo?.Chiave);
        });
    }

    [RelayCommand(CanExecute = nameof(HaPercorsoFisico))]
    private void ApriInEsplora()
    {
        if (NodoSelezionato is not { Tipo: not TipoNodo.Scadenze } nodo)
            return;

        var percorso = files.PercorsoAssoluto(nodo.PercorsoRelativo);
        if (!Directory.Exists(percorso))
        {
            dialog.MostraErrore($"La cartella non esiste sul disco:\n{percorso}");
            return;
        }

        try
        {
            shell.ApriCartella(percorso);
        }
        catch (Win32Exception ex)
        {
            dialog.MostraErrore($"Impossibile aprire Esplora file: {ex.Message}");
        }
    }

    /// <summary>Rilegge tutto dall'archivio (utile se i file sono stati toccati fuori dal programma).</summary>
    [RelayCommand]
    private Task AggiornaAsync() => EseguiAsync(() => RicaricaAsync());

    // ---------- Supporto ----------

    private NodoAlberoViewModel? Radice => Radici.FirstOrDefault();

    private string? ErroreNomeArea(string testo, NodoAlberoViewModel? escludi)
    {
        if (ValidazioneNomi.Errore(testo, ValidazioneNomi.LunghezzaMassimaArea) is { } errore)
            return errore;

        var nome = testo.Trim();
        var giaUsato = Radice?.Aree.Any(a =>
            !ReferenceEquals(a, escludi) && string.Equals(a.Nome, nome, StringComparison.OrdinalIgnoreCase)) == true;
        return giaUsato ? $"Esiste già un'area chiamata «{nome}»." : null;
    }

    private static string MessaggioEliminazione(NodoAlberoViewModel nodo)
    {
        var contenuto = new List<string>();
        if (nodo.Tipo == TipoNodo.Area && nodo.NumeroCartelle > 0)
            contenuto.Add(Conta(nodo.NumeroCartelle, "cartella", "cartelle"));
        if (nodo.NumeroDocumenti > 0)
            contenuto.Add(Conta(nodo.NumeroDocumenti, "documento", "documenti"));

        var tipo = nodo.Tipo == TipoNodo.Area ? "l'area" : "la cartella";
        var testo = $"Eliminare {tipo} «{nodo.Nome}»?";
        if (contenuto.Count > 0)
            testo += $"\n\nVerranno eliminati anche: {string.Join(" e ", contenuto)}.";
        return testo + "\n\nI file vengono spostati nel Cestino di Windows.";
    }

    private static string Conta(int numero, string singolare, string plurale) =>
        $"{numero} {(numero == 1 ? singolare : plurale)}";

    private static string Giorni(int numero) => Conta(numero, "giorno", "giorni");

    /// <summary>Esegue un'operazione trasformando gli errori prevedibili in messaggi per l'utente.</summary>
    private async Task EseguiAsync(Func<Task> operazione)
    {
        try
        {
            await operazione();
        }
        catch (ArchivioException ex)
        {
            dialog.MostraErrore(ex.Message);
            await RicaricaAsync(); // l'albero mostrato potrebbe essere vecchio (es. elemento già eliminato)
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialog.MostraErrore(
                $"Operazione non riuscita: {ex.Message}\n\nControlla che i file e le cartelle non siano aperti in un altro programma.");
            await RicaricaAsync();
        }
        catch (OperationCanceledException)
        {
            // L'utente ha annullato una finestra di Windows (es. il Cestino): nessun messaggio.
        }
    }
}
