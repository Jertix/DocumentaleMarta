using System.ComponentModel;
using System.Globalization;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DocumentaleMarta.App.Grafica;
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
        _colore = attuali.Colore;
        _sfondoColorato = attuali.SfondoColorato;
        OpzioniColore = [.. Tavolozze.Tutte.Select(t => new OpzioneColoreViewModel(this, t))];

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
        _righeAnteprimaTesto = attuali.RigheAnteprimaTesto.ToString(CultureInfo.InvariantCulture);
        _ricercaXmlAttiva = attuali.RicercaXmlAttiva;
        _modoXml = attuali.ModoXml;
        _animazioniAttive = attuali.AnimazioniAttive;
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

    /// <summary>Quante righe mostra l'anteprima di un file di testo (.txt, .csv, .xml), come testo: finché l'utente scrive può non essere ancora un numero.</summary>
    [ObservableProperty] private string _righeAnteprimaTesto;

    /// <summary>Cercare anche dentro i file XML: se spenta, di un XML si cerca solo il nome.</summary>
    [ObservableProperty] private bool _ricercaXmlAttiva;

    /// <summary>Che cosa si cerca dentro i file XML (solo il testo, oppure tutto il file compresi i tag).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SoloTestoXml), nameof(TuttoIlFileXml))]
    private ModoRicercaXml _modoXml;

    // Un pulsante di scelta per ogni modo (ognuno si lega a una proprietà vero/falso).
    public bool SoloTestoXml { get => ModoXml == ModoRicercaXml.SoloTesto; set { if (value) ModoXml = ModoRicercaXml.SoloTesto; } }
    public bool TuttoIlFileXml { get => ModoXml == ModoRicercaXml.TuttoIlFile; set { if (value) ModoXml = ModoRicercaXml.TuttoIlFile; } }

    /// <summary>La spiegazione sotto le scelte sulla ricerca nei file XML (perché è spenta di base e cosa succede cambiandola).</summary>
    public string SuggerimentoRicercaXml =>
        "Un file XML può essere molto grande (per esempio un file di log): leggerlo per la ricerca pesa sull'archivio, per questo di base "
        + "non si fa e di un XML si cerca solo il nome. Se li attivi, gli XML si leggono in background. "
        + "«Solo nel testo» cerca i valori scritti nel file, senza i nomi dei tag. "
        + "Cambiando queste scelte gli XML già presenti si rileggono (o, se spegni la ricerca, si tolgono dall'indice).";

    /// <summary>La spiegazione sotto il campo delle righe dell'anteprima di testo (dice anche i limiti).</summary>
    public string SuggerimentoRigheAnteprimaTesto =>
        $"Un file di testo si vede nel pannello dell'anteprima solo per le prime righe (da {ImpostazioniApp.RigheAnteprimaMinime} a "
        + $"{ImpostazioniApp.RigheAnteprimaMassime}; in genere ne bastano {ImpostazioniApp.RigheAnteprimaPredefinite}). Il resto si legge aprendo il file.";

    /// <summary>Chiaro, scuro o come Windows.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TemaChiaro), nameof(TemaScuro), nameof(TemaComeWindows))]
    private TemaApp _tema;

    // Un pulsante di scelta per ogni tema (ognuno si lega a una proprietà vero/falso).
    public bool TemaChiaro { get => Tema == TemaApp.Chiaro; set { if (value) Tema = TemaApp.Chiaro; } }
    public bool TemaScuro { get => Tema == TemaApp.Scuro; set { if (value) Tema = TemaApp.Scuro; } }
    public bool TemaComeWindows { get => Tema == TemaApp.ComeWindows; set { if (value) Tema = TemaApp.ComeWindows; } }

    /// <summary>Il colore principale: dei pulsanti principali, delle spunte e della selezione.</summary>
    [ObservableProperty]
    private ColoreApp _colore;

    /// <summary>Le finestre hanno uno sfondo con una leggera tinta del colore principale.</summary>
    [ObservableProperty]
    private bool _sfondoColorato;

    /// <summary>
    /// Ogni tanto passa un personaggio animato nella striscia in fondo all'albero. Vale quando si salva (non è una scelta
    /// di colori, quindi non c'è l'anteprima immediata).
    /// </summary>
    [ObservableProperty]
    private bool _animazioniAttive;

    /// <summary>La spiegazione sotto la casella delle animazioni.</summary>
    public string SuggerimentoAnimazioni =>
        "Ogni 3-6 minuti circa passa qualcuno nella striscia in fondo all'albero, solo a finestra attiva. "
        + "Con un doppio clic sulla striscia ne passa uno subito. Non compare se in Windows hai spento le animazioni.";

    /// <summary>I colori tra cui scegliere, ognuno con il suo pulsante di scelta (un pallino colorato e il nome).</summary>
    public IReadOnlyList<OpzioneColoreViewModel> OpzioniColore { get; }

    // Ogni scelta di aspetto nella finestra si prova subito sul programma, prima ancora di salvare.
    partial void OnTemaChanged(TemaApp value) => ProvaAspetto();

    /// <summary>Spuntando lo sfondo colorato lo si prova subito sul programma, prima ancora di salvare.</summary>
    partial void OnSfondoColoratoChanged(bool value) => ProvaAspetto();

    /// <summary>
    /// Scegliendo un colore principale si riaccendono i pulsanti di scelta giusti e lo si prova subito sul programma, prima
    /// ancora di salvare.
    /// </summary>
    partial void OnColoreChanged(ColoreApp value)
    {
        foreach (var opzione in OpzioniColore)
            opzione.AggiornaScelta();
        ProvaAspetto();
    }

    /// <summary>Mostra subito sul programma l'aspetto scelto fin qui (tema, colore principale, sfondo).</summary>
    private void ProvaAspetto() => _anteprimaAspetto?.Invoke(new AspettoApp(Tema, Colore, SfondoColorato));

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
            if (!TryLeggiGiorni(RigheAnteprimaTesto, out var righe))
                return "Le righe dell'anteprima di testo devono essere un numero intero.";

            var problemi = Costruisci(arancione, rossa, promemoria, righe).Valida();
            return problemi.Count > 0 ? problemi[0] : "";
        }
    }

    public bool PuoSalvare => Errore.Length == 0;

    /// <summary>Dopo ogni modifica ricalcola il messaggio d'errore e se si può salvare.</summary>
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
            || !TryLeggiGiorni(PromemoriaBackup, out var promemoria) || !TryLeggiGiorni(RigheAnteprimaTesto, out var righe))
            throw new InvalidOperationException("I numeri indicati non sono validi.");
        return Costruisci(arancione, rossa, promemoria, righe);
    }

    /// <summary>
    /// Crea le impostazioni nuove: una copia di quelle di partenza con i valori della finestra (testi ripuliti dagli spazi,
    /// cartella vuota = non scelta).
    /// </summary>
    private ImpostazioniApp Costruisci(int arancione, int rossa, int promemoria, int righeAnteprima)
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
        risultato.RigheAnteprimaTesto = righeAnteprima;
        risultato.RicercaXmlAttiva = RicercaXmlAttiva;
        risultato.ModoXml = ModoXml;
        risultato.Tema = Tema;
        risultato.Colore = Colore;
        risultato.SfondoColorato = SfondoColorato;
        risultato.AnimazioniAttive = AnimazioniAttive;
        return risultato;
    }

    /// <summary>Legge un numero di giorni scritto dall'utente (solo cifre); falso se non lo è.</summary>
    private static bool TryLeggiGiorni(string? testo, out int giorni) =>
        int.TryParse(testo?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out giorni);
}

