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
    private readonly AlertService? _avvisi;

    private DatiCartella _salvati;
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

        Id = dettaglio.Id;
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

        if (InCaricamento)
            return;

        if (e.PropertyName is nameof(Titolo) or nameof(Descrizione) or nameof(DataScadenza)
            or nameof(Completato) or nameof(DataCompletamento))
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
            ApplicaDettaglio(await _archivio.AggiornaCartellaAsync(Id, dati));
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
    }

    // ---------- Allegati ----------

    [RelayCommand]
    private async Task AllegaAsync()
    {
        var scelti = _dialog.SelezionaFile("Allega documenti");
        if (scelti.Count == 0)
            return;

        try
        {
            var nuovi = await _archivio.AllegaDocumentiAsync(Id, scelti);
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
                NumeroDocumentiCambiato?.Invoke(Documenti.Count);
                break;
            case EsitoEliminazione.NonPiuEsistente:
                RicaricaRichiesta?.Invoke();
                break;
        }
    }
}
