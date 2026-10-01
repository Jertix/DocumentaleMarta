using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.App.Servizi;

/// <summary>
/// Prima di allegare dei file controlla se esistono già nell'archivio (stesso contenuto, anche con un altro nome)
/// e lascia decidere all'utente. È lo stesso in tutti i punti da cui si allega: pulsante, trascinamento, nuova cartella.
/// </summary>
public class ControlloDuplicati(IArchivioService archivio, IDialogService dialog)
{
    /// <summary>
    /// I file da allegare davvero: tutti se non ci sono duplicati o l'utente li vuole lo stesso, solo quelli nuovi se
    /// sceglie di saltare i duplicati. Null se l'utente rinuncia ad allegare.
    /// </summary>
    public async Task<IReadOnlyList<string>?> FiltraAsync(IReadOnlyList<string> percorsi)
    {
        if (percorsi.Count == 0)
            return percorsi;

        var duplicati = await archivio.TrovaDuplicatiAsync(percorsi);
        if (duplicati.Count == 0)
            return percorsi;

        switch (dialog.ChiediDuplicati(duplicati, percorsi.Count))
        {
            case SceltaDuplicati.AllegaComunque:
                return percorsi;
            case SceltaDuplicati.SaltaDuplicati:
                var daSaltare = duplicati.Select(d => d.PercorsoFile).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var nuovi = percorsi.Where(p => !daSaltare.Contains(p)).ToList();
                return nuovi.Count > 0 ? nuovi : null;
            default:
                return null;
        }
    }
}
