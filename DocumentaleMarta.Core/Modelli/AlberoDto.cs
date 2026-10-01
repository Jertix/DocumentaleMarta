namespace DocumentaleMarta.Core.Modelli;

/// <summary>Cartella come serve all'albero: solo i dati da mostrare, senza entità tracciate.</summary>
public record CartellaNodo(
    int Id, string Titolo, string PercorsoRelativo, int NumeroDocumenti, DateOnly? DataScadenza, bool Completato,
    bool Archiviata = false);

/// <summary>Area con le sue cartelle, già ordinate per la visualizzazione.</summary>
public record AreaNodo(int Id, string Nome, string PercorsoRelativo, IReadOnlyList<CartellaNodo> Cartelle);

/// <summary>Una cartella non completata con una scadenza: la materia prima per l'elenco "Scadenze".</summary>
public record CartellaScadenza(
    int CartellaId, string Titolo, int AreaId, string NomeArea, DateOnly DataScadenza, int NumeroDocumenti);
