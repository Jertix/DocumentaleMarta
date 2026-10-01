namespace DocumentaleMarta.Core.Modelli;

/// <summary>Un documento già nell'archivio, per dire dove si trova una copia identica a un file che si sta per allegare.</summary>
public record DocumentoGiaArchiviato(int DocumentoId, string NomeFile, int CartellaId, string TitoloCartella, string NomeArea);

/// <summary>Un file che si sta per allegare e che ha lo stesso contenuto di uno o più documenti già archiviati.</summary>
public record DuplicatoTrovato(string PercorsoFile, string NomeFile, IReadOnlyList<DocumentoGiaArchiviato> GiaArchiviati);
