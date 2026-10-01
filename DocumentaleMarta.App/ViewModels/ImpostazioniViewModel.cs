using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DocumentaleMarta.Core.Impostazioni;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>
/// I valori della finestra "Impostazioni": una copia che l'utente modifica. Le impostazioni vere cambiano solo
/// se si conferma e i valori sono validi.
/// </summary>
public partial class ImpostazioniViewModel : ObservableObject
{
    private readonly ImpostazioniApp _partenza;

    public ImpostazioniViewModel(ImpostazioniApp attuali, string percorsoFile)
    {
        _partenza = attuali.Clona();
        PercorsoFile = percorsoFile;

        _ragioneSociale = attuali.Azienda.RagioneSociale;
        _codiceFiscale = attuali.Azienda.CodiceFiscale;
        _partitaIva = attuali.Azienda.PartitaIva;
        _indirizzo = attuali.Azienda.Indirizzo;
        _descrizione = attuali.Azienda.Descrizione;
        _nomeRadice = attuali.NomeRadice;
        _avvisiAttivi = attuali.AvvisiAttivi;
        _riepilogoAvvio = attuali.RiepilogoAvvio;
        _sogliaArancione = attuali.SogliaArancioneGiorni.ToString(CultureInfo.InvariantCulture);
        _sogliaRossa = attuali.SogliaRossaGiorni.ToString(CultureInfo.InvariantCulture);
    }

    [ObservableProperty] private string _ragioneSociale;
    [ObservableProperty] private string _codiceFiscale;
    [ObservableProperty] private string _partitaIva;
    [ObservableProperty] private string _indirizzo;
    [ObservableProperty] private string _descrizione;
    [ObservableProperty] private string _nomeRadice;
    [ObservableProperty] private bool _avvisiAttivi;
    [ObservableProperty] private bool _riepilogoAvvio;

    /// <summary>Giorni di preavviso per l'arancione, come testo: finché l'utente scrive può non essere ancora un numero.</summary>
    [ObservableProperty] private string _sogliaArancione;

    [ObservableProperty] private string _sogliaRossa;

    /// <summary>Dove sta l'archivio: non si cambia da qui (spostarlo vuol dire spostare anche i file).</summary>
    public string PercorsoRadice => _partenza.PercorsoRadice;

    /// <summary>Il file in cui le impostazioni vengono salvate.</summary>
    public string PercorsoFile { get; }

    /// <summary>Il primo problema trovato nei valori, o vuoto se si può salvare.</summary>
    public string Errore
    {
        get
        {
            if (!TryLeggiGiorni(SogliaArancione, out var arancione))
                return "La soglia arancione deve essere un numero intero di giorni.";
            if (!TryLeggiGiorni(SogliaRossa, out var rossa))
                return "La soglia rossa deve essere un numero intero di giorni.";

            var problemi = Costruisci(arancione, rossa).Valida();
            return problemi.Count > 0 ? problemi[0] : "";
        }
    }

    public bool PuoSalvare => Errore.Length == 0;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(Errore) or nameof(PuoSalvare))
            return;

        OnPropertyChanged(nameof(Errore));
        OnPropertyChanged(nameof(PuoSalvare));
    }

    /// <summary>Le impostazioni con i valori scelti (da chiamare quando <see cref="PuoSalvare"/> è vero).</summary>
    public ImpostazioniApp Costruisci()
    {
        if (!TryLeggiGiorni(SogliaArancione, out var arancione) || !TryLeggiGiorni(SogliaRossa, out var rossa))
            throw new InvalidOperationException("Le soglie non sono numeri validi.");
        return Costruisci(arancione, rossa);
    }

    private ImpostazioniApp Costruisci(int arancione, int rossa)
    {
        var risultato = _partenza.Clona();
        risultato.Azienda.RagioneSociale = RagioneSociale.Trim();
        risultato.Azienda.CodiceFiscale = CodiceFiscale.Trim();
        risultato.Azienda.PartitaIva = PartitaIva.Trim();
        risultato.Azienda.Indirizzo = Indirizzo.Trim();
        risultato.Azienda.Descrizione = Descrizione.Trim();
        risultato.NomeRadice = NomeRadice.Trim();
        risultato.AvvisiAttivi = AvvisiAttivi;
        risultato.RiepilogoAvvio = RiepilogoAvvio;
        risultato.SogliaArancioneGiorni = arancione;
        risultato.SogliaRossaGiorni = rossa;
        return risultato;
    }

    private static bool TryLeggiGiorni(string? testo, out int giorni) =>
        int.TryParse(testo?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out giorni);
}

/// <summary>Tutto ciò che mostra la finestra "Informazioni".</summary>
public record InformazioniViewModel(
    string Nome,
    string Versione,
    string Ditta,
    string CodiceFiscale,
    string PartitaIva,
    string Indirizzo,
    string Descrizione,
    string PercorsoArchivio,
    string PercorsoDatabase,
    string PercorsoImpostazioni,
    string Contenuto,
    string StatoOcr,
    string Ambiente);
