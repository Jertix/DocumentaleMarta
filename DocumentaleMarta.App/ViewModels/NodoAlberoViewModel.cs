using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.App.ViewModels;

public enum TipoNodo
{
    Radice,

    /// <summary>Nodo speciale sotto la radice: non corrisponde a nessuna cartella su disco, mostra le scadenze in arrivo.</summary>
    Scadenze,

    Area,
    Cartella
}

/// <summary>Un nodo dell'albero a sinistra: la radice "Tutti i documenti", il nodo "Scadenze", un'area o una cartella.</summary>
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

    /// <summary>Per le cartelle completate: la spiegazione dell'icona, per il suggerimento che compare passandoci il mouse.</summary>
    public string DescrizioneStato => Tipo == TipoNodo.Cartella && Completato ? "Cartella completata" : "";

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

    /// <summary>Il testo mostrato nell'albero: per "Scadenze" comprende il numero, es. "Scadenze (3)".</summary>
    public string Testo => Tipo == TipoNodo.Scadenze && NumeroAvvisi > 0 ? $"{Nome} ({NumeroAvvisi})" : Nome;

    // ---------- Contatori ----------

    /// <summary>Aree contenute (per la radice): il nodo "Scadenze" non è un'area.</summary>
    public IEnumerable<NodoAlberoViewModel> Aree => Figli.Where(f => f.Tipo == TipoNodo.Area);

    /// <summary>Cartelle contenute (per la radice e le aree). Serve ai riepiloghi e alle conferme di eliminazione.</summary>
    public int NumeroCartelle => Tipo switch
    {
        TipoNodo.Radice => Aree.Sum(a => a.Figli.Count),
        TipoNodo.Area => Figli.Count,
        _ => 0
    };

    /// <summary>Documenti contenuti: per radice e aree la somma delle cartelle sottostanti.</summary>
    public int NumeroDocumenti => Tipo == TipoNodo.Cartella ? _documentiDellaCartella : Figli.Sum(f => f.NumeroDocumenti);

    public void ImpostaNumeroDocumenti(int numero) => _documentiDellaCartella = numero;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
            selezionato(this);
    }

    /// <summary>Identifica il nodo tra un caricamento dell'albero e il successivo (gli oggetti vengono ricreati).</summary>
    public string Chiave => CreaChiave(Tipo, Id);

    public static string CreaChiave(TipoNodo tipo, int id) => $"{tipo}:{id}";

    // Glifi di Segoe Fluent Icons / Segoe MDL2 Assets: casa, calendario, libreria, cartella e, per le completate, un cerchio con la spunta.
    public const string IconaRadice = "\uE80F";
    public const string IconaScadenze = "\uE787";
    public const string IconaArea = "\uE8F1";
    public const string IconaCartella = "\uE8B7";
    public const string IconaCartellaCompletata = "\uE930";

    public string Icona => Tipo switch
    {
        TipoNodo.Radice => IconaRadice,
        TipoNodo.Scadenze => IconaScadenze,
        TipoNodo.Area => IconaArea,
        _ when Completato => IconaCartellaCompletata,
        _ => IconaCartella
    };

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
