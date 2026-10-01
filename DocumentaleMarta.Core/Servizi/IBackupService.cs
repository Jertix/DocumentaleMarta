namespace DocumentaleMarta.Core.Servizi;

/// <summary>Il risultato di un backup riuscito.</summary>
/// <param name="PercorsoZip">Il file ZIP creato.</param>
/// <param name="NumeroFile">Quanti file dell'archivio contiene (documenti e database).</param>
/// <param name="Dimensione">Dimensione del file ZIP, in byte.</param>
/// <param name="Data">Quando è stato fatto.</param>
public record EsitoBackup(string PercorsoZip, int NumeroFile, long Dimensione, DateTime Data);

/// <summary>Cosa contiene un file di backup, letto senza ripristinarlo.</summary>
/// <param name="Data">Quando è stato fatto il backup (la data del database copiato), se si riesce a saperla.</param>
/// <param name="NumeroFile">Quanti file dell'archivio contiene (documenti e database).</param>
/// <param name="DimensioneDecompressa">Quanto spazio occupano i file una volta estratti, in byte.</param>
public record InfoBackup(string PercorsoZip, DateTime? Data, int NumeroFile, long DimensioneDecompressa);

/// <summary>Il risultato di un ripristino riuscito.</summary>
/// <param name="Cartella">La nuova radice dell'archivio ripristinato.</param>
/// <param name="NumeroFile">Quanti file sono stati estratti.</param>
/// <param name="DocumentiMancanti">Quanti documenti elencati nel database non hanno il loro file nel backup (zero se tutto è in ordine).</param>
public record EsitoRipristino(string Cartella, int NumeroFile, int DocumentiMancanti);

/// <summary>Copia di sicurezza di tutto l'archivio (documenti e database) in un unico file ZIP, e ripristino da quel file.</summary>
public interface IBackupService
{
    /// <summary>
    /// Crea in <paramref name="cartellaDestinazione"/> un file ZIP con tutta la radice dell'archivio. Il database è una
    /// copia coerente fatta mentre il programma è in uso. Se qualcosa va storto non resta nessun file a metà.
    /// </summary>
    /// <param name="avanzamento">Riceve il numero di file copiati finora.</param>
    /// <exception cref="ArchivioException">La cartella di destinazione non è valida (per esempio sta dentro l'archivio).</exception>
    Task<EsitoBackup> CreaBackupAsync(
        string cartellaDestinazione, IProgress<int>? avanzamento = null, CancellationToken annullamento = default);

    /// <summary>Controlla che il file sia un backup di Documentale e dice cosa contiene, senza estrarre niente.</summary>
    /// <exception cref="ArchivioException">Il file non esiste, non è uno ZIP o non è un backup di Documentale.</exception>
    Task<InfoBackup> LeggiBackupAsync(string percorsoZip, CancellationToken annullamento = default);

    /// <summary>
    /// Estrae il backup in una cartella nuova (o vuota), aggiorna il database all'ultima versione e controlla che sia integro.
    /// Non tocca mai l'archivio attuale. Se qualcosa va storto la cartella di destinazione torna com'era (non resta niente a metà).
    /// </summary>
    /// <param name="avanzamento">Riceve il numero di file estratti finora.</param>
    /// <exception cref="ArchivioException">
    /// Il backup non è valido o è danneggiato, oppure la destinazione non va bene (non è vuota, o sta dentro l'archivio attuale o lo contiene).
    /// </exception>
    Task<EsitoRipristino> RipristinaAsync(
        string percorsoZip, string cartellaDestinazione, IProgress<int>? avanzamento = null, CancellationToken annullamento = default);
}
