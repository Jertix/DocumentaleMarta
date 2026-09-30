using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>Una riga della griglia di radice o area: un documento con la cartella e l'area a cui appartiene.</summary>
public partial class DocumentoElencoViewModel(
    DocumentoElenco dati, bool fileMancante, StatoAvviso avviso, ElencoDocumentiViewModel elenco) : ObservableObject
{
    public int Id { get; } = dati.Id;
    public int CartellaId { get; } = dati.CartellaId;
    public string NomeFile { get; } = dati.NomeFile;

    /// <summary>"PDF", "DOCX"...</summary>
    public string Tipo { get; } = dati.Estensione.TrimStart('.').ToUpperInvariant();

    public string TitoloCartella { get; } = dati.TitoloCartella;
    public string NomeArea { get; } = dati.NomeArea;

    public DateTime DataCaricamento { get; } = dati.DataCaricamento;

    /// <summary>Scadenza della cartella del documento, se ce l'ha.</summary>
    public DateTime? Scadenza { get; } = dati.ScadenzaCartella?.ToDateTime(TimeOnly.MinValue);

    public string PercorsoRelativo { get; } = dati.PercorsoRelativo;

    /// <summary>Urgenza della scadenza della cartella del documento (la riga si colora di conseguenza).</summary>
    public StatoAvviso Avviso { get; } = avviso;

    [ObservableProperty]
    private bool _fileMancante = fileMancante;

    [RelayCommand]
    private void Apri() => elenco.ApriDocumento(this);

    [RelayCommand]
    private void ApriNellaCartella() => elenco.MostraDocumentoInEsplora(this);

    [RelayCommand]
    private Task EliminaAsync() => elenco.EliminaDocumentoAsync(this);

    /// <summary>Seleziona nell'albero la cartella che contiene il documento (doppio clic sulla riga).</summary>
    [RelayCommand]
    private void VaiAllaCartella() => elenco.VaiAllaCartella(this);
}

/// <summary>La griglia dei documenti di tutta l'archivio (radice) o di una sola area.</summary>
public class ElencoDocumentiViewModel
{
    private readonly IArchivioService _archivio;
    private readonly IArchivioFileService _files;
    private readonly AzioniDocumenti _azioni;
    private readonly int? _areaId;
    private readonly AlertService? _avvisi;

    /// <param name="areaId">L'area da mostrare; null per tutti i documenti dell'archivio.</param>
    /// <param name="avvisi">Per colorare le righe secondo la scadenza della cartella; senza, le righe restano senza colore.</param>
    public ElencoDocumentiViewModel(
        IArchivioService archivio, IArchivioFileService files, IDialogService dialog, IShellService shell, int? areaId,
        AlertService? avvisi = null)
    {
        _archivio = archivio;
        _files = files;
        _azioni = new AzioniDocumenti(archivio, files, dialog, shell);
        _areaId = areaId;
        _avvisi = avvisi;
    }

    /// <summary>La colonna "Area" serve solo quando si vedono i documenti di più aree.</summary>
    public bool MostraArea => _areaId is null;

    public string TestoVuoto => _areaId is null
        ? "Nessun documento nell'archivio."
        : "Nessun documento in quest'area.";

    public ObservableCollection<DocumentoElencoViewModel> Documenti { get; } = [];

    /// <summary>L'utente ha fatto doppio clic su un documento: serve selezionare la sua cartella (id cartella).</summary>
    public event Action<int>? VaiAllaCartellaRichiesto;

    /// <summary>Un documento è stato eliminato da questa griglia (id della sua cartella), per aggiornare i contatori dell'albero.</summary>
    public event Action<int>? DocumentoEliminato;

    /// <summary>Un documento non esiste più nell'archivio: la griglia e l'albero sono vecchi e vanno riletti.</summary>
    public event Action? RicaricaRichiesta;

    public async Task CaricaAsync()
    {
        var elenco = await _archivio.CaricaDocumentiAsync(_areaId);

        // Un controllo su disco per ogni documento: fuori dal thread dell'interfaccia, con migliaia di file non deve bloccarla.
        var mancanti = await Task.Run(() => elenco.Select(d => !_files.Esiste(d.PercorsoRelativo)).ToList());

        Documenti.Clear();
        for (var i = 0; i < elenco.Count; i++)
        {
            var avviso = _avvisi?.Valuta(elenco[i].ScadenzaCartella, elenco[i].CartellaCompletata) ?? StatoAvviso.Nessuno;
            Documenti.Add(new DocumentoElencoViewModel(elenco[i], mancanti[i], avviso, this));
        }
    }

    internal void ApriDocumento(DocumentoElencoViewModel documento)
    {
        documento.FileMancante = !_azioni.FileEsiste(documento.PercorsoRelativo);
        if (!documento.FileMancante)
            _azioni.Apri(documento.PercorsoRelativo, documento.Tipo);
    }

    internal void MostraDocumentoInEsplora(DocumentoElencoViewModel documento)
    {
        documento.FileMancante = !_azioni.FileEsiste(documento.PercorsoRelativo);
        if (!documento.FileMancante)
            _azioni.MostraInEsplora(documento.PercorsoRelativo);
    }

    internal async Task EliminaDocumentoAsync(DocumentoElencoViewModel documento)
    {
        switch (await _azioni.EliminaAsync(documento.Id, documento.NomeFile))
        {
            case EsitoEliminazione.Eliminato:
                Documenti.Remove(documento);
                DocumentoEliminato?.Invoke(documento.CartellaId);
                break;
            case EsitoEliminazione.NonPiuEsistente:
                RicaricaRichiesta?.Invoke();
                break;
        }
    }

    internal void VaiAllaCartella(DocumentoElencoViewModel documento) =>
        VaiAllaCartellaRichiesto?.Invoke(documento.CartellaId);
}
