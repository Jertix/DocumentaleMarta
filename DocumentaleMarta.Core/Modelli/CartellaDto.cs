namespace DocumentaleMarta.Core.Modelli;

/// <summary>I campi di una cartella che l'utente può modificare.</summary>
/// <param name="Ricorrenza">Ogni quanto si ripete. Senza una scadenza non ha senso e viene ignorata (resta "Nessuna").</param>
/// <param name="NoteCompletamento">
/// Le note scritte completando la cartella. Una cartella non completata non ne ha (come per la data di completamento).
/// </param>
public record DatiCartella(
    string Titolo,
    string? Descrizione,
    DateOnly? DataScadenza,
    bool Completato,
    DateOnly? DataCompletamento,
    Ricorrenza Ricorrenza = Ricorrenza.Nessuna,
    string? NoteCompletamento = null);

/// <summary>
/// Un documento di una cartella, come lo mostra la griglia (nome, tipo, dimensione, data di caricamento e dove si trova
/// nell'archivio).
/// </summary>
public record DocumentoDettaglio(
    int Id,
    string NomeFile,
    string Estensione,
    long Dimensione,
    DateTime DataCaricamento,
    string PercorsoRelativo);

/// <summary>Una cartella con tutti i suoi dati, i suoi documenti e se è nell'«Archivio completati».</summary>
public record CartellaDettaglio(
    int Id,
    int AreaId,
    string NomeArea,
    string PercorsoRelativo,
    DatiCartella Dati,
    IReadOnlyList<DocumentoDettaglio> Documenti,
    bool Archiviata = false);
