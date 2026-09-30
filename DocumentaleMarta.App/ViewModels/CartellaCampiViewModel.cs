using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentaleMarta.Core.Modelli;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>I campi di una cartella, uguali nella finestra di creazione e nel form di modifica.</summary>
public abstract partial class CartellaCampiViewModel : ObservableObject
{
    [ObservableProperty]
    private string _titolo = "";

    [ObservableProperty]
    private string? _descrizione;

    [ObservableProperty]
    private DateTime? _dataScadenza;

    [ObservableProperty]
    private bool _completato;

    [ObservableProperty]
    private DateTime? _dataCompletamento;

    /// <summary>Vero mentre si caricano valori già salvati: in quel momento le modifiche non sono dell'utente.</summary>
    protected bool InCaricamento { get; private set; }

    /// <summary>Spuntando "Completato" la data di completamento diventa oggi (modificabile); togliendo la spunta sparisce.</summary>
    partial void OnCompletatoChanged(bool value)
    {
        if (InCaricamento)
            return;
        DataCompletamento = value ? DataCompletamento ?? DateTime.Today : null;
    }

    [RelayCommand]
    private void CancellaScadenza() => DataScadenza = null;

    public DatiCartella Dati => new(
        Titolo.Trim(),
        string.IsNullOrWhiteSpace(Descrizione) ? null : Descrizione,
        ADateOnly(DataScadenza),
        Completato,
        Completato ? ADateOnly(DataCompletamento) : null);

    protected void Carica(DatiCartella dati)
    {
        InCaricamento = true;
        try
        {
            Titolo = dati.Titolo;
            Descrizione = dati.Descrizione;
            DataScadenza = ADateTime(dati.DataScadenza);
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

    private static DateOnly? ADateOnly(DateTime? data) => data is { } d ? DateOnly.FromDateTime(d) : null;

    private static DateTime? ADateTime(DateOnly? data) => data is { } d ? d.ToDateTime(TimeOnly.MinValue) : null;
}
