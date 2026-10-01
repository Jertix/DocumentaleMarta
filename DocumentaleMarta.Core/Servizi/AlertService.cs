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

    /// <summary>"Scaduta da 3 giorni", "Scade oggi", "Scade domani", "Scade tra 5 giorni".</summary>
    public string Descrivi(DateOnly scadenza) => Giorni(scadenza) switch
    {
        < -1 and var g => $"Scaduta da {-g} giorni",
        -1 => "Scaduta ieri",
        0 => "Scade oggi",
        1 => "Scade domani",
        var g => $"Scade tra {g} giorni"
    };

    /// <summary>Il più grave dei due.</summary>
    public static StatoAvviso Peggiore(StatoAvviso a, StatoAvviso b) => a >= b ? a : b;
}
