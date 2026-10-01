namespace DocumentaleMarta.Core.Modelli;

/// <summary>I campi di una cartella che l'utente può modificare.</summary>
/// <param name="Ricorrenza">Ogni quanto si ripete. Senza una scadenza non ha senso e viene ignorata (resta "Nessuna").</param>
public record DatiCartella(
    string Titolo,
    string? Descrizione,
    DateOnly? DataScadenza,
    bool Completato,
    DateOnly? DataCompletamento,
    Ricorrenza Ricorrenza = Ricorrenza.Nessuna);

public record DocumentoDettaglio(
    int Id,
    string NomeFile,
    string Estensione,
    long Dimensione,
    DateTime DataCaricamento,
    string PercorsoRelativo);

public record CartellaDettaglio(
    int Id,
    int AreaId,
    string NomeArea,
    string PercorsoRelativo,
    DatiCartella Dati,
    IReadOnlyList<DocumentoDettaglio> Documenti);
