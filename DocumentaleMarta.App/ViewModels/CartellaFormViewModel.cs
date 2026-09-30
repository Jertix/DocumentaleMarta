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

    private DatiCartella _salvati;
    private bool _salvataggioInCorso;
    private bool _salvataggioRichiesto;
    private Task _ultimoSalvataggio = Task.CompletedTask;

    public CartellaFormViewModel(
        IArchivioService archivio, IArchivioFileService files, IDialogService dialog, IShellService shell,
        CartellaDettaglio dettaglio)
    {
        _archivio = archivio;
        _files = files;
        _dialog = dialog;
        _shell = shell;

        Id = dettaglio.Id;
        NomeArea = dettaglio.NomeArea;
        PercorsoRelativo = dettaglio.PercorsoRelativo;
        _salvati = dettaglio.Dati;
        Carica(dettaglio.Dati);

        foreach (var documento in dettaglio.Documenti)
            Documenti.Add(new DocumentoViewModel(documento, !_files.Esiste(documento.PercorsoRelativo), this));

        Documenti.CollectionChanged += (_, _) => OnPropertyChanged(nameof(TitoloDocumenti));
        PropertyChanged += OnProprietaCambiata;
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

    /// <summary>Il numero di documenti è cambiato (allegati o eliminati).</summary>
    public event Action<int>? NumeroDocumentiCambiato;

    /// <summary>La cartella non esiste più nell'archivio: l'albero mostrato è vecchio e va riletto.</summary>
    public event Action? RicaricaRichiesta;

    /// <summary>Completa quando i salvataggi avviati fin qui sono finiti.</summary>
    public Task AttendiSalvataggioAsync() => _ultimoSalvataggio;

    // ---------- Salvataggio automatico ----------

    private void OnProprietaCambiata(object? mittente, PropertyChangedEventArgs e)
    {
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
        if (!VerificaFile(documento, out var percorso))
            return;

        try
        {
            _shell.ApriFile(percorso);
        }
        catch (Win32Exception)
        {
            _dialog.MostraErrore(
                $"Windows non ha un programma per aprire questo tipo di file ({documento.Tipo}).\n\nUsa «Apri nella cartella» e scegli tu il programma.");
        }
    }

    internal void MostraDocumentoInEsplora(DocumentoViewModel documento)
    {
        if (VerificaFile(documento, out var percorso))
            _shell.MostraFileInEsplora(percorso);
    }

    internal async Task EliminaDocumentoAsync(DocumentoViewModel documento)
    {
        if (!_dialog.Conferma(
                "Elimina documento",
                $"Eliminare il documento «{documento.NomeFile}»?\n\nIl file viene spostato nel Cestino di Windows."))
            return;

        try
        {
            await _archivio.EliminaDocumentoAsync(documento.Id);
            Documenti.Remove(documento);
            NumeroDocumentiCambiato?.Invoke(Documenti.Count);
        }
        catch (ArchivioException ex)
        {
            _dialog.MostraErrore(ex.Message);
            RicaricaRichiesta?.Invoke();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialog.MostraErrore(
                $"Non è stato possibile eliminare il documento: {ex.Message}\n\nControlla che non sia aperto in un altro programma.");
        }
    }

    /// <summary>Controlla che il file ci sia ancora sul disco, aggiornando la riga; se manca avvisa l'utente.</summary>
    private bool VerificaFile(DocumentoViewModel documento, out string percorso)
    {
        percorso = _files.PercorsoAssoluto(documento.PercorsoRelativo);
        var esiste = File.Exists(percorso);
        documento.FileMancante = !esiste;
        if (!esiste)
            _dialog.MostraErrore(
                $"Il file non si trova più nell'archivio:\n{percorso}\n\nPotrebbe essere stato spostato o eliminato da Esplora file.");
        return esiste;
    }
}
