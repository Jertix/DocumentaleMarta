using System.Text.Json.Serialization;

namespace DocumentaleMarta.Core.Impostazioni;

public class DatiAzienda
{
    public string RagioneSociale { get; set; } = "METAL PROJET DI TEDDE PAOLO E SORRENTINO LUCA SNC";
    public string CodiceFiscale { get; set; } = "01790610990";
    public string PartitaIva { get; set; } = "01790610990 (IT)";
    public string Indirizzo { get; set; } = "VIA DELLE GINESTRE, 102 R - 16137 GENOVA (GE - IT)";
    public string Descrizione { get; set; } =
        "Realtà specializzata in fabbricazione di strutture metalliche e di parti di strutture metalliche, con base a Genova.";
}

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

    /// <summary>Chiaro, scuro o come Windows.</summary>
    [JsonConverter(typeof(EnumTolleranteConverter<TemaApp>))]
    public TemaApp Tema { get; set; } = TemaApp.ComeWindows;

    /// <summary>Le scelte di aspetto, raccolte in un solo valore (per applicarle in blocco).</summary>
    [JsonIgnore]
    public AspettoApp Aspetto => new(Tema);

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
        Tema = altra.Tema;
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
