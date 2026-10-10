using DocumentaleMarta.Core.Modelli;

namespace DocumentaleMarta.Core.Servizi;

/// <summary>Quando cade la scadenza successiva di una cartella che si ripete.</summary>
public static class CalcoloRicorrenza
{
    /// <summary>
    /// La scadenza dopo <paramref name="scadenza"/>: uno, due o tre mesi o un anno più avanti.
    /// Se il giorno non esiste nel mese di arrivo si usa l'ultimo (il 31 gennaio + 1 mese = 28 febbraio).
    /// </summary>
    public static DateOnly Prossima(DateOnly scadenza, Ricorrenza ricorrenza) => ricorrenza switch
    {
        Ricorrenza.Mensile => scadenza.AddMonths(1),
        Ricorrenza.Bimestrale => scadenza.AddMonths(2),
        Ricorrenza.Trimestrale => scadenza.AddMonths(3),
        Ricorrenza.Annuale => scadenza.AddYears(1),
        _ => throw new ArgumentOutOfRangeException(nameof(ricorrenza), ricorrenza, "La cartella non si ripete.")
    };

    /// <summary>"ogni mese", "ogni 2 mesi", "ogni 3 mesi", "ogni anno"; vuoto per una cartella che non si ripete.</summary>
    public static string Descrizione(Ricorrenza ricorrenza) => ricorrenza switch
    {
        Ricorrenza.Mensile => "ogni mese",
        Ricorrenza.Bimestrale => "ogni 2 mesi",
        Ricorrenza.Trimestrale => "ogni 3 mesi",
        Ricorrenza.Annuale => "ogni anno",
        _ => ""
    };
}
