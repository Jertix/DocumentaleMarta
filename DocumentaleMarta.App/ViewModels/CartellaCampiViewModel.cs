using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentaleMarta.Core.Modelli;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>Una voce del menu "Si ripete".</summary>
public record RicorrenzaOpzione(Ricorrenza Valore, string Testo)
{
    public static readonly IReadOnlyList<RicorrenzaOpzione> Tutte =
    [
        new(Ricorrenza.Nessuna, "Mai"),
        new(Ricorrenza.Mensile, "Ogni mese"),
        new(Ricorrenza.Trimestrale, "Ogni 3 mesi"),
        new(Ricorrenza.Annuale, "Ogni anno")
    ];
}

/// <summary>I campi di una cartella, uguali nella finestra di creazione e nel form di modifica.</summary>
public abstract partial class CartellaCampiViewModel : ObservableObject
{
    [ObservableProperty]
    private string _titolo = "";

    [ObservableProperty]
    private string? _descrizione;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaScadenza))]
    private DateTime? _dataScadenza;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SiRipete))]
    private Ricorrenza _ricorrenza;

    [ObservableProperty]
    private bool _completato;

    [ObservableProperty]
    private DateTime? _dataCompletamento;

    /// <summary>La ricorrenza si calcola dalla scadenza: senza, il menu "Si ripete" è spento.</summary>
    public bool HaScadenza => DataScadenza is not null;

    /// <summary>La cartella si ripete: mostra il suggerimento sulla proposta della cartella successiva.</summary>
    public bool SiRipete => Ricorrenza != Ricorrenza.Nessuna;

    public IReadOnlyList<RicorrenzaOpzione> OpzioniRicorrenza => RicorrenzaOpzione.Tutte;

    /// <summary>Togliendo la scadenza la ricorrenza non ha più senso: torna a "Mai".</summary>
    partial void OnDataScadenzaChanged(DateTime? value)
    {
        if (value is null && !InCaricamento)
            Ricorrenza = Ricorrenza.Nessuna;
    }

    /// <summary>Vero mentre si caricano valori già salvati: in quel momento le modifiche non sono dell'utente.</summary>
    protected bool InCaricamento { get; private set; }

    /// <summary>Spuntando "Completato" la data di completamento diventa oggi (modificabile); togliendo la spunta sparisce.</summary>
    partial void OnCompletatoChanged(bool value)
    {
        if (InCaricamento)
            return;
        DataCompletamento = value ? DataCompletamento ?? DateTime.Today : null;
    }

    /// <summary>Toglie la data di scadenza dalla cartella.</summary>
    [RelayCommand]
    private void CancellaScadenza() => DataScadenza = null;

    public DatiCartella Dati => new(
        Titolo.Trim(),
        string.IsNullOrWhiteSpace(Descrizione) ? null : Descrizione,
        ADateOnly(DataScadenza),
        Completato,
        Completato ? ADateOnly(DataCompletamento) : null,
        DataScadenza is null ? Ricorrenza.Nessuna : Ricorrenza);

    /// <summary>
    /// Riempie i campi con i dati già salvati, senza che il cambio conti come una modifica dell'utente (quindi senza far
    /// partire il salvataggio automatico).
    /// </summary>
    protected void Carica(DatiCartella dati)
    {
        InCaricamento = true;
        try
        {
            Titolo = dati.Titolo;
            Descrizione = dati.Descrizione;
            DataScadenza = ADateTime(dati.DataScadenza);
            Ricorrenza = dati.Ricorrenza;
            Completato = dati.Completato;
            DataCompletamento = ADateTime(dati.DataCompletamento);
        }
        finally
        {
            InCaricamento = false;
        }
    }

    /// <summary>Rimette il titolo a un valore senza che conti come una modifica dell'utente.</summary>
    protected void ImpostaTitoloSenzaEffetti(string titolo)
    {
        InCaricamento = true;
        try { Titolo = titolo; }
        finally { InCaricamento = false; }
    }

    /// <summary>Converte la data scelta nel calendario del form (con l'ora) nella sola data usata dall'archivio.</summary>
    private static DateOnly? ADateOnly(DateTime? data) => data is { } d ? DateOnly.FromDateTime(d) : null;

    /// <summary>
    /// Converte la sola data dell'archivio in una data con l'ora (mezzanotte) per il calendario del form.
    /// </summary>
    private static DateTime? ADateTime(DateOnly? data) => data is { } d ? d.ToDateTime(TimeOnly.MinValue) : null;
}
