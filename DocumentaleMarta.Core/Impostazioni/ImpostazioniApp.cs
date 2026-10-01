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

    public string? CartellaBackup { get; set; }
    public int BackupPromemoriaGiorni { get; set; } = 30;
    public DateTime? UltimoBackup { get; set; }

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

        return errori;
    }
}
