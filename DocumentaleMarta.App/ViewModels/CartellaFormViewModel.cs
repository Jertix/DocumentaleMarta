using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>
/// Form di una cartella esistente (pannello di destra). Ogni modifica ai campi viene salvata da sola:
/// non c'è un pulsante "Salva" e quindi nemmeno modifiche perse passando ad un altro elemento dell'albero.
/// </summary>
public partial class CartellaFormViewModel : CartellaCampiViewModel
{
    private readonly IArchivioService _archivio;
    private readonly IArchivioFileService _files;
    private readonly IDialogService _dialog;
    private readonly IShellService _shell;
    private readonly AzioniDocumenti _azioni;
    private readonly ControlloDuplicati _duplicati;
    private readonly AlertService? _avvisi;

    private readonly int _areaId;
    private DatiCartella _salvati;

    /// <summary>La scadenza per cui è già stata fatta la proposta della cartella successiva: spuntando "Completato" più volte non si richiede.</summary>
    private DateOnly? _scadenzaGiaProposta;

    private bool _salvataggioInCorso;
    private bool _salvataggioRichiesto;
    private Task _ultimoSalvataggio = Task.CompletedTask;

    /// <param name="avvisi">Per mostrare accanto alla scadenza quanto manca; senza, il testo della scadenza non compare.</param>
    public CartellaFormViewModel(
        IArchivioService archivio, IArchivioFileService files, IDialogService dialog, IShellService shell,
        CartellaDettaglio dettaglio, AlertService? avvisi = null)
    {
        _archivio = archivio;
        _files = files;
        _dialog = dialog;
        _shell = shell;
        _avvisi = avvisi;
        _azioni = new AzioniDocumenti(archivio, files, dialog, shell);
        _duplicati = new ControlloDuplicati(archivio, dialog);

        Id = dettaglio.Id;
        _archiviata = dettaglio.Archiviata;
        _areaId = dettaglio.AreaId;
        NomeArea = dettaglio.NomeArea;
        PercorsoRelativo = dettaglio.PercorsoRelativo;
        _salvati = dettaglio.Dati;
        Carica(dettaglio.Dati);

        foreach (var documento in dettaglio.Documenti)
            Documenti.Add(new DocumentoViewModel(documento, !_files.Esiste(documento.PercorsoRelativo), this));

        Documenti.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TitoloDocumenti));
        AggiornaAvviso();
        PropertyChanged += OnProprietaCambiata;
    }

    // ---------- Stato della scadenza ----------

    /// <summary>Urgenza della scadenza mostrata accanto al campo (Nessuno se non c'è, è lontana o la cartella è completata).</summary>
    [ObservableProperty]
    private StatoAvviso _statoScadenza;

    /// <summary>"Scade tra 5 giorni", "Scaduta da 3 giorni"... Vuoto se non c'è una scadenza o la cartella è completata.</summary>
    [ObservableProperty]
    private string _testoScadenza = "";

    private void AggiornaAvviso()
    {
        if (_avvisi is null || Completato || DataScadenza is not { } data)
        {
            StatoScadenza = StatoAvviso.Nessuno;
            TestoScadenza = "";
            return;
        }

        var scadenza = DateOnly.FromDateTime(data);
        StatoScadenza = _avvisi.Valuta(scadenza, completato: false);
        TestoScadenza = _avvisi.Descrivi(scadenza);
    }

    public int Id { get; }
    public string NomeArea { get; }

    [ObservableProperty]
    private string _percorsoRelativo = "";

    /// <summary>Messaggio sotto il form quando un salvataggio non è riuscito.</summary>
    [ObservableProperty]
    private string _errore = "";

    public ObservableCollection<DocumentoViewModel> Documenti { get; } = [];

    public string TitoloDocumenti => $"Documenti ({Documenti.Count})";

    // ---------- Archivio completati ----------

    /// <summary>La cartella sta nell'"Archivio completati" (resta nella sua area e nella sua cartella su disco).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PuoArchiviare), nameof(PuoRipristinare))]
    private bool _archiviata;

    /// <summary>Una cartella completata e non ancora archiviata si può archiviare.</summary>
    public bool PuoArchiviare => Completato && !Archiviata;

    /// <summary>Una cartella archiviata si può rimettere nella sua area.</summary>
    public bool PuoRipristinare => Archiviata;

    /// <summary>La cartella è stata archiviata o ripristinata (o riaperta, e quindi tolta dall'archivio): l'albero va rifatto.</summary>
    public event Action? ArchiviataCambiato;

    [RelayCommand]
    private async Task ArchiviaAsync()
    {
        // Se "Completato" è stato appena spuntato il salvataggio può essere ancora in corso: si aspetta.
        await AttendiSalvataggioAsync();
        await CambiaArchiviazioneAsync(() => _archivio.ArchiviaCartellaAsync(Id), archiviata: true);
    }

    [RelayCommand]
    private Task RipristinaAsync() => CambiaArchiviazioneAsync(() => _archivio.RipristinaCartellaAsync(Id), archiviata: false);

    private async Task CambiaArchiviazioneAsync(Func<Task> operazione, bool archiviata)
    {
        try
        {
            await operazione();
            Archiviata = archiviata;
            ArchiviataCambiato?.Invoke();
        }
        catch (ArchivioException ex)
        {
            _dialog.MostraErrore(ex.Message);
            RicaricaRichiesta?.Invoke();
        }
    }

    /// <summary>La riga selezionata nella griglia: se ne mostra l'anteprima.</summary>
    [ObservableProperty]
    private DocumentoViewModel? _documentoSelezionato;

    /// <summary>È cambiato il documento selezionato (null = nessuno).</summary>
    public event Action<IDocumentoAnteprima?>? DocumentoSelezionatoCambiato;

    partial void OnDocumentoSelezionatoChanged(DocumentoViewModel? value) => DocumentoSelezionatoCambiato?.Invoke(value);

    /// <summary>Il titolo è stato salvato: titolo e nuovo percorso, per aggiornare il nodo dell'albero.</summary>
    public event Action<string, string>? TitoloSalvato;

    /// <summary>I dati della cartella sono stati salvati: serve all'albero per aggiornare gli avvisi di scadenza.</summary>
    public event Action<DatiCartella>? DatiSalvati;

    /// <summary>Il numero di documenti è cambiato (allegati o eliminati).</summary>
    public event Action<int>? NumeroDocumentiCambiato;

    /// <summary>La cartella non esiste più nell'archivio: l'albero mostrato è vecchio e va riletto.</summary>
    public event Action? RicaricaRichiesta;

    /// <summary>Completa quando i salvataggi avviati fin qui sono finiti.</summary>
    public Task AttendiSalvataggioAsync() => _ultimoSalvataggio;

    // ---------- Salvataggio automatico ----------

    private void OnProprietaCambiata(object? mittente, PropertyChangedEventArgs e)
    {
        // Il testo della scadenza segue subito ciò che si vede nel form, anche prima del salvataggio.
        if (e.PropertyName is nameof(DataScadenza) or nameof(Completato))
            AggiornaAvviso();
        if (e.PropertyName is nameof(Completato))
            OnPropertyChanged(nameof(PuoArchiviare)); // "Archivia" compare appena si spunta "Completato"

        if (InCaricamento)
            return;

        if (e.PropertyName is nameof(Titolo) or nameof(Descrizione) or nameof(DataScadenza)
            or nameof(Ricorrenza) or nameof(Completato) or nameof(DataCompletamento))
            _ultimoSalvataggio = SalvaAsync();
    }

    private async Task SalvaAsync()
    {
        // Più modifiche ravvicinate (es. "Completato" e la sua data) non lanciano salvataggi in parallelo:
        // chi arriva durante un salvataggio chiede solo di ripeterlo a fine lavoro.
        if (_salvataggioInCorso)
        {
            _salvataggioRichiesto = true;
            return;
        }

        _salvataggioInCorso = true;
        try
        {
            do
            {
                _salvataggioRichiesto = false;
                await SalvaUnaVoltaAsync();
            } while (_salvataggioRichiesto);
        }
        finally
        {
            _salvataggioInCorso = false;
        }
    }

    private async Task SalvaUnaVoltaAsync()
    {
        var dati = Dati;
        if (dati == _salvati)
            return;

        if (string.IsNullOrWhiteSpace(dati.Titolo))
        {
            Errore = "Il titolo non può essere vuoto.";
            RipristinaTitolo(dati);
            return;
        }

        Errore = "";
        try
        {
            var eraCompletata = _salvati.Completato;
            var salvato = await _archivio.AggiornaCartellaAsync(Id, dati);
            ApplicaDettaglio(salvato);

            // Appena completata una cartella che si ripete: si propone la prossima.
            if (!eraCompletata && salvato.Dati.Completato)
                await ProponiCartellaSuccessivaAsync(salvato.Dati);
        }
        catch (ArchivioException ex)
        {
            Errore = ex.Message;
            RipristinaTitolo(dati);
            if (await _archivio.CaricaCartellaAsync(Id) is null)
                RicaricaRichiesta?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Tipico: si cambia il titolo mentre un documento della cartella è aperto in Word o nel lettore PDF.
            _dialog.MostraErrore(
                $"Non è stato possibile salvare le modifiche: {ex.Message}\n\nChiudi i documenti di questa cartella aperti in altri programmi e riprova.");
            RipristinaTitolo(dati);
        }
    }

    /// <summary>
    /// Dopo un salvataggio fallito rimette il vecchio titolo (l'unico campo che può far fallire il salvataggio) e,
    /// se nel frattempo sono cambiati anche altri campi, li salva comunque.
    /// </summary>
    private void RipristinaTitolo(DatiCartella tentati)
    {
        var titoloEraCambiato = tentati.Titolo != _salvati.Titolo;
        ImpostaTitoloSenzaEffetti(_salvati.Titolo);
        _salvataggioRichiesto = titoloEraCambiato && Dati != _salvati;
    }

    private void ApplicaDettaglio(CartellaDettaglio dettaglio)
    {
        var titoloCambiato = dettaglio.Dati.Titolo != _salvati.Titolo;
        _salvati = dettaglio.Dati;

        var percorsoCambiato = dettaglio.PercorsoRelativo != PercorsoRelativo;
        if (percorsoCambiato)
        {
            // La cartella fisica è stata rinominata: i documenti hanno un nuovo percorso.
            PercorsoRelativo = dettaglio.PercorsoRelativo;
            foreach (var documento in Documenti)
            {
                var aggiornato = dettaglio.Documenti.FirstOrDefault(d => d.Id == documento.Id);
                if (aggiornato is null)
                    continue;
                documento.PercorsoRelativo = aggiornato.PercorsoRelativo;
                documento.FileMancante = !_files.Esiste(aggiornato.PercorsoRelativo);
            }
        }

        if (titoloCambiato || percorsoCambiato)
            TitoloSalvato?.Invoke(dettaglio.Dati.Titolo, dettaglio.PercorsoRelativo);
        DatiSalvati?.Invoke(dettaglio.Dati);

        // Riaprire una cartella archiviata la toglie dall'archivio: l'albero deve rifarla.
        if (dettaglio.Archiviata != Archiviata)
        {
            Archiviata = dettaglio.Archiviata;
            ArchiviataCambiato?.Invoke();
        }
    }

    // ---------- Cartelle che si ripetono ----------

    /// <summary>Si è creata la cartella successiva di una serie (la lista delle cartelle va riletta).</summary>
    public event Action? CartellaSuccessivaCreata;

    /// <summary>
    /// Una cartella che si ripete (ogni mese, 3 mesi, anno) è stata completata: propone di creare la successiva,
    /// con lo stesso titolo e la scadenza spostata in avanti. I documenti non si copiano. Si può rifiutare.
    /// </summary>
    private async Task ProponiCartellaSuccessivaAsync(DatiCartella completata)
    {
        if (completata.Ricorrenza == Ricorrenza.Nessuna || completata.DataScadenza is not { } scadenza)
            return;
        if (_scadenzaGiaProposta == scadenza)
            return;
        _scadenzaGiaProposta = scadenza;

        var prossima = CalcoloRicorrenza.Prossima(scadenza, completata.Ricorrenza);
        var messaggio = $"La cartella «{completata.Titolo}» si ripete {CalcoloRicorrenza.Descrizione(completata.Ricorrenza)}.\n\n"
                        + $"Vuoi creare la prossima, con scadenza {prossima:dd/MM/yyyy}?\n\n"
                        + "Si copiano titolo, descrizione e ripetizione; i documenti no.";
        if (!_dialog.Chiedi("Cartella ricorrente", messaggio))
            return;

        try
        {
            await _archivio.CreaCartellaConDatiAsync(
                _areaId,
                new DatiCartella(completata.Titolo, completata.Descrizione, prossima, false, null, completata.Ricorrenza),
                []);
            CartellaSuccessivaCreata?.Invoke();
        }
        catch (ArchivioException ex)
        {
            _dialog.MostraErrore($"Non è stato possibile creare la cartella successiva: {ex.Message}");
            RicaricaRichiesta?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialog.MostraErrore($"Non è stato possibile creare la cartella successiva: {ex.Message}");
        }
    }

    // ---------- Allegati ----------

    /// <param name="trascinati">
    /// I file trascinati sul form da Esplora file; senza (il pulsante "Allega") si apre la finestra per sceglierli.
    /// </param>
    [RelayCommand]
    private async Task AllegaAsync(IReadOnlyList<string>? trascinati = null)
    {
        IReadOnlyList<string> scelti;
        if (trascinati is null)
        {
            scelti = _dialog.SelezionaFile("Allega documenti");
        }
        else
        {
            var (file, cartelleEscluse) = Trascinamento.SoloFile(trascinati);
            if (cartelleEscluse > 0)
                _dialog.MostraErrore(Trascinamento.MessaggioCartelleEscluse(cartelleEscluse));
            scelti = file;
        }

        if (scelti.Count == 0)
            return;

        try
        {
            // Un file già archiviato (anche con un altro nome) non si allega due volte senza che l'utente lo sappia.
            if (await _duplicati.FiltraAsync(scelti) is not { } daAllegare)
                return;

            var nuovi = await _archivio.AllegaDocumentiAsync(Id, daAllegare);
            foreach (var documento in nuovi)
                Documenti.Add(new DocumentoViewModel(documento, fileMancante: false, this));
            NumeroDocumentiCambiato?.Invoke(Documenti.Count);
        }
        catch (ArchivioException ex)
        {
            _dialog.MostraErrore(ex.Message);
            RicaricaRichiesta?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialog.MostraErrore($"Non è stato possibile allegare i documenti: {ex.Message}\n\nNon è stato allegato nessun file.");
        }
    }

    [RelayCommand]
    private void ApriCartella()
    {
        var percorso = _files.PercorsoAssoluto(PercorsoRelativo);
        if (!Directory.Exists(percorso))
        {
            _dialog.MostraErrore($"La cartella non esiste sul disco:\n{percorso}");
            return;
        }
        _shell.ApriCartella(percorso);
    }

    // ---------- Azioni sulle righe della griglia ----------

    internal void ApriDocumento(DocumentoViewModel documento)
    {
        // La riga si aggiorna: se il file è sparito la prossima volta si vede subito.
        documento.FileMancante = !_azioni.FileEsiste(documento.PercorsoRelativo);
        if (!documento.FileMancante)
            _azioni.Apri(documento.PercorsoRelativo, documento.Tipo);
    }

    internal void MostraDocumentoInEsplora(DocumentoViewModel documento)
    {
        documento.FileMancante = !_azioni.FileEsiste(documento.PercorsoRelativo);
        if (!documento.FileMancante)
            _azioni.MostraInEsplora(documento.PercorsoRelativo);
    }

    internal async Task EliminaDocumentoAsync(DocumentoViewModel documento)
    {
        switch (await _azioni.EliminaAsync(documento.Id, documento.NomeFile))
        {
            case EsitoEliminazione.Eliminato:
                Documenti.Remove(documento);
                if (ReferenceEquals(DocumentoSelezionato, documento))
                    DocumentoSelezionato = null;
                NumeroDocumentiCambiato?.Invoke(Documenti.Count);
                break;
            case EsitoEliminazione.NonPiuEsistente:
                RicaricaRichiesta?.Invoke();
                break;
        }
    }
}
