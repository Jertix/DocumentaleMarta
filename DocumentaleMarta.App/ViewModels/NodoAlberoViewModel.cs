using System.Collections.ObjectModel;
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
    public TipoNodo Tipo { get; } = tipo;
    public int Id { get; } = id;
    public string Nome { get; } = nome;
    public string PercorsoRelativo { get; } = percorsoRelativo;
    public NodoAlberoViewModel? Padre { get; } = padre;
    public ObservableCollection<NodoAlberoViewModel> Figli { get; } = [];

    /// <summary>Cartelle contenute (per la radice e le aree). Serve ai riepiloghi e alle conferme di eliminazione.</summary>
    public int NumeroCartelle { get; init; }

    /// <summary>Documenti contenuti, anche nelle cartelle figlie.</summary>
    public int NumeroDocumenti { get; init; }

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
}
