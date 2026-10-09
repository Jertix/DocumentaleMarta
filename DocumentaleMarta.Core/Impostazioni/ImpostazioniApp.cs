using System.Text.Json.Serialization;

namespace DocumentaleMarta.Core.Impostazioni;

/// <summary>
/// I dati della ditta mostrati in fondo alla finestra e nella finestra «Informazioni» (ragione sociale, codice fiscale,
/// partita IVA, indirizzo, descrizione dell'attività).
/// </summary>
public class DatiAzienda
{
    public string RagioneSociale { get; set; } = "METAL PROJET DI TEDDE PAOLO E SORRENTINO LUCA SNC";
    public string CodiceFiscale { get; set; } = "01790610990";
    public string PartitaIva { get; set; } = "01790610990 (IT)";
    public string Indirizzo { get; set; } = "VIA DELLE GINESTRE, 102 R - 16137 GENOVA (GE - IT)";
    public string Descrizione { get; set; } =
        "Realtà specializzata in fabbricazione di strutture metalliche e di parti di strutture metalliche, con base a Genova.";
}

/// <summary>Che cosa si cerca dentro un file XML (quando la ricerca nei file XML è attiva).</summary>
public enum ModoRicercaXml
{
    /// <summary>Solo il testo: i valori degli elementi, i blocchi CDATA e i valori degli attributi, non i nomi dei tag.</summary>
    SoloTesto,

    /// <summary>Tutto il file com'è scritto, compresi i nomi dei tag e degli attributi.</summary>
    TuttoIlFile
}

/// <summary>
/// Tutte le impostazioni del programma: dati della ditta, dove sta l'archivio, soglie e riepilogo degli avvisi di scadenza,
/// cartella e promemoria del backup, tema. Si salvano nel file impostazioni.json.
/// </summary>
public class ImpostazioniApp
{
    public DatiAzienda Azienda { get; set; } = new();

    public string PercorsoRadice { get; set; } = @"C:\Documentale";
    public string NomeRadice { get; set; } = "Tutti i documenti";

    public bool AvvisiAttivi { get; set; } = true;
    public bool RiepilogoAvvio { get; set; } = true;

    /// <summary>Arancione se la scadenza è entro questi giorni.</summary>
    public int SogliaArancioneGiorni { get; set; } = 30;

    /// <summary>Rosso se la scadenza è entro questi giorni (le scadenze già passate sono sempre rosse).</summary>
    public int SogliaRossaGiorni { get; set; } = 7;

    /// <summary>La cartella dei backup che l'utente ha scelto; vuota finché non ne sceglie una (vedi <see cref="CartellaBackupInUso"/>).</summary>
    public string? CartellaBackup { get; set; }

    /// <summary>
    /// Dove si salvano i backup finché l'utente non sceglie un'altra cartella: così il primo backup parte senza chiedere niente.
    /// Si crea da sola al primo backup.
    /// </summary>
    public const string CartellaBackupPredefinita = @"C:\Backup\DocumentaleMarta";

    public int BackupPromemoriaGiorni { get; set; } = 30;
    public DateTime? UltimoBackup { get; set; }

    /// <summary>Quante righe mostra l'anteprima di un file di testo (.txt, .csv, .xml): le prime, poi si apre il file con il suo programma.</summary>
    public int RigheAnteprimaTesto { get; set; } = RigheAnteprimaPredefinite;

    /// <summary>
    /// Cercare anche dentro i file XML. Spenta di base: un XML può essere molto grande (per esempio un file di log) e leggerlo
    /// pesa sull'archivio; spenta, di un XML si cerca solo il nome.
    /// </summary>
    public bool RicercaXmlAttiva { get; set; }

    /// <summary>Che cosa si cerca dentro i file XML, quando la ricerca nei file XML è attiva.</summary>
    [JsonConverter(typeof(EnumTolleranteConverter<ModoRicercaXml>))]
    public ModoRicercaXml ModoXml { get; set; } = ModoRicercaXml.SoloTesto;

    /// <summary>Come si leggono ora gli XML per la ricerca; vuoto (null) se la ricerca nei file XML è spenta.</summary>
    [JsonIgnore]
    public ModoRicercaXml? ModoXmlInUso => RicercaXmlAttiva ? ModoXml : null;

    /// <summary>Le righe dell'anteprima di testo finché l'utente non sceglie altro.</summary>
    public const int RigheAnteprimaPredefinite = 100;

    /// <summary>Il minimo di righe che si può chiedere all'anteprima di testo.</summary>
    public const int RigheAnteprimaMinime = 10;

    /// <summary>Il massimo di righe che si può chiedere all'anteprima di testo (oltre, il pannello diventerebbe lento da scorrere).</summary>
    public const int RigheAnteprimaMassime = 1000;

    /// <summary>Chiaro, scuro o come Windows.</summary>
    [JsonConverter(typeof(EnumTolleranteConverter<TemaApp>))]
    public TemaApp Tema { get; set; } = TemaApp.ComeWindows;

    /// <summary>Il colore dei pulsanti principali, delle spunte e della selezione.</summary>
    [JsonConverter(typeof(EnumTolleranteConverter<ColoreApp>))]
    public ColoreApp Colore { get; set; } = ColoreApp.Acciaio;

    /// <summary>Le finestre hanno uno sfondo con una leggera tinta del colore principale invece del grigio neutro.</summary>
    public bool SfondoColorato { get; set; }

