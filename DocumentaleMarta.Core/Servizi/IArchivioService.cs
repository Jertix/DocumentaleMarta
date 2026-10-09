using DocumentaleMarta.Core.Modelli;

namespace DocumentaleMarta.Core.Servizi;

/// <summary>
/// Operazioni su aree e cartelle che tengono allineati database e cartelle fisiche:
/// se una delle due parti fallisce, l'altra viene riportata allo stato precedente.
/// </summary>
public interface IArchivioService
{
    /// <summary>Legge l'albero: le aree con le loro cartelle.</summary>
    Task<IReadOnlyList<AreaNodo>> CaricaAlberoAsync();

    /// <summary>Crea un'area con l'icona indicata (null per quella predefinita).</summary>
    /// <exception cref="ArchivioException">Nome non valido o già usato da un'altra area, oppure icona sconosciuta.</exception>
    Task<int> CreaAreaAsync(string nome, string? icona = null);

    /// <summary>Rinomina un'area (e la sua cartella sul disco). L'icona resta quella che era.</summary>
    Task RinominaAreaAsync(int areaId, string nuovoNome);

    /// <summary>Cambia l'icona di un'area (null per tornare a quella predefinita). Non tocca file e cartelle.</summary>
    /// <exception cref="ArchivioException">L'area non esiste più o l'icona non è tra quelle proposte.</exception>
    Task ImpostaIconaAreaAsync(int areaId, string? icona);

    /// <summary>Elimina l'area con tutte le sue cartelle e documenti. I file vanno nel Cestino.</summary>
    Task EliminaAreaAsync(int areaId);

    /// <summary>Crea una cartella. Il titolo può ripetersi (es. scadenze mensili): la cartella fisica prende un suffisso.</summary>
    Task<int> CreaCartellaAsync(int areaId, string titolo);

    /// <summary>Cambia il titolo di una cartella (e il nome della sua cartella sul disco).</summary>
    Task RinominaCartellaAsync(int cartellaId, string nuovoTitolo);

    /// <summary>Elimina la cartella con i suoi documenti. I file vanno nel Cestino.</summary>
    Task EliminaCartellaAsync(int cartellaId);

    /// <summary>Restituisce null se la cartella non esiste (più).</summary>
    Task<CartellaDettaglio?> CaricaCartellaAsync(int cartellaId);

    /// <summary>
    /// Crea la cartella con tutti i suoi dati e copia i file indicati al suo interno. È tutto o niente:
    /// se una copia fallisce non resta né la cartella né i file già copiati.
    /// </summary>
    Task<CartellaDettaglio> CreaCartellaConDatiAsync(int areaId, DatiCartella dati, IReadOnlyList<string> fileDaAllegare);

    /// <summary>
    /// Salva i dati della cartella. Se il titolo cambia rinomina anche la cartella fisica (e aggiorna i percorsi dei
    /// documenti); se la rinomina fallisce (file aperto altrove) non cambia nulla.
    /// </summary>
    Task<CartellaDettaglio> AggiornaCartellaAsync(int cartellaId, DatiCartella dati);

    /// <summary>Copia i file nella cartella (gli originali restano dove sono). È tutto o niente.</summary>
    Task<IReadOnlyList<DocumentoDettaglio>> AllegaDocumentiAsync(int cartellaId, IReadOnlyList<string> percorsiFile);

    /// <summary>
    /// Tra i file indicati (fuori dall'archivio) trova quelli con lo stesso contenuto di documenti già archiviati,
    /// confrontando l'impronta SHA-256. I file che non si riescono a leggere si ignorano: l'errore lo darà la copia.
    /// </summary>
    Task<IReadOnlyList<DuplicatoTrovato>> TrovaDuplicatiAsync(IReadOnlyList<string> percorsiFile);

    /// <summary>Elimina il documento dal database e manda il file nel Cestino.</summary>
    Task EliminaDocumentoAsync(int documentoId);

    /// <summary>
    /// Sposta il documento (il file vero e la riga nel database) in un'altra cartella. Se nella cartella di destinazione
    /// c'è già un file con lo stesso nome, quello spostato prende un suffisso "(1)", "(2)"... Se lo spostamento
    /// del file fallisce (file aperto altrove) non cambia nulla. Il testo già letto per la ricerca resta valido.
    /// </summary>
    Task SpostaDocumentoAsync(int documentoId, int cartellaDestinazioneId);

    /// <summary>
    /// I documenti di un'area, o di tutto l'archivio se <paramref name="areaId"/> è null,
    /// dal più recente al più vecchio.
    /// </summary>
    Task<IReadOnlyList<DocumentoElenco>> CaricaDocumentiAsync(int? areaId);

    /// <summary>
    /// I documenti delle sole cartelle archiviate (dell'area indicata, o di tutte se <paramref name="areaId"/> è null),
    /// dal più recente al più vecchio.
    /// </summary>
    Task<IReadOnlyList<DocumentoElenco>> CaricaDocumentiArchiviatiAsync(int? areaId);

    /// <summary>
    /// Mette la cartella nell'"Archivio completati" dell'albero. Non sposta niente: la cartella e i suoi file restano
    /// nell'area e nella cartella su disco. Se è già archiviata non fa nulla.
    /// </summary>
    /// <exception cref="ArchivioException">La cartella non è completata (si archiviano solo quelle completate) o non esiste più.</exception>
    Task ArchiviaCartellaAsync(int cartellaId);

    /// <summary>Toglie la cartella dall'archivio: torna a comparire nella sua area. Se non è archiviata non fa nulla.</summary>
    Task RipristinaCartellaAsync(int cartellaId);

    /// <summary>
    /// Archivia tutte le cartelle completate non ancora archiviate (di un'area, o di tutte se <paramref name="areaId"/> è null).
    /// Restituisce quante ne ha archiviate.
    /// </summary>
    Task<int> ArchiviaCompletateAsync(int? areaId);

    /// <summary>
    /// Le cartelle non completate che hanno una scadenza, dalla più vicina (o più scaduta) alla più lontana.
    /// Quali siano "in scadenza" lo decide <see cref="AlertService"/>, non il database.
    /// </summary>
    Task<IReadOnlyList<CartellaScadenza>> CaricaScadenzeAsync();
}