/// <summary>Un colore principale nella finestra «Impostazioni»: il pallino colorato, il nome e se è quello scelto.</summary>
public class OpzioneColoreViewModel : ObservableObject
{
    private readonly ImpostazioniViewModel _impostazioni;

    /// <summary>Crea la scelta per la palette indicata, collegata alla finestra a cui appartiene.</summary>
    public OpzioneColoreViewModel(ImpostazioniViewModel impostazioni, Tavolozza tavolozza)
    {
        _impostazioni = impostazioni;
        Id = tavolozza.Id;
        Nome = tavolozza.Nome;

        var pennello = new SolidColorBrush(tavolozza.Principale ?? System.Windows.SystemColors.AccentColor);
        pennello.Freeze();
        Pennello = pennello;
    }

    public ColoreApp Id { get; }

    public string Nome { get; }

    /// <summary>Il colore del pallino.</summary>
    public Brush Pennello { get; }

    /// <summary>Vero se è il colore scelto. Scegliendolo (il pulsante si accende) diventa il colore principale; spegnerlo non fa nulla.</summary>
    public bool Scelto
    {
        get => _impostazioni.Colore == Id;
        set
        {
            if (value)
                _impostazioni.Colore = Id;
        }
    }

    /// <summary>Il colore scelto è cambiato: il pulsante si riaccende o si spegne di conseguenza.</summary>
    public void AggiornaScelta() => OnPropertyChanged(nameof(Scelto));
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