    /// <summary>
    /// Ogni tanto passa un personaggio animato (un cagnolino, un omino…) nella striscia in fondo all'albero. Acceso di base;
    /// è una scelta di comportamento, non di colori, e vale appena si salvano le impostazioni.
    /// </summary>
    public bool AnimazioniAttive { get; set; } = true;

    /// <summary>Le scelte di aspetto, raccolte in un solo valore (per applicarle in blocco).</summary>
    [JsonIgnore]
    public AspettoApp Aspetto => new(Tema, Colore, SfondoColorato);

    /// <summary>Una copia indipendente: modificarla non tocca l'originale.</summary>
    public ImpostazioniApp Clona()
    {
        var copia = (ImpostazioniApp)MemberwiseClone();
        copia.Azienda = new DatiAzienda
        {
            RagioneSociale = Azienda.RagioneSociale,
            CodiceFiscale = Azienda.CodiceFiscale,
            PartitaIva = Azienda.PartitaIva,
            Indirizzo = Azienda.Indirizzo,
            Descrizione = Azienda.Descrizione
        };
        return copia;
    }

    /// <summary>
    /// Copia tutti i valori di <paramref name="altra"/> dentro questo oggetto, che resta lo stesso: chi lo tiene in mano
    /// (il programma in esecuzione) vede subito i nuovi valori.
    /// </summary>
    public void CopiaDa(ImpostazioniApp altra)
    {
        Azienda = altra.Clona().Azienda;
        PercorsoRadice = altra.PercorsoRadice;
        NomeRadice = altra.NomeRadice;
        AvvisiAttivi = altra.AvvisiAttivi;
        RiepilogoAvvio = altra.RiepilogoAvvio;
        SogliaArancioneGiorni = altra.SogliaArancioneGiorni;
        SogliaRossaGiorni = altra.SogliaRossaGiorni;
        CartellaBackup = altra.CartellaBackup;
        BackupPromemoriaGiorni = altra.BackupPromemoriaGiorni;
        UltimoBackup = altra.UltimoBackup;
        RigheAnteprimaTesto = altra.RigheAnteprimaTesto;
        RicercaXmlAttiva = altra.RicercaXmlAttiva;
        ModoXml = altra.ModoXml;
        Tema = altra.Tema;
        Colore = altra.Colore;
        SfondoColorato = altra.SfondoColorato;
        AnimazioniAttive = altra.AnimazioniAttive;
    }

    /// <summary>Restituisce i problemi trovati, vuoto se le impostazioni sono valide.</summary>
    public IReadOnlyList<string> Valida()
    {
        var errori = new List<string>();

        if (string.IsNullOrWhiteSpace(PercorsoRadice))
            errori.Add("Il percorso della radice dell'archivio è obbligatorio.");
        if (string.IsNullOrWhiteSpace(NomeRadice))
            errori.Add("Il nome della radice dell'albero è obbligatorio.");
        if (SogliaRossaGiorni < 0)
            errori.Add("La soglia rossa non può essere negativa.");
        if (SogliaArancioneGiorni <= SogliaRossaGiorni)
            errori.Add("La soglia arancione deve essere maggiore della soglia rossa.");
        if (BackupPromemoriaGiorni < 1)
            errori.Add("Il promemoria del backup deve essere di almeno 1 giorno.");
        if (ErroreCartellaBackup() is { } erroreBackup)
            errori.Add(erroreBackup);
        if (RigheAnteprimaTesto is < RigheAnteprimaMinime or > RigheAnteprimaMassime)
            errori.Add($"Le righe dell'anteprima di testo devono essere tra {RigheAnteprimaMinime} e {RigheAnteprimaMassime}.");

        return errori;
    }

    /// <summary>
    /// La cartella in cui fare il backup adesso: quella scelta dall'utente oppure, se non ne ha scelta nessuna, quella predefinita.
    /// È vuota (null) solo se non c'è una scelta e la cartella predefinita starebbe dentro l'archivio (per esempio se l'archivio
    /// è stato messo proprio in «C:\Backup»): allora il programma deve chiedere all'utente dove salvare.
    /// </summary>
    [JsonIgnore]
    public string? CartellaBackupInUso
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CartellaBackup))
                return CartellaBackup.Trim();

            return ErroreCartellaBackup(CartellaBackupPredefinita) is null ? CartellaBackupPredefinita : null;
        }
    }

    /// <summary>La cartella dei backup scelta (se indicata) deve essere un percorso valido e stare fuori dall'archivio.</summary>
    private string? ErroreCartellaBackup() => ErroreCartellaBackup(CartellaBackup);

    /// <summary>
    /// Controlla una cartella per i backup: deve essere un percorso valido e stare fuori dall'archivio (un backup dentro
    /// l'archivio finirebbe dentro il backup successivo). Restituisce il motivo se non va bene, null se va bene o se non c'è nulla da controllare.
    /// </summary>
    private string? ErroreCartellaBackup(string? cartella)
    {
        if (string.IsNullOrWhiteSpace(cartella) || string.IsNullOrWhiteSpace(PercorsoRadice))
            return null;

        string backup, radice;
        try
        {
            backup = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cartella));
            radice = Path.TrimEndingDirectorySeparator(Path.GetFullPath(PercorsoRadice));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "La cartella dei backup non è un percorso valido.";
        }

        var dentro = backup.Equals(radice, StringComparison.OrdinalIgnoreCase)
                     || backup.StartsWith(radice + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        return dentro ? "La cartella dei backup non può stare dentro l'archivio: scegline una fuori." : null;
    }
}
