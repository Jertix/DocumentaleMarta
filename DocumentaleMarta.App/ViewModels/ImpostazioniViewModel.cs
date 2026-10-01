using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DocumentaleMarta.Core.Impostazioni;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>Cosa l'utente ha chiesto di fare, oltre a salvare, premendo un pulsante della finestra "Impostazioni".</summary>
public enum AzioneDaImpostazioni
{
    /// <summary>Solo salvare (o annullare).</summary>
    Nessuna,

    /// <summary>Salvare e poi fare subito il backup.</summary>
    Backup,

    /// <summary>Salvare e poi ripristinare un backup.</summary>
    Ripristino
}

/// <summary>
/// I valori della finestra "Impostazioni": una copia che l'utente modifica. Le impostazioni vere cambiano solo
/// se si conferma e i valori sono validi.
/// </summary>
public partial class ImpostazioniViewModel : ObservableObject
{
    private readonly ImpostazioniApp _partenza;
    private readonly Action<AspettoApp>? _anteprimaAspetto;

    /// <param name="backupDisponibile">C'è il servizio che fa backup e ripristino: solo allora la finestra offre i due pulsanti.</param>
    /// <param name="anteprimaAspetto">Chiamata a ogni scelta di aspetto: il programma si vede subito con i colori scelti, prima ancora di salvare.</param>
    public ImpostazioniViewModel(ImpostazioniApp attuali, string percorsoFile, bool backupDisponibile = false,
        Action<AspettoApp>? anteprimaAspetto = null)
    {
        _partenza = attuali.Clona();
        PercorsoFile = percorsoFile;
        BackupDisponibile = backupDisponibile;
        _anteprimaAspetto = anteprimaAspetto;
        _tema = attuali.Tema;

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
        _cartellaBackup = attuali.CartellaBackupInUso ?? ""; // se non ne ha scelta una, si vede quella predefinita
        _promemoriaBackup = attuali.BackupPromemoriaGiorni.ToString(CultureInfo.InvariantCulture);
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

    /// <summary>Dove si salvano i backup; se non è mai stata scelta una cartella c'è quella predefinita, e lasciandola vuota si usa quella.</summary>
    [ObservableProperty] private string _cartellaBackup;

    /// <summary>La spiegazione sotto il campo della cartella dei backup (dice anche qual è la cartella predefinita).</summary>
    public string SuggerimentoCartellaBackup =>
        "Meglio un altro disco (una chiavetta o un disco esterno): così, se il PC si rompe, i backup si salvano. "
        + "Non può stare dentro la cartella dell'archivio. "
        + $"Se la lasci vuota si usa {ImpostazioniApp.CartellaBackupPredefinita}, che viene creata da sola.";

    /// <summary>Dopo quanti giorni dall'ultimo backup il programma lo ricorda, come testo.</summary>
    [ObservableProperty] private string _promemoriaBackup;

    /// <summary>Chiaro, scuro o come Windows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemaChiaro), nameof(TemaScuro), nameof(TemaComeWindows))]
    private TemaApp _tema;

    // Un pulsante di scelta per ogni tema (ognuno si lega a una proprietà vero/falso).
    public bool TemaChiaro { get => Tema == TemaApp.Chiaro; set { if (value) Tema = TemaApp.Chiaro; } }
    public bool TemaScuro { get => Tema == TemaApp.Scuro; set { if (value) Tema = TemaApp.Scuro; } }
    public bool TemaComeWindows { get => Tema == TemaApp.ComeWindows; set { if (value) Tema = TemaApp.ComeWindows; } }

    partial void OnTemaChanged(TemaApp value) => _anteprimaAspetto?.Invoke(new AspettoApp(value));

    /// <summary>I pulsanti "Fai il backup ora" e "Ripristina da un backup…" si vedono solo se c'è il servizio che li esegue.</summary>

    public bool BackupDisponibile { get; }

    /// <summary>
    /// Impostata dalla finestra quando si preme uno dei due pulsanti del backup: la finestra si chiude confermando (le impostazioni
    /// si salvano) e chi l'ha aperta esegue poi l'azione. Resta "Nessuna" se si preme solo Salva o Annulla.
    /// </summary>
    public AzioneDaImpostazioni AzioneRichiesta { get; set; }

    /// <summary>Quando è stato fatto l'ultimo backup (informazione, non si modifica da qui).</summary>
    public string UltimoBackupTesto => _partenza.UltimoBackup is { } data
        ? $"Ultimo backup: {data:dd/MM/yyyy} alle {data:HH:mm}"
        : "Non hai ancora fatto nessun backup.";

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
            if (!TryLeggiGiorni(PromemoriaBackup, out var promemoria))
                return "I giorni del promemoria del backup devono essere un numero intero.";

            var problemi = Costruisci(arancione, rossa, promemoria).Valida();
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
        if (!TryLeggiGiorni(SogliaArancione, out var arancione) || !TryLeggiGiorni(SogliaRossa, out var rossa)
            || !TryLeggiGiorni(PromemoriaBackup, out var promemoria))
            throw new InvalidOperationException("I giorni indicati non sono numeri validi.");
        return Costruisci(arancione, rossa, promemoria);
    }

    private ImpostazioniApp Costruisci(int arancione, int rossa, int promemoria)
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
        risultato.CartellaBackup = string.IsNullOrWhiteSpace(CartellaBackup) ? null : CartellaBackup.Trim();
        risultato.BackupPromemoriaGiorni = promemoria;
        risultato.Tema = Tema;
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
