using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentaleMarta.App.Grafica;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;

namespace DocumentaleMarta.App.ViewModels;

/// <param name="avvisi">Decide quali scadenze segnalare; se manca lo si crea con le soglie delle impostazioni.</param>
/// <param name="ricerca">Senza, il campo di ricerca non compare.</param>
/// <param name="monitor">Lo stato della lettura dei documenti in background, per la barra in fondo alla finestra.</param>
/// <param name="ocr">Serve solo a spiegare perché manca il riconoscimento del testo.</param>
/// <param name="servizioImpostazioni">Salva le modifiche della finestra "Impostazioni"; senza, la finestra non compare.</param>
/// <param name="servizioBackup">Fa il backup dell'archivio; senza, il pulsante "Backup" e il promemoria non compaiono.</param>
/// <param name="generatoreAnteprima">Disegna l'anteprima dei documenti selezionati; senza, il pannello non compare.</param>
/// <param name="aspetto">Cambia i colori del programma; senza, la scelta dell'aspetto nelle impostazioni si salva ma non si vede.</param>
public partial class MainViewModel(
    IArchivioService archivio,
    IArchivioFileService files,
    IDialogService dialog,
    IShellService shell,
    ImpostazioniApp impostazioni,
    AlertService? avvisi = null,
    IRicercaService? ricerca = null,
    IMonitorIndicizzazione? monitor = null,
    IOcr? ocr = null,
    ImpostazioniService? servizioImpostazioni = null,
    IBackupService? servizioBackup = null,
    IGeneratoreAnteprima? generatoreAnteprima = null,
    IAspettoService? aspetto = null) : ObservableObject
{
    // Non è readonly: cambiando le soglie nelle impostazioni il servizio viene ricreato con i valori nuovi.
    private AlertService _avvisi = avvisi ?? new AlertService(impostazioni);

    private readonly ControlloDuplicati _duplicati = new(archivio, dialog);

    /// <summary>Il pannello con l'anteprima del documento selezionato; null se non c'è il generatore.</summary>
    public AnteprimaViewModel? Anteprima { get; } =
        generatoreAnteprima is null
            ? null
            : new AnteprimaViewModel(generatoreAnteprima, files, mostraIngrandita: ingrandita => dialog.MostraAnteprimaIngrandita(ingrandita));

    public bool AnteprimaDisponibile => Anteprima is not null;

    private bool _caricamentoInCorso;
    private bool _ripristinandoSelezione;
    private DateOnly _dataUltimoCalcolo;

    /// <summary>Contiene sempre e solo la radice "Tutti i documenti" (il TreeView vuole una lista).</summary>
    public ObservableCollection<NodoAlberoViewModel> Radici { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaSelezione), nameof(RadiceSelezionata), nameof(AreaSelezionata),
        nameof(PuoCreareCartella), nameof(PuoModificare), nameof(HaPercorsoFisico), nameof(RiepilogoVisibile),
        nameof(TipoDettaglio), nameof(TitoloDettaglio), nameof(RiepilogoDettaglio), nameof(PercorsoDettaglio),
        nameof(PuoArchiviare), nameof(PuoRipristinare), nameof(PuoArchiviareCompletate))]
    [NotifyCanExecuteChangedFor(nameof(NuovaCartellaCommand), nameof(RinominaCommand),
        nameof(EliminaCommand), nameof(ApriInEsploraCommand), nameof(ArchiviaCommand), nameof(RipristinaCommand),
        nameof(ArchiviaCompletateCommand))]
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
        Anteprima?.Svuota(); // il documento che si vedeva era della schermata precedente
        FormCartella = null;
        ElencoDocumenti = null;
        ElencoScadenze = null;
        _caricamentoForm = Task.CompletedTask;
        _caricamentoElenco = Task.CompletedTask;

        // Se c'è una ricerca in corso (es. l'albero è stato riletto) si mostrano di nuovo i suoi risultati.
        if (RicercaAttiva && value is not null)
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
            case TipoNodo.Radice or TipoNodo.Area or TipoNodo.Archivio or TipoNodo.AreaArchivio:
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
            // Un'area e il suo gruppo nell'archivio mostrano i documenti di quell'area; radice e archivio quelli di tutte.
            // L'archivio e i suoi gruppi mostrano solo i documenti delle cartelle archiviate.
            var elenco = new ElencoDocumentiViewModel(
                archivio, files, dialog, shell, nodo.Tipo is TipoNodo.Area or TipoNodo.AreaArchivio ? nodo.Id : null, _avvisi,
                soloArchiviate: nodo.Tipo is TipoNodo.Archivio or TipoNodo.AreaArchivio);
            await elenco.CaricaAsync();
            if (!ReferenceEquals(NodoSelezionato, nodo) || RicercaAttiva)
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
            if (!ReferenceEquals(NodoSelezionato, nodo) || RicercaAttiva)
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
            if (!ReferenceEquals(NodoSelezionato, nodo) || RicercaAttiva)
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
                AggiornaStatoArchiviazione(); // "Archivia" si offre solo per le cartelle completate
            };
            form.ArchiviataCambiato += () => _ = RicaricaSicuraAsync(nodo.Chiave);
            form.RicaricaRichiesta += () => _ = RicaricaSicuraAsync();
            form.CartellaSuccessivaCreata += () => _ = RicaricaSicuraAsync();
            form.DocumentoSelezionatoCambiato += documento => Anteprima?.Mostra(documento);
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
        if (RicercaAttiva)
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

    private async Task RicaricaSicuraAsync(string? chiaveDaSelezionare = null)
    {
        try { await RicaricaAsync(chiaveDaSelezionare); }
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

    /// <summary>I nodi "Scadenze" e "Archivio completati" non corrispondono a nessuna cartella su disco.</summary>
    public bool HaPercorsoFisico => !InRicerca && NodoSelezionato is { Tipo: not (TipoNodo.Scadenze or TipoNodo.Archivio) };

    /// <summary>Una cartella completata e non ancora archiviata si può mettere nell'"Archivio completati".</summary>
    public bool PuoArchiviare => NodoSelezionato is { Tipo: TipoNodo.Cartella, Completato: true, Archiviata: false };

    /// <summary>Una cartella dell'archivio si può rimettere nella sua area.</summary>
    public bool PuoRipristinare => NodoSelezionato is { Tipo: TipoNodo.Cartella, Archiviata: true };

    /// <summary>Da radice e aree si possono archiviare in una volta tutte le cartelle completate.</summary>
    public bool PuoArchiviareCompletate => NodoSelezionato?.Tipo is TipoNodo.Radice or TipoNodo.Area;

    /// <summary>Lo stato "completata" della cartella selezionata è cambiato: menu e pulsanti di archiviazione si aggiornano.</summary>
    private void AggiornaStatoArchiviazione()
    {
        OnPropertyChanged(nameof(PuoArchiviare));
        OnPropertyChanged(nameof(PuoRipristinare));
        ArchiviaCommand.NotifyCanExecuteChanged();
        RipristinaCommand.NotifyCanExecuteChanged();
    }

    // ---------- Intestazione del pannello di destra ----------

    public string TipoDettaglio => InRicerca ? "Ricerca" : NodoSelezionato?.Tipo switch
    {
        TipoNodo.Radice => "Archivio",
        TipoNodo.Scadenze => "Promemoria",
        TipoNodo.Area => "Area",
        TipoNodo.Cartella => "Cartella",
        TipoNodo.Archivio => "Cartelle completate",
        TipoNodo.AreaArchivio => "Archiviate dall'area",
        _ => ""
    };

    public string TitoloDettaglio => InRicerca ? "Risultati" : NodoSelezionato?.Nome ?? "";

    public string RiepilogoDettaglio => ElencoDocumenti is { IsRicerca: true } risultati
        ? RiepilogoRicerca(risultati)
        : NodoSelezionato switch
    {
        { Tipo: TipoNodo.Radice } n =>
            $"{Conta(n.Aree.Count(), "area", "aree")}  ·  {Cartelle(n)}  ·  {Conta(n.NumeroDocumenti, "documento", "documenti")}",
        { Tipo: TipoNodo.Area } n =>
            $"{Cartelle(n)}  ·  {Conta(n.NumeroDocumenti, "documento", "documenti")}",
        { Tipo: TipoNodo.Archivio or TipoNodo.AreaArchivio } n =>
            $"{Conta(n.NumeroCartelle, "cartella", "cartelle")}  ·  {Conta(n.NumeroDocumenti, "documento", "documenti")}",
        { Tipo: TipoNodo.Cartella } n => Conta(n.NumeroDocumenti, "documento", "documenti"),
        { Tipo: TipoNodo.Scadenze } => DescriviAvvisi(ContaAvvisi()) is { Count: > 0 } righe
            ? string.Join("  ·  ", righe)
            : "Nessuna scadenza in arrivo.",
        _ => ""
    };

    public string PercorsoDettaglio =>
        !InRicerca && NodoSelezionato is { Tipo: not (TipoNodo.Scadenze or TipoNodo.Archivio) } n ? files.PercorsoAssoluto(n.PercorsoRelativo) : "";

    /// <summary>"3 cartelle", o "3 cartelle (1 archiviata)" se alcune sono nell'archivio.</summary>
    private static string Cartelle(NodoAlberoViewModel nodo)
    {
        var testo = Conta(nodo.NumeroCartelle, "cartella", "cartelle");
        return nodo.CartelleArchiviate == 0
            ? testo
            : $"{testo} ({Conta(nodo.CartelleArchiviate, "archiviata", "archiviate")})";
    }

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

            // "Archivio completati" sta in fondo, dopo le aree: le cartelle archiviate compaiono lì (raggruppate per area)
            // e non più nella loro area. Su disco restano dove sono.
            var nodoArchivio = new NodoAlberoViewModel(TipoNodo.Archivio, 0, "Archivio completati", "", radice, Seleziona);

            foreach (var area in aree)
            {
                var nodoArea = new NodoAlberoViewModel(TipoNodo.Area, area.Id, area.Nome, area.PercorsoRelativo, radice, Seleziona);
                NodoAlberoViewModel? gruppoArchivio = null;

                foreach (var cartella in area.Cartelle)
                {
                    NodoAlberoViewModel genitore;
                    if (cartella.Archiviata)
                    {
                        gruppoArchivio ??= new NodoAlberoViewModel(
                            TipoNodo.AreaArchivio, area.Id, area.Nome, area.PercorsoRelativo, nodoArchivio, Seleziona);
                        genitore = gruppoArchivio;
                    }
                    else
                    {
                        genitore = nodoArea;
                    }

                    var nodoCartella = new NodoAlberoViewModel(
                        TipoNodo.Cartella, cartella.Id, cartella.Titolo, cartella.PercorsoRelativo, genitore, Seleziona);
                    nodoCartella.ImpostaNumeroDocumenti(cartella.NumeroDocumenti);
                    nodoCartella.ImpostaScadenza(cartella.DataScadenza, cartella.Completato);
                    nodoCartella.ImpostaArchiviata(cartella.Archiviata);
                    genitore.Figli.Add(nodoCartella);
                }

                // L'area ricorda quante cartelle (e documenti) ha nell'archivio: contano nei totali e prima di eliminarla.
                var archiviate = area.Cartelle.Where(c => c.Archiviata).ToList();
                nodoArea.ImpostaArchiviate(archiviate.Count, archiviate.Sum(c => c.NumeroDocumenti));

                radice.Figli.Add(nodoArea);
                if (gruppoArchivio is not null)
                    nodoArchivio.Figli.Add(gruppoArchivio);
            }
            radice.Figli.Add(nodoArchivio);

            foreach (var nodo in radice.ConDiscendenti())
                nodo.IsExpanded = espansi.Contains(nodo.Chiave);
            if (primoCaricamento)
                radice.IsExpanded = true;

            Radici.Clear();
            Radici.Add(radice);
            RicalcolaAvvisi();
            Filtri.AggiornaAree(radice.Aree.Select(a => (a.Id, a.Nome)));
            AggiornaPromemoriaBackup();
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
        if (RicercaAttiva && !_ripristinandoSelezione)
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

    /// <summary>Il campo di ricerca contiene qualcosa (non è vuoto né fatto di soli spazi).</summary>
    public bool TestoRicercaAttivo => !string.IsNullOrWhiteSpace(TestoRicerca);

    /// <summary>C'è una ricerca da mostrare: delle parole, dei filtri della ricerca avanzata, o entrambi.</summary>
    public bool RicercaAttiva => TestoRicercaAttivo || Filtri.NumeroAttivi > 0;

    // ---------- Ricerca avanzata ----------

    private FiltriRicercaViewModel? _filtri;

    /// <summary>I filtri del pannello "Ricerca avanzata".</summary>
    public FiltriRicercaViewModel Filtri
    {
        get
        {
            if (_filtri is null)
            {
                _filtri = new FiltriRicercaViewModel();
                _filtri.Cambiati += OnFiltriCambiati;
            }
            return _filtri;
        }
    }

    /// <summary>Spiega cosa significano "In scadenza" e "Scadute" nel menu dello stato.</summary>
    public string SuggerimentoStatoRicerca =>
        $"«In scadenza»: non completate, con la scadenza entro {Giorni(_avvisi.SogliaArancioneGiorni)} (la soglia arancione delle impostazioni).\n" +
        "«Scadute»: non completate, con la scadenza già passata.\n" +
        "«Completate»: anche quelle archiviate. «Archiviate»: solo quelle messe in «Archivio completati».";

    /// <summary>Il pannello dei filtri è aperto.</summary>
    [ObservableProperty]
    private bool _pannelloFiltriAperto;

    /// <summary>Un filtro è cambiato: la ricerca si rifà subito (o, se non resta più nulla da cercare, si torna alla vista normale).</summary>
    private void OnFiltriCambiati()
    {
        if (_sopprimiRicerca)
            return;

        OnPropertyChanged(nameof(RicercaAttiva));
        AnnullaRicercaInCorso();
        if (!RicercaAttiva)
        {
            ElencoDocumenti = null;
            CaricaPannello(NodoSelezionato);
            return;
        }

        _ricerca = EseguiRicercaAsync(TestoRicerca.Trim(), TimeSpan.Zero);
    }

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

        OnPropertyChanged(nameof(RicercaAttiva));
        AnnullaRicercaInCorso();
        if (!RicercaAttiva)
        {
            // Campo svuotato e nessun filtro: si torna a mostrare ciò che spetta all'elemento selezionato.
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
        if (!RicercaAttiva)
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

            var risultati = new ElencoDocumentiViewModel(
                archivio, files, dialog, shell, null, _avvisi, ricerca, testo, Filtri.ToFiltri(_avvisi));
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

    /// <summary>
    /// Finisce la ricerca: svuota il campo e azzera i filtri senza far partire altro; se richiesto ricarica
    /// il pannello dell'elemento selezionato.
    /// </summary>
    private void EsciDallaRicerca(bool ricarica)
    {
        AnnullaRicercaInCorso();

        _sopprimiRicerca = true;
        try
        {
            TestoRicerca = "";
            Filtri.Azzera(notifica: false);
        }
        finally
        {
            _sopprimiRicerca = false;
        }
        OnPropertyChanged(nameof(RicercaAttiva));

        if (ElencoDocumenti is { IsRicerca: true })
            ElencoDocumenti = null;
        if (ricarica)
            CaricaPannello(NodoSelezionato);
    }

    private string RiepilogoRicerca(ElencoDocumentiViewModel risultati)
    {
        var parole = string.IsNullOrWhiteSpace(risultati.TestoRicerca) ? "" : $" per «{risultati.TestoRicerca}»";
        var filtri = risultati.HaFiltri ? " con i filtri scelti" : "";
        var testo = $"{Conta(risultati.Documenti.Count, "documento trovato", "documenti trovati")}{parole}{filtri}";
        return risultati.Troncato ? testo + " (ce ne sono altri: restringi la ricerca)" : testo;
    }

    /// <summary>Gli eventi delle griglie (doppio clic, eliminazione...) sono gli stessi per documenti di un'area e risultati di ricerca.</summary>
    private void CollegaElenco(ElencoDocumentiViewModel elenco)
    {
        Anteprima?.Svuota(); // l'elenco precedente (o i risultati vecchi) non c'è più
        elenco.DocumentoSelezionatoCambiato += documento => Anteprima?.Mostra(documento);
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
                // Se l'utente rinuncia davanti ai duplicati la finestra si riapre: può togliere il file o allegarlo comunque.
                if (await _duplicati.FiltraAsync(modello.PercorsiFile) is not { } daAllegare)
                    continue;

                var creata = await archivio.CreaCartellaConDatiAsync(area.Id, modello.Dati, daAllegare);
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
        if (NodoSelezionato is not { Tipo: not (TipoNodo.Scadenze or TipoNodo.Archivio) } nodo)
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

    // ---------- Archivio completati ----------

    /// <summary>
    /// Mette la cartella completata nell'"Archivio completati": sparisce dalla sua area nell'albero e compare lì.
    /// Non sposta niente su disco. Si resta sulla cartella, che ora si trova nell'archivio.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuoArchiviare))]
    private async Task ArchiviaAsync()
    {
        if (NodoSelezionato is not { Tipo: TipoNodo.Cartella, Completato: true, Archiviata: false } nodo)
            return;

        await EseguiAsync(async () =>
        {
            await archivio.ArchiviaCartellaAsync(nodo.Id);
            await RicaricaAsync(nodo.Chiave);
        });
    }

    /// <summary>Rimette la cartella archiviata nella sua area (resta completata). Si resta sulla cartella.</summary>
    [RelayCommand(CanExecute = nameof(PuoRipristinare))]
    private async Task RipristinaAsync()
    {
        if (NodoSelezionato is not { Tipo: TipoNodo.Cartella, Archiviata: true } nodo)
            return;

        await EseguiAsync(async () =>
        {
            await archivio.RipristinaCartellaAsync(nodo.Id);
            await RicaricaAsync(nodo.Chiave);
        });
    }

    /// <summary>Archivia in una volta tutte le cartelle completate di un'area (o di tutte le aree, dalla radice), dopo aver chiesto.</summary>
    [RelayCommand(CanExecute = nameof(PuoArchiviareCompletate))]
    private async Task ArchiviaCompletateAsync()
    {
        if (NodoSelezionato is not { Tipo: TipoNodo.Radice or TipoNodo.Area } nodo)
            return;

        var eArea = nodo.Tipo == TipoNodo.Area;
        var daArchiviare = eArea
            ? nodo.Figli.Count(c => c.Completato)
            : nodo.Aree.Sum(a => a.Figli.Count(c => c.Completato));

        if (daArchiviare == 0)
        {
            dialog.MostraMessaggio("Archivio completati", "Non ci sono cartelle completate da archiviare.");
            return;
        }

        var dove = eArea ? $" di «{nodo.Nome}»" : "";
        if (!dialog.Chiedi(
                "Archivia cartelle completate",
                $"Archiviare {Conta(daArchiviare, "cartella completata", "cartelle completate")}{dove}?\n\n"
                + "Non si sposta nessun file: le cartelle compaiono in «Archivio completati» e si possono ripristinare una per una."))
            return;

        await EseguiAsync(async () =>
        {
            await archivio.ArchiviaCompletateAsync(eArea ? nodo.Id : null);
            await RicaricaAsync(nodo.Chiave);
        });
    }

    // ---------- Impostazioni e informazioni ----------

    /// <summary>La finestra "Impostazioni" compare solo se c'è un servizio che sa salvarle.</summary>
    public bool ImpostazioniDisponibili => servizioImpostazioni is not null;

    /// <summary>
    /// Apre la finestra delle impostazioni. Se i valori sono validi si salvano nel file e si applicano subito
    /// (soglie, dati della ditta, nome della radice): niente riavvio. Se il salvataggio fallisce la finestra resta aperta.
    /// Se si è premuto "Fai il backup ora" o "Ripristina da un backup…", dopo il salvataggio parte l'operazione.
    /// </summary>
    [RelayCommand]
    private async Task ApriImpostazioniAsync()
    {
        if (servizioImpostazioni is null)
            return;

        // L'aspetto si prova mentre la finestra è aperta; a fine lavoro vale quello delle impostazioni (nuove se salvate, vecchie se annullate).
        var modello = new ImpostazioniViewModel(impostazioni, servizioImpostazioni.PercorsoFile, BackupDisponibile, aspetto is null ? null : aspetto.Applica);
        try
        {
            while (dialog.MostraImpostazioni(modello))
            {
                // Un'azione chiesta prima di un salvataggio fallito non vale per la riapertura della finestra.
                var azione = modello.AzioneRichiesta;
                modello.AzioneRichiesta = AzioneDaImpostazioni.Nessuna;

                var nuove = modello.Costruisci();
                try
                {
                    servizioImpostazioni.Salva(nuove);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    dialog.MostraErrore(
                        $"Non è stato possibile salvare le impostazioni: {ex.Message}\n\nFile: {servizioImpostazioni.PercorsoFile}");
                    continue;
                }

                impostazioni.CopiaDa(nuove);
                aspetto?.Applica(impostazioni.Aspetto);
                await EseguiAsync(ApplicaImpostazioniAsync);

                // Le impostazioni sono salvate e applicate (anche la cartella dei backup appena scelta): ora l'operazione richiesta.
                switch (azione)
                {
                    case AzioneDaImpostazioni.Backup:
                        await EseguiBackupCommand.ExecuteAsync(null);
                        break;
                    case AzioneDaImpostazioni.Ripristino:
                        await RipristinaDaBackupCommand.ExecuteAsync(null);
                        break;
                }
                return;
            }
        }
        finally
        {
            aspetto?.Applica(impostazioni.Aspetto);
        }
    }

    /// <summary>Le soglie e i dati della ditta sono cambiati: si ricrea il servizio degli avvisi e si rilegge l'albero.</summary>
    private async Task ApplicaImpostazioniAsync()
    {
        _avvisi = new AlertService(impostazioni, _avvisi.Tempo);
        OnPropertyChanged(nameof(TestoAzienda));
        OnPropertyChanged(nameof(DescrizioneAzienda));
        OnPropertyChanged(nameof(SuggerimentoStatoRicerca));
        await RicaricaAsync();
    }

    /// <summary>Mostra la finestra "Informazioni": versione, dati della ditta, dove sono archivio e database, stato dell'OCR.</summary>
    [RelayCommand]
    private void ApriInformazioni()
    {
        var azienda = impostazioni.Azienda;
        var assemblea = typeof(MainViewModel).Assembly;
        var versione = assemblea.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                       ?? assemblea.GetName().Version?.ToString() ?? "";
        versione = versione.Split('+')[0]; // il compilatore aggiunge "+identificativo del commit"

        var radice = Radici.FirstOrDefault();
        var contenuto = radice is null
            ? ""
            : $"{Conta(radice.Aree.Count(), "area", "aree")}  ·  {Conta(radice.NumeroCartelle, "cartella", "cartelle")}  ·  {Conta(radice.NumeroDocumenti, "documento", "documenti")}";

        var statoOcr = ocr switch
        {
            null => "Non verificato.",
            { Disponibile: true } => $"Disponibile ({ocr.Lingua}).",
            _ => ocr.MotivoNonDisponibile ?? "Non disponibile."
        };

        dialog.MostraInformazioni(new InformazioniViewModel(
            "Documentale", versione,
            azienda.RagioneSociale, azienda.CodiceFiscale, azienda.PartitaIva, azienda.Indirizzo, azienda.Descrizione,
            files.PercorsoRadice, ArchivioDatabase.PercorsoDatabase(files.PercorsoRadice),
            servizioImpostazioni?.PercorsoFile ?? "",
            contenuto, statoOcr,
            $"{RuntimeInformation.FrameworkDescription} su {RuntimeInformation.OSDescription}"));
    }

    // ---------- Backup ----------

    /// <summary>Il pulsante "Backup" compare solo se c'è il servizio che lo esegue.</summary>
    public bool BackupDisponibile => servizioBackup is not null;

    /// <summary>"Backup in corso… 12 file" o "Ripristino in corso…" mentre l'operazione lavora (nella barra in fondo); vuoto altrimenti.</summary>
    [ObservableProperty]
    private string _testoBackup = "";

    private bool _promemoriaBackupRimandato;
    private bool _backupInCorso;

    /// <summary>
    /// Cosa dice il promemoria sotto la barra degli strumenti: il backup non si fa da troppo tempo (o non si è mai fatto)
    /// e nell'archivio c'è qualcosa da perdere. Vuoto se non c'è nulla da ricordare.
    /// </summary>
    public string TestoPromemoriaBackup
    {
        get
        {
            if (servizioBackup is null || Radice is not { NumeroDocumenti: > 0 })
                return "";
            if (impostazioni.UltimoBackup is not { } ultimo)
                return "Non hai ancora fatto nessun backup dei tuoi documenti.";

            var giorni = _avvisi.Oggi.DayNumber - DateOnly.FromDateTime(ultimo).DayNumber;
            return giorni >= impostazioni.BackupPromemoriaGiorni
                ? $"L'ultimo backup risale a {Conta(giorni, "giorno", "giorni")} fa."
                : "";
        }
    }

    /// <summary>Il promemoria si vede, a meno che l'utente lo abbia rimandato (fino alla prossima apertura del programma).</summary>
    public bool PromemoriaBackupVisibile => !_promemoriaBackupRimandato && TestoPromemoriaBackup.Length > 0;

    private void AggiornaPromemoriaBackup()
    {
        OnPropertyChanged(nameof(TestoPromemoriaBackup));
        OnPropertyChanged(nameof(PromemoriaBackupVisibile));
    }

    [RelayCommand]
    private void RimandaPromemoriaBackup()
    {
        _promemoriaBackupRimandato = true;
        AggiornaPromemoriaBackup();
    }

    /// <summary>
    /// Salva tutto l'archivio (documenti e database) in un file ZIP nella cartella dei backup. Se l'utente non ha scelto una
    /// cartella si usa quella predefinita (C:\Backup\DocumentaleMarta), senza chiedere niente; la si chiede solo se quella
    /// predefinita non si può usare. A backup riuscito la cartella si ricorda nelle impostazioni. Il pulsante resta spento
    /// finché il backup lavora.
    /// </summary>
    [RelayCommand(CanExecute = nameof(BackupDisponibile))]
    private async Task EseguiBackupAsync()
    {
        // Il pulsante si spegne da solo mentre il backup lavora; qui si evita comunque di farne due insieme.
        if (servizioBackup is null || _backupInCorso)
            return;

        var cartella = impostazioni.CartellaBackupInUso;
        if (string.IsNullOrWhiteSpace(cartella))
        {
            cartella = dialog.SelezionaCartella("Scegli la cartella in cui salvare i backup", null);
            if (cartella is null)
                return;
        }

        _backupInCorso = true;
        TestoBackup = "Backup in corso…";
        try
        {
            var progresso = new ProgressoSuInterfaccia(
                n => TestoBackup = $"Backup in corso… {Conta(n, "file", "file")}", SynchronizationContext.Current);
            var esito = await servizioBackup.CreaBackupAsync(cartella, progresso);

            // Solo a backup riuscito la cartella si ricorda (una scelta sbagliata non resta nelle impostazioni).
            impostazioni.CartellaBackup = cartella;
            impostazioni.UltimoBackup = esito.Data;
            var avviso = SalvaImpostazioniDopoBackup();

            dialog.MostraMessaggio(
                "Backup completato",
                $"Il backup è stato creato:\n{esito.PercorsoZip}\n\n"
                + $"{Conta(esito.NumeroFile, "file", "file")} salvati ({FormatiTesto.Dimensione(esito.Dimensione)}).\n\n"
                + "Per ripristinarlo, apri il file ZIP: dentro c'è un file con le istruzioni."
                + avviso);
        }
        catch (ArchivioException ex)
        {
            dialog.MostraErrore(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialog.MostraErrore(
                $"Il backup non è riuscito: {ex.Message}\n\n"
                + "Controlla che la cartella dei backup sia raggiungibile (per esempio che il disco esterno sia collegato) "
                + "e che ci sia spazio libero. Puoi sceglierne un'altra dalle Impostazioni. Non è stato creato nessun file.");
        }
        catch (OperationCanceledException)
        {
            // Backup interrotto (chiusura del programma): nessun messaggio.
        }
        finally
        {
            _backupInCorso = false;
            TestoBackup = "";
            AggiornaPromemoriaBackup();
        }
    }

    /// <summary>Salva nel file delle impostazioni la cartella e la data dell'ultimo backup. Restituisce un avviso da mostrare se non ci riesce.</summary>
    private string SalvaImpostazioniDopoBackup()
    {
        if (servizioImpostazioni is null)
            return "";
        try
        {
            servizioImpostazioni.Salva(impostazioni);
            return "";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"\n\nAttenzione: non è stato possibile salvare la data del backup nelle impostazioni ({ex.Message}).";
        }
    }

    /// <summary>
    /// Ripristina un backup. Non sovrascrive mai niente: il backup si estrae in una cartella NUOVA (dentro quella che l'utente
    /// sceglie), si controlla, e solo se l'utente lo vuole il programma passa a usarla (si riavvia). L'archivio attuale resta
    /// dov'è, intatto.
    /// </summary>
    [RelayCommand(CanExecute = nameof(BackupDisponibile))]
    private async Task RipristinaDaBackupAsync()
    {
        if (servizioBackup is null || _backupInCorso)
            return;

        // 1. Quale backup.
        var zip = dialog.SelezionaFileBackup(impostazioni.CartellaBackupInUso);
        if (zip is null)
            return;

        InfoBackup info;
        try
        {
            info = await servizioBackup.LeggiBackupAsync(zip);
        }
        catch (ArchivioException ex)
        {
            dialog.MostraErrore(ex.Message);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialog.MostraErrore($"Non è stato possibile leggere il file: {ex.Message}");
            return;
        }

        // 2. Dove: si sceglie una cartella e dentro se ne crea una nuova, così non si può mescolare niente con file esistenti.
        var madre = dialog.SelezionaCartella(
            "Scegli dove ripristinare l'archivio (ci verrà creata una nuova cartella)", Directory.GetParent(files.PercorsoRadice)?.FullName);
        if (madre is null)
            return;

        var nome = NomiFileSicuri.RendiUnivoco($"Documentale ripristinato {_avvisi.Oggi:yyyy-MM-dd}", n => Path.Exists(Path.Combine(madre, n)));
        var destinazione = Path.Combine(madre, nome);

        // 3. Conferma, dicendo bene cosa succede e cosa no.
        var quando = info.Data is { } data ? $"del {data:dd/MM/yyyy} alle {data:HH:mm}: " : ": ";
        if (!dialog.Chiedi(
                "Ripristina da backup",
                $"Ripristinare il backup {quando}{Conta(info.NumeroFile, "file", "file")} ({FormatiTesto.Dimensione(info.DimensioneDecompressa)})?\n\n"
                + $"Si crea la cartella:\n{destinazione}\n\n"
                + $"L'archivio attuale ({files.PercorsoRadice}) non viene toccato."))
            return;

        // 4. Il ripristino vero.
        EsitoRipristino esito;
        _backupInCorso = true;
        TestoBackup = "Ripristino in corso…";
        try
        {
            var progresso = new ProgressoSuInterfaccia(
                n => TestoBackup = $"Ripristino in corso… {Conta(n, "file", "file")}", SynchronizationContext.Current);
            esito = await servizioBackup.RipristinaAsync(zip, destinazione, progresso);
        }
        catch (ArchivioException ex)
        {
            dialog.MostraErrore(ex.Message);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialog.MostraErrore($"Il ripristino non è riuscito: {ex.Message}\n\nNon è stato creato nulla. Controlla che ci sia spazio libero.");
            return;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            _backupInCorso = false;
            TestoBackup = "";
        }

        // 5. Usarlo subito oppure no.
        ProponiDiUsareLArchivioRipristinato(esito);
    }

    /// <summary>Dopo un ripristino riuscito: avvisa se mancano dei file e chiede se passare subito al nuovo archivio.</summary>
    private void ProponiDiUsareLArchivioRipristinato(EsitoRipristino esito)
    {
        var avviso = esito.DocumentiMancanti switch
        {
            0 => "",
            1 => "\n\nAttenzione: 1 documento elencato nel database non ha il suo file nel backup.",
            var n => $"\n\nAttenzione: {n} documenti elencati nel database non hanno il loro file nel backup."
        };

        var percorsoImpostazioni = servizioImpostazioni?.PercorsoFile ?? "";
        var comeUsarlo =
            $"Per usarlo cambia «PercorsoRadice» nel file delle impostazioni ({percorsoImpostazioni}) e riavvia Documentale.";

        if (servizioImpostazioni is null
            || !dialog.Chiedi(
                "Ripristino completato",
                $"L'archivio è stato ripristinato in:\n{esito.Cartella}{avviso}\n\n"
                + "Vuoi usarlo subito? Documentale si riavvia. L'archivio attuale resta dov'è."))
        {
            dialog.MostraMessaggio(
                "Ripristino completato", $"L'archivio ripristinato è in:\n{esito.Cartella}{avviso}\n\n{comeUsarlo}");
            return;
        }

        // Si cambia solo il file: le impostazioni in uso restano quelle di adesso fino al riavvio.
        var nuove = impostazioni.Clona();
        nuove.PercorsoRadice = esito.Cartella;
        if (nuove.Valida() is { Count: > 0 } problemi)
        {
            dialog.MostraErrore(
                $"L'archivio è stato ripristinato in:\n{esito.Cartella}\n\nMa non si può usare subito: {problemi[0]}\n\n{comeUsarlo}");
            return;
        }

        try
        {
            servizioImpostazioni.Salva(nuove);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialog.MostraErrore(
                $"L'archivio è stato ripristinato in:\n{esito.Cartella}\n\nMa non è stato possibile salvare le impostazioni ({ex.Message}).\n\n{comeUsarlo}");
            return;
        }

        shell.RiavviaApplicazione();
    }

    /// <summary>Riporta l'avanzamento sul thread dell'interfaccia (i file vengono copiati in background).</summary>
    private sealed class ProgressoSuInterfaccia(Action<int> aggiorna, SynchronizationContext? contesto) : IProgress<int>
    {
        public void Report(int valore)
        {
            if (contesto is null)
                aggiorna(valore);
            else
                contesto.Post(_ => aggiorna(valore), null);
        }
    }

    // ---------- Trascinamento sull'albero ----------

    /// <summary>
    /// Un documento è stato trascinato su una cartella dell'albero: lo si sposta lì (il file vero e la riga nel database).
    /// </summary>
    public Task SpostaDocumentoAsync(int documentoId, int cartellaDestinazioneId) => EseguiAsync(async () =>
    {
        await archivio.SpostaDocumentoAsync(documentoId, cartellaDestinazioneId);
        await RicaricaAsync();
    });

    /// <summary>Dei file sono stati trascinati da Esplora file su una cartella dell'albero: si allegano a quella cartella.</summary>
    public Task AllegaATrascinatiAsync(int cartellaId, IReadOnlyList<string> percorsi) => EseguiAsync(async () =>
    {
        var (file, cartelleEscluse) = Trascinamento.SoloFile(percorsi);
        if (cartelleEscluse > 0)
            dialog.MostraErrore(Trascinamento.MessaggioCartelleEscluse(cartelleEscluse));
        if (file.Count == 0)
            return;

        if (await _duplicati.FiltraAsync(file) is not { } daAllegare)
            return;

        await archivio.AllegaDocumentiAsync(cartellaId, daAllegare);
        await RicaricaAsync();
    });

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

        // Le cartelle archiviate non si vedono tra i figli dell'area ma si eliminano con lei: meglio dirlo.
        if (nodo.Tipo == TipoNodo.Area && nodo.CartelleArchiviate > 0)
            testo += $"\n\nCi sono anche {Conta(nodo.CartelleArchiviate, "cartella archiviata", "cartelle archiviate")} in «Archivio completati».";
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
