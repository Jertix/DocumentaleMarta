using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>Una riga della griglia di radice o area: un documento con la cartella e l'area a cui appartiene.</summary>
public partial class DocumentoElencoViewModel(
    DocumentoElenco dati, bool fileMancante, StatoAvviso avviso, ElencoDocumentiViewModel elenco, string? trovato = null)
    : ObservableObject, IOrigineDocumento
{
    public int Id { get; } = dati.Id;
    public int CartellaId { get; } = dati.CartellaId;
    int IOrigineDocumento.DocumentoId => Id;
    int IOrigineDocumento.CartellaOrigineId => CartellaId;
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

    /// <summary>
    /// Solo nei risultati di una ricerca: l'estratto del testo con le parole trovate tra i segnaposto
    /// <see cref="RisultatoRicerca.InizioEvidenza"/> e <see cref="RisultatoRicerca.FineEvidenza"/>, oppure il nome del campo in cui sono state trovate.
    /// </summary>
    public string Trovato { get; } = trovato ?? "";

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

/// <summary>La griglia dei documenti di tutta l'archivio (radice), di una sola area o i risultati di una ricerca.</summary>
public class ElencoDocumentiViewModel
{
    private readonly IArchivioService _archivio;
    private readonly IArchivioFileService _files;
    private readonly AzioniDocumenti _azioni;
    private readonly int? _areaId;
    private readonly AlertService? _avvisi;
    private readonly IRicercaService? _ricerca;
    private readonly string? _testoRicerca;
    private readonly FiltriRicerca? _filtri;

    /// <param name="areaId">L'area da mostrare; null per tutti i documenti dell'archivio.</param>
    /// <param name="avvisi">Per colorare le righe secondo la scadenza della cartella; senza, le righe restano senza colore.</param>
    /// <param name="ricerca">Insieme a <paramref name="testoRicerca"/>: invece dei documenti di un'area mostra i risultati della ricerca.</param>
    /// <param name="filtri">I filtri della ricerca avanzata (solo per i risultati di una ricerca).</param>
    public ElencoDocumentiViewModel(
        IArchivioService archivio, IArchivioFileService files, IDialogService dialog, IShellService shell, int? areaId,
        AlertService? avvisi = null, IRicercaService? ricerca = null, string? testoRicerca = null, FiltriRicerca? filtri = null)
    {
        _archivio = archivio;
        _files = files;
        _azioni = new AzioniDocumenti(archivio, files, dialog, shell);
        _areaId = areaId;
        _avvisi = avvisi;
        _ricerca = ricerca;
        _testoRicerca = testoRicerca;
        _filtri = filtri;
    }

    /// <summary>Questa griglia mostra i risultati di una ricerca.</summary>
    public bool IsRicerca => _ricerca is not null && _testoRicerca is not null;

    /// <summary>Il testo cercato (solo per i risultati di una ricerca).</summary>
    public string TestoRicerca => _testoRicerca ?? "";

    /// <summary>Ci sono più risultati di quelli mostrati: conviene restringere la ricerca.</summary>
    public bool Troncato { get; private set; }

    /// <summary>La colonna "Area" serve solo quando si vedono i documenti di più aree.</summary>
    public bool MostraArea => _areaId is null;

    /// <summary>La colonna "Trovato" serve solo nei risultati di una ricerca.</summary>
    public bool MostraTrovato => IsRicerca;

    /// <summary>La ricerca usa dei filtri oltre alle parole (o al posto loro).</summary>
    public bool HaFiltri => _filtri?.HaFiltri == true;

    public string TestoVuoto => IsRicerca
        ? string.IsNullOrWhiteSpace(_testoRicerca)
            ? "Nessun documento corrisponde ai filtri scelti."
            : HaFiltri
                ? $"Nessun documento trovato per «{_testoRicerca}» con i filtri scelti."
                : $"Nessun documento trovato per «{_testoRicerca}»."
        : _areaId is null
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
        List<(DocumentoElenco Documento, string? Trovato)> righe;
        if (IsRicerca)
        {
            var esito = await _ricerca!.CercaAsync(_testoRicerca!, _filtri);
            Troncato = esito.Troncato;
            righe = esito.Risultati.Select(r => (r.Documento, (string?)r.Trovato)).ToList();
        }
        else
        {
            righe = (await _archivio.CaricaDocumentiAsync(_areaId)).Select(d => (d, (string?)null)).ToList();
        }

        // Un controllo su disco per ogni documento: fuori dal thread dell'interfaccia, con migliaia di file non deve bloccarla.
        var mancanti = await Task.Run(() => righe.Select(r => !_files.Esiste(r.Documento.PercorsoRelativo)).ToList());

        Documenti.Clear();
        for (var i = 0; i < righe.Count; i++)
        {
            var (documento, trovato) = righe[i];
            var avviso = _avvisi?.Valuta(documento.ScadenzaCartella, documento.CartellaCompletata) ?? StatoAvviso.Nessuno;
            Documenti.Add(new DocumentoElencoViewModel(documento, mancanti[i], avviso, this, trovato));
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
