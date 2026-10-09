using DocumentaleMarta.Core.Impostazioni;

namespace DocumentaleMarta.Core.Servizi;

/// <summary>Quanto è urgente una scadenza. L'ordine conta: un valore più alto è più grave.</summary>
public enum StatoAvviso
{
    Nessuno = 0,
    Arancione = 1,
    Rosso = 2
}

/// <summary>
/// Decide se una scadenza va segnalata: arancione se mancano al massimo <see cref="SogliaArancioneGiorni"/> giorni,
/// rosso se ne mancano al massimo <see cref="SogliaRossaGiorni"/> o se è già passata. Le cartelle completate
/// e quelle senza scadenza non generano mai avvisi, e neanche tutte le altre se gli avvisi sono disattivati.
/// </summary>
public class AlertService
{
    private readonly TimeProvider _tempo;

    /// <summary>
    /// Crea il servizio con le due soglie (arancione e rosso) e se gli avvisi sono attivi; le soglie devono essere coerenti
    /// (rossa non negativa, arancione maggiore della rossa).
    /// </summary>
    public AlertService(int sogliaArancioneGiorni, int sogliaRossaGiorni, bool attivo = true, TimeProvider? tempo = null)
    {
        if (sogliaRossaGiorni < 0)
            throw new ArgumentOutOfRangeException(nameof(sogliaRossaGiorni), "La soglia rossa non può essere negativa.");
        if (sogliaArancioneGiorni <= sogliaRossaGiorni)
            throw new ArgumentOutOfRangeException(nameof(sogliaArancioneGiorni), "La soglia arancione deve essere maggiore di quella rossa.");

        SogliaArancioneGiorni = sogliaArancioneGiorni;
        SogliaRossaGiorni = sogliaRossaGiorni;
        Attivo = attivo;
        _tempo = tempo ?? TimeProvider.System;
    }

    /// <summary>Crea il servizio con le soglie scelte nelle impostazioni.</summary>
    public AlertService(ImpostazioniApp impostazioni, TimeProvider? tempo = null)
        : this(impostazioni.SogliaArancioneGiorni, impostazioni.SogliaRossaGiorni, impostazioni.AvvisiAttivi, tempo)
    {
    }

    public int SogliaArancioneGiorni { get; }
    public int SogliaRossaGiorni { get; }
    public bool Attivo { get; }

    /// <summary>L'orologio usato: chi ricrea il servizio con soglie nuove lo riusa.</summary>
    public TimeProvider Tempo => _tempo;

    /// <summary>La data di oggi, secondo l'orologio del PC.</summary>
    public DateOnly Oggi => DateOnly.FromDateTime(_tempo.GetLocalNow().DateTime);

    /// <summary>Giorni che mancano alla scadenza: negativi se è già passata, 0 se scade oggi.</summary>
    public int Giorni(DateOnly scadenza) => scadenza.DayNumber - Oggi.DayNumber;

    /// <summary>
    /// Decide l'urgenza di una scadenza: nessuna se gli avvisi sono spenti, se la cartella è completata o non c'è scadenza;
    /// rossa se è passata o vicina; arancione se è nella soglia arancione.
    /// </summary>
    public StatoAvviso Valuta(DateOnly? scadenza, bool completato)
    {
        if (!Attivo || completato || scadenza is null)
            return StatoAvviso.Nessuno;

        var giorni = Giorni(scadenza.Value);
        if (giorni <= SogliaRossaGiorni)
            return StatoAvviso.Rosso;
        return giorni <= SogliaArancioneGiorni ? StatoAvviso.Arancione : StatoAvviso.Nessuno;
    }

    /// <summary>Fino a questi giorni la distanza si dice in giorni; oltre, in mesi e giorni («3 mesi e 4 giorni»).</summary>
    public const int GiorniDettiInGiorni = 30;

    /// <summary>
    /// "Scaduta da 3 giorni", "Scade oggi", "Scade domani", "Scade tra 5 giorni". Oltre 30 giorni la distanza si dice in
    /// mesi (e anni) e giorni: "Scade tra 3 mesi e 4 giorni", "Scaduta da 1 mese e 14 giorni".
    /// </summary>
    public string Descrivi(DateOnly scadenza)
    {
        var oggi = Oggi;
        return (scadenza.DayNumber - oggi.DayNumber) switch
        {
            < -1 => $"Scaduta da {Distanza(scadenza, oggi)}",
            -1 => "Scaduta ieri",
            0 => "Scade oggi",
            1 => "Scade domani",
            _ => $"Scade tra {Distanza(oggi, scadenza)}"
        };
    }

    /// <summary>
    /// La distanza tra due date (<paramref name="da"/> prima di <paramref name="a"/>) a parole: «5 giorni» fino a 30 giorni,
    /// poi mesi interi di calendario e i giorni che avanzano («3 mesi e 4 giorni», «1 anno, 2 mesi e 3 giorni»). Un mese
    /// è quello di calendario: dal 10 ottobre al 10 gennaio sono 3 mesi, qualunque sia il numero di giorni.
    /// </summary>
    private static string Distanza(DateOnly da, DateOnly a)
    {
        var giorni = a.DayNumber - da.DayNumber;
        if (giorni <= GiorniDettiInGiorni)
            return Plurale(giorni, "giorno", "giorni");

        // I mesi interi che stanno prima di «a» (un 31 gennaio + 1 mese è il 28 febbraio: il giorno si riporta alla fine del mese).
        var mesi = (a.Year - da.Year) * 12 + a.Month - da.Month;
        if (da.AddMonths(mesi) > a)
            mesi--;
        var giorniAvanzati = a.DayNumber - da.AddMonths(mesi).DayNumber;

        var parti = new List<string>(3);
        if (mesi / 12 > 0)
            parti.Add(Plurale(mesi / 12, "anno", "anni"));
        if (mesi % 12 > 0)
            parti.Add(Plurale(mesi % 12, "mese", "mesi"));
        if (giorniAvanzati > 0)
            parti.Add(Plurale(giorniAvanzati, "giorno", "giorni"));

        // «A», «A e B», «A, B e C»
        return parti.Count <= 1 ? string.Concat(parti) : string.Join(", ", parti.Take(parti.Count - 1)) + " e " + parti[^1];
    }

    /// <summary>«1 giorno», «2 giorni».</summary>
    private static string Plurale(int numero, string singolare, string plurale) =>
        $"{numero} {(numero == 1 ? singolare : plurale)}";

    /// <summary>Il più grave dei due.</summary>
    public static StatoAvviso Peggiore(StatoAvviso a, StatoAvviso b) => a >= b ? a : b;
}
