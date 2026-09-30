using DocumentaleMarta.Core.Modelli;

namespace DocumentaleMarta.Core.Servizi;

/// <summary>
/// Operazioni su aree e cartelle che tengono allineati database e cartelle fisiche:
/// se una delle due parti fallisce, l'altra viene riportata allo stato precedente.
/// </summary>
public interface IArchivioService
{
    Task<IReadOnlyList<AreaNodo>> CaricaAlberoAsync();

    /// <exception cref="ArchivioException">Nome non valido o già usato da un'altra area.</exception>
    Task<int> CreaAreaAsync(string nome);

    Task RinominaAreaAsync(int areaId, string nuovoNome);

    /// <summary>Elimina l'area con tutte le sue cartelle e documenti. I file vanno nel Cestino.</summary>
    Task EliminaAreaAsync(int areaId);

    /// <summary>Crea una cartella. Il titolo può ripetersi (es. scadenze mensili): la cartella fisica prende un suffisso.</summary>
    Task<int> CreaCartellaAsync(int areaId, string titolo);

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

    /// <summary>Elimina il documento dal database e manda il file nel Cestino.</summary>
    Task EliminaDocumentoAsync(int documentoId);

    /// <summary>
    /// I documenti di un'area, o di tutto l'archivio se <paramref name="areaId"/> è null,
    /// dal più recente al più vecchio.
    /// </summary>
    Task<IReadOnlyList<DocumentoElenco>> CaricaDocumentiAsync(int? areaId);

    /// <summary>
    /// Le cartelle non completate che hanno una scadenza, dalla più vicina (o più scaduta) alla più lontana.
    /// Quali siano "in scadenza" lo decide <see cref="AlertService"/>, non il database.
    /// </summary>
    Task<IReadOnlyList<CartellaScadenza>> CaricaScadenzeAsync();
}
