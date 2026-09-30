namespace DocumentaleMarta.Core.Modelli;

public enum Ricorrenza
{
    Nessuna = 0,
    Mensile = 1,
    Trimestrale = 2,
    Annuale = 3
}

public enum StatoIndicizzazione
{
    DaIndicizzare = 0,
    Indicizzato = 1,
    /// <summary>Nessun testo estraibile (formato non supportato o file vuoto).</summary>
    NonSupportato = 2,
    Errore = 3
}
