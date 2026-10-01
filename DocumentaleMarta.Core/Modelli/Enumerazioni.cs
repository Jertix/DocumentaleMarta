namespace DocumentaleMarta.Core.Modelli;

/// <summary>
/// Ogni quanto una cartella si ripete (per esempio le scadenze mensili); alla scadenza il programma propone la cartella
/// successiva.
/// </summary>
public enum Ricorrenza
{
    Nessuna = 0,
    Mensile = 1,
    Trimestrale = 2,
    Annuale = 3
}

/// <summary>A che punto è la lettura del testo di un documento, per poterlo cercare.</summary>
public enum StatoIndicizzazione
{
    DaIndicizzare = 0,
    Indicizzato = 1,
    /// <summary>Nessun testo estraibile (formato non supportato o file vuoto).</summary>
    NonSupportato = 2,
    Errore = 3
}
