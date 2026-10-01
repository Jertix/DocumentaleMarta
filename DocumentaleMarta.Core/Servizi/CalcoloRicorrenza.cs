using DocumentaleMarta.Core.Modelli;

namespace DocumentaleMarta.Core.Servizi;

/// <summary>Quando cade la scadenza successiva di una cartella che si ripete.</summary>
public static class CalcoloRicorrenza
{
    /// <summary>
    /// La scadenza dopo <paramref name="scadenza"/>: un mese, tre mesi o un anno più avanti.
    /// Se il giorno non esiste nel mese di arrivo si usa l'ultimo (il 31 gennaio + 1 mese = 28 febbraio).
    /// </summary>
    public static DateOnly Prossima(DateOnly scadenza, Ricorrenza ricorrenza) => ricorrenza switch
    {
        Ricorrenza.Mensile => scadenza.AddMonths(1),
        Ricorrenza.Trimestrale => scadenza.AddMonths(3),
        Ricorrenza.Annuale => scadenza.AddYears(1),
        _ => throw new ArgumentOutOfRangeException(nameof(ricorrenza), ricorrenza, "La cartella non si ripete.")
    };

    /// <summary>"ogni mese", "ogni 3 mesi", "ogni anno"; vuoto per una cartella che non si ripete.</summary>
    public static string Descrizione(Ricorrenza ricorrenza) => ricorrenza switch
    {
        Ricorrenza.Mensile => "ogni mese",
        Ricorrenza.Trimestrale => "ogni 3 mesi",
        Ricorrenza.Annuale => "ogni anno",
        _ => ""
    };
}
