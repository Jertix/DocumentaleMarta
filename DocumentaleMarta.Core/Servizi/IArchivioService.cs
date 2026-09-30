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
}
