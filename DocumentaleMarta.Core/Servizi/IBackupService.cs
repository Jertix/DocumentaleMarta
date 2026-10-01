namespace DocumentaleMarta.Core.Servizi;

/// <summary>Il risultato di un backup riuscito.</summary>
/// <param name="PercorsoZip">Il file ZIP creato.</param>
/// <param name="NumeroFile">Quanti file dell'archivio contiene (documenti e database).</param>
/// <param name="Dimensione">Dimensione del file ZIP, in byte.</param>
/// <param name="Data">Quando è stato fatto.</param>
public record EsitoBackup(string PercorsoZip, int NumeroFile, long Dimensione, DateTime Data);

/// <summary>Copia di sicurezza di tutto l'archivio (documenti e database) in un unico file ZIP.</summary>
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
}
