using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DocumentaleMarta.App.ViewModels;

public enum TipoNodo
{
    Radice,
    Area,
    Cartella
}

/// <summary>Un nodo dell'albero a sinistra: la radice "Tutti i documenti", un'area o una cartella.</summary>
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
    private string _nome = nome;

    [ObservableProperty]
    private string _percorsoRelativo = percorsoRelativo;

    /// <summary>Cartelle contenute (per la radice e le aree). Serve ai riepiloghi e alle conferme di eliminazione.</summary>
    public int NumeroCartelle => Tipo switch
    {
        TipoNodo.Radice => Figli.Sum(a => a.Figli.Count),
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

    // Glifi di Segoe Fluent Icons / Segoe MDL2 Assets: casa, libreria, cartella.
    public string Icona => Tipo switch
    {
        TipoNodo.Radice => "",
        TipoNodo.Area => "",
        _ => ""
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
