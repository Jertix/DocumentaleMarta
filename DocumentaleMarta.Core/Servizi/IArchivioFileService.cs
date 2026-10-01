namespace DocumentaleMarta.Core.Servizi;

/// <summary>File copiato nell'archivio.</summary>
public record FileArchiviato(string PercorsoRelativo, string NomeFile, long Dimensione, string Hash);

/// <summary>
/// Gestisce le cartelle e i file fisici sotto la radice dell'archivio.
/// Tutti i percorsi in ingresso e in uscita sono relativi alla radice.
/// </summary>
public interface IArchivioFileService
{
    string PercorsoRadice { get; }

    /// <summary>Percorso completo di un percorso relativo. Rifiuta i percorsi che escono dalla radice.</summary>
    string PercorsoAssoluto(string percorsoRelativo);

    /// <summary>Vero se esiste il file o la cartella indicati.</summary>
    bool Esiste(string percorsoRelativo);

    /// <summary>
    /// Crea una sottocartella dentro <paramref name="padreRelativo"/> (vuoto = radice) e restituisce il percorso
    /// relativo effettivo. Se il nome è già occupato aggiunge un suffisso "(1)", "(2)"...
    /// </summary>
    string CreaCartella(string padreRelativo, string nome);

    /// <summary>Rinomina una cartella e restituisce il nuovo percorso relativo.</summary>
    string RinominaCartella(string percorsoRelativo, string nuovoNome);

    /// <summary>Manda la cartella, con tutto il contenuto, nel Cestino di Windows. Mai la radice.</summary>
    void EliminaCartella(string percorsoRelativo);

    /// <summary>Elimina la cartella solo se è vuota, senza passare dal Cestino. Serve ad annullare una creazione fallita.</summary>
    void RimuoviCartellaVuota(string percorsoRelativo);

    /// <summary>Copia un file esterno nella cartella indicata. L'originale non viene toccato.</summary>
    FileArchiviato CopiaFile(string percorsoSorgente, string cartellaRelativa);

    /// <summary>SHA-256 del contenuto di un file qualsiasi (anche fuori dall'archivio), in esadecimale. Lo si legge anche se è aperto in un altro programma.</summary>
    string CalcolaHash(string percorsoAssoluto);

    /// <summary>Sposta un file dell'archivio in un'altra cartella e restituisce il nuovo percorso relativo.</summary>
    string SpostaFile(string percorsoRelativo, string cartellaDestinazioneRelativa);

    /// <summary>Manda il file nel Cestino di Windows.</summary>
    void EliminaFile(string percorsoRelativo);

    /// <summary>Elimina subito (senza Cestino) un file appena copiato. Serve ad annullare un'operazione fallita a metà.</summary>
    void RimuoviFileCopiato(string percorsoRelativo);
}
