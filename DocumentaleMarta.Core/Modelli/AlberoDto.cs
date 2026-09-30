namespace DocumentaleMarta.Core.Modelli;

/// <summary>Cartella come serve all'albero: solo i dati da mostrare, senza entità tracciate.</summary>
public record CartellaNodo(int Id, string Titolo, string PercorsoRelativo, int NumeroDocumenti);

/// <summary>Area con le sue cartelle, già ordinate per la visualizzazione.</summary>
public record AreaNodo(int Id, string Nome, string PercorsoRelativo, IReadOnlyList<CartellaNodo> Cartelle);
