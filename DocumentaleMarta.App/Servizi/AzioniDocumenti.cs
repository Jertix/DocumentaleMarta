using System.ComponentModel;
using System.IO;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.App.Servizi;

public enum EsitoEliminazione
{
    /// <summary>Il documento è stato eliminato.</summary>
    Eliminato,

    /// <summary>L'utente ha annullato, oppure l'eliminazione non è riuscita (l'utente è già stato avvisato).</summary>
    NonEliminato,

    /// <summary>Il documento non esiste più nell'archivio: l'elenco mostrato è vecchio e va riletto.</summary>
    NonPiuEsistente
}

/// <summary>
/// Le azioni sui documenti (aprire, mostrare in Esplora file, eliminare) con i relativi messaggi all'utente.
/// Sono le stesse nel form di una cartella e nelle griglie di aree e radice.
/// </summary>
public class AzioniDocumenti(
    IArchivioService archivio, IArchivioFileService files, IDialogService dialog, IShellService shell)
{
    /// <summary>Controlla che il file ci sia ancora sul disco; se manca avvisa l'utente.</summary>
    public bool FileEsiste(string percorsoRelativo)
    {
        var percorso = files.PercorsoAssoluto(percorsoRelativo);
        if (File.Exists(percorso))
            return true;

        dialog.MostraErrore(
            $"Il file non si trova più nell'archivio:\n{percorso}\n\nPotrebbe essere stato spostato o eliminato da Esplora file.");
        return false;
    }

    /// <summary>Apre il file con il programma predefinito di Windows. Da chiamare dopo <see cref="FileEsiste"/>.</summary>
    public void Apri(string percorsoRelativo, string tipo)
    {
        try
        {
            shell.ApriFile(files.PercorsoAssoluto(percorsoRelativo));
        }
        catch (Win32Exception)
        {
            dialog.MostraErrore(
                $"Windows non ha un programma per aprire questo tipo di file ({tipo}).\n\nUsa «Apri nella cartella» e scegli tu il programma.");
        }
    }

    /// <summary>Apre Esplora file con il documento evidenziato. Da chiamare dopo <see cref="FileEsiste"/>.</summary>
    public void MostraInEsplora(string percorsoRelativo) =>
        shell.MostraFileInEsplora(files.PercorsoAssoluto(percorsoRelativo));

    /// <summary>Chiede conferma ed elimina il documento (il file va nel Cestino). Gestisce da sé i messaggi d'errore.</summary>
    public async Task<EsitoEliminazione> EliminaAsync(int documentoId, string nomeFile)
    {
        if (!dialog.Conferma(
                "Elimina documento",
                $"Eliminare il documento «{nomeFile}»?\n\nIl file viene spostato nel Cestino di Windows."))
            return EsitoEliminazione.NonEliminato;

        try
        {
            await archivio.EliminaDocumentoAsync(documentoId);
            return EsitoEliminazione.Eliminato;
        }
        catch (ArchivioException ex)
        {
            dialog.MostraErrore(ex.Message);
            return EsitoEliminazione.NonPiuEsistente;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialog.MostraErrore(
                $"Non è stato possibile eliminare il documento: {ex.Message}\n\nControlla che non sia aperto in un altro programma.");
            return EsitoEliminazione.NonEliminato;
        }
    }
}
