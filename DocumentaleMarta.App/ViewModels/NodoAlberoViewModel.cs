using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>
/// I tipi di elemento dell'albero: la radice, il nodo «Scadenze», le aree, le cartelle e i due nodi dell'«Archivio
/// completati».
/// </summary>
public enum TipoNodo
{
    Radice,

    /// <summary>Nodo speciale sotto la radice: non corrisponde a nessuna cartella su disco, mostra le scadenze in arrivo.</summary>
    Scadenze,

    Area,
    Cartella,

    /// <summary>
    /// Nodo speciale in fondo alla radice: raccoglie le cartelle completate e archiviate. Non corrisponde a nessuna cartella su disco:
    /// le cartelle archiviate restano nella loro area e nella loro cartella fisica.
    /// </summary>
    Archivio,

    /// <summary>Dentro l'archivio, raggruppa le cartelle archiviate di una stessa area (ha lo stesso id e nome dell'area vera).</summary>
    AreaArchivio
}

/// <summary>
/// Un nodo dell'albero a sinistra: la radice "Tutti i documenti", il nodo "Scadenze", un'area, una cartella,
/// l'"Archivio completati" e i suoi gruppi per area.
/// </summary>
public partial class NodoAlberoViewModel(
    TipoNodo tipo, int id, string nome, string percorsoRelativo, NodoAlberoViewModel? padre,
    Action<NodoAlberoViewModel> selezionato) : ObservableObject
{
    private static readonly StringComparer OrdineAlfabetico =
        StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), ignoreCase: true);

    private int _documentiDellaCartella;

    public TipoNodo Tipo { get; } = tipo;
    public int Id { get; } = id;
    public NodoAlberoViewModel? Padre { get; } = padre;
    public ObservableCollection<NodoAlberoViewModel> Figli { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Testo))]
    private string _nome = nome;

    [ObservableProperty]
    private string _percorsoRelativo = percorsoRelativo;

    /// <summary>Scadenza della cartella (solo per i nodi cartella).</summary>
    public DateOnly? DataScadenza { get; private set; }

    /// <summary>La cartella è completata: non genera più avvisi e nell'albero ha un'icona e un colore suoi.</summary>
    public bool Completato { get; private set; }

    /// <summary>
    /// Aggiorna la scadenza e lo stato «completata» della cartella; spuntando «Completato» nel form l'icona cambia subito,
    /// senza rileggere l'albero.
    /// </summary>
    public void ImpostaScadenza(DateOnly? dataScadenza, bool completato)
    {
        DataScadenza = dataScadenza;
        if (Completato == completato)
            return;

        // Spuntando "Completato" nel form l'icona cambia subito, senza rileggere l'albero.
        Completato = completato;
        OnPropertyChanged(nameof(Completato));
        OnPropertyChanged(nameof(Icona));
        OnPropertyChanged(nameof(DescrizioneStato));
        OnPropertyChanged(nameof(NomeAccessibile));
    }

    /// <summary>La cartella sta nell'"Archivio completati" invece che nella sua area (solo per i nodi cartella).</summary>
    public bool Archiviata { get; private set; }

    /// <summary>Segna la cartella come archiviata (o no).</summary>
    public void ImpostaArchiviata(bool archiviata) => Archiviata = archiviata;

    /// <summary>Per le cartelle completate: la spiegazione dell'icona, per il suggerimento che compare passandoci il mouse.</summary>
    public string DescrizioneStato => Tipo != TipoNodo.Cartella || !Completato
        ? ""
        : Archiviata ? "Cartella completata e archiviata" : "Cartella completata";

    /// <summary>Il nome letto dai programmi per ipovedenti: dice anche se la cartella è completata (l'icona da sola non si legge).</summary>
    public string NomeAccessibile => Tipo == TipoNodo.Cartella && Completato ? $"{Testo} (completata)" : Testo;

    // ---------- Avvisi di scadenza ----------

    /// <summary>
    /// Per una cartella lo stato della sua scadenza; per un'area o per la radice il più grave tra quelli che contengono
    /// (così in cima all'albero si vede subito se c'è qualcosa da controllare).
    /// </summary>
    [ObservableProperty]
    private StatoAvviso _avviso;

    /// <summary>Spiegazione dell'avviso, per il suggerimento che compare passando il mouse sull'icona.</summary>
    [ObservableProperty]
    private string _descrizioneAvviso = "";

    /// <summary>Solo per il nodo "Scadenze": quante cartelle sono in scadenza o scadute.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Testo))]
    private int _numeroAvvisi;

    /// <summary>Il testo mostrato nell'albero: per "Scadenze" e "Archivio completati" comprende il numero, es. "Scadenze (3)".</summary>
    public string Testo => Tipo switch
    {
        TipoNodo.Scadenze when NumeroAvvisi > 0 => $"{Nome} ({NumeroAvvisi})",
        TipoNodo.Archivio when NumeroCartelle > 0 => $"{Nome} ({NumeroCartelle})",
        _ => Nome
    };

    // ---------- Contatori ----------

    /// <summary>Aree contenute (per la radice): i nodi "Scadenze" e "Archivio completati" non sono aree.</summary>
    public IEnumerable<NodoAlberoViewModel> Aree => Figli.Where(f => f.Tipo == TipoNodo.Area);

    private int _cartelleArchiviate;
    private int _documentiArchiviati;

    /// <summary>
    /// Di un'area: quante sue cartelle (e documenti) sono nell'archivio. Non sono tra i suoi figli nell'albero,
    /// ma contano nei totali e nell'avviso prima di eliminarla.
    /// </summary>
    public void ImpostaArchiviate(int cartelle, int documenti)
    {
        _cartelleArchiviate = cartelle;
        _documentiArchiviati = documenti;
    }

    /// <summary>Cartelle archiviate: per un'area le sue, per la radice quelle di tutte le aree.</summary>
    public int CartelleArchiviate => Tipo switch
    {
        TipoNodo.Radice => Aree.Sum(a => a.CartelleArchiviate),
        TipoNodo.Area => _cartelleArchiviate,
        _ => 0
    };

    /// <summary>Cartelle contenute, archiviate comprese (per la radice e le aree). Serve ai riepiloghi e alle conferme di eliminazione.</summary>
    public int NumeroCartelle => Tipo switch
    {
        TipoNodo.Radice => Aree.Sum(a => a.NumeroCartelle),
        TipoNodo.Area => Figli.Count + _cartelleArchiviate,
        TipoNodo.Archivio => Figli.Sum(g => g.Figli.Count),
        TipoNodo.AreaArchivio => Figli.Count,
        _ => 0
    };

    /// <summary>
    /// Documenti contenuti, anche quelli delle cartelle archiviate: una radice e un'area li comprendono tutti,
    /// l'archivio e i suoi gruppi contano solo quelli archiviati.
    /// </summary>
    public int NumeroDocumenti => Tipo switch
    {
        TipoNodo.Cartella => _documentiDellaCartella,
        TipoNodo.Radice => Aree.Sum(a => a.NumeroDocumenti),
        TipoNodo.Area => Figli.Sum(f => f.NumeroDocumenti) + _documentiArchiviati,
        _ => Figli.Sum(f => f.NumeroDocumenti)
    };

    /// <summary>Aggiorna il numero di documenti della cartella (dopo averne allegati o eliminati).</summary>
    public void ImpostaNumeroDocumenti(int numero) => _documentiDellaCartella = numero;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Quando il nodo viene selezionato lo comunica a chi governa l'albero.</summary>
    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            selezionato(this);
    }

    /// <summary>Identifica il nodo tra un caricamento dell'albero e il successivo (gli oggetti vengono ricreati).</summary>
    public string Chiave => CreaChiave(Tipo, Id);

    /// <summary>La chiave che identifica un nodo (tipo e numero), uguale prima e dopo la rilettura dell'albero.</summary>
    public static string CreaChiave(TipoNodo tipo, int id) => $"{tipo}:{id}";

    // Glifi di Segoe Fluent Icons / Segoe MDL2 Assets: casa, calendario, libreria, cartella e, per le completate, un cerchio con la spunta.
    public const string IconaRadice = "\uE80F";
    public const string IconaScadenze = "\uE787";
    public const string IconaArea = "\uE8F1"; // quella predefinita delle aree (vedi IconeArea.Predefinita)
    public const string IconaCartella = "\uE8B7";
    public const string IconaCartellaCompletata = "\uE930";
    public const string IconaArchivio = "\uE7B8"; // la scatola d'archivio

    /// <summary>Il glifo mostrato accanto al nome. Per le aree (e i loro gruppi nell'archivio) è l'icona scelta dall'utente.</summary>
    public string Icona => Tipo switch
    {
        TipoNodo.Radice => IconaRadice,
        TipoNodo.Scadenze => IconaScadenze,
        TipoNodo.Area or TipoNodo.AreaArchivio => IconeArea.Glifo(_codiceIconaArea),
        TipoNodo.Archivio => IconaArchivio,
        _ when Completato => IconaCartellaCompletata,
        _ => IconaCartella
    };

    private string? _codiceIconaArea;

    /// <summary>
    /// Il codice dell'icona scelta per l'area (solo per i nodi area); null per quella predefinita. È la sola cosa che
    /// cambia con «Cambia icona»: non serve rileggere l'albero.
    /// </summary>
    public string? CodiceIconaArea => _codiceIconaArea;

    /// <summary>Imposta l'icona scelta per l'area (null per quella predefinita) e aggiorna subito quella mostrata.</summary>
    public void ImpostaIcona(string? codice)
    {
        if (_codiceIconaArea == codice)
            return;

        _codiceIconaArea = codice;
        OnPropertyChanged(nameof(Icona));
    }

    /// <summary>Questo nodo e tutti quelli sotto di lui, a qualunque profondità.</summary>
    public IEnumerable<NodoAlberoViewModel> ConDiscendenti()
    {
        yield return this;
        foreach (var figlio in Figli)
            foreach (var nodo in figlio.ConDiscendenti())
                yield return nodo;
    }

    /// <summary>Cambia nome e percorso sul posto (la cartella è stata rinominata dal form) e rimette il nodo in ordine alfabetico.</summary>
    public void Rinomina(string nome, string percorsoRelativo)
    {
        Nome = nome;
        PercorsoRelativo = percorsoRelativo;

        if (Padre is null)
            return;

        var fratelli = Padre.Figli;
        var posizioneAttuale = fratelli.IndexOf(this);
        var posizioneGiusta = 0;
        foreach (var fratello in fratelli)
        {
            if (!ReferenceEquals(fratello, this) && OrdineAlfabetico.Compare(fratello.Nome, nome) <= 0)
                posizioneGiusta++;
        }

        if (posizioneGiusta != posizioneAttuale)
            fratelli.Move(posizioneAttuale, posizioneGiusta);
    }
}
