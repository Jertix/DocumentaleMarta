using System.Security.Cryptography;
using DocumentaleMarta.Core.Servizi;
using Microsoft.VisualBasic.FileIO;

namespace DocumentaleMarta.Data;

/// <param name="percorsoRadice">Cartella radice dell'archivio (es. C:\Documentale).</param>
/// <param name="usaCestino">Se vero le eliminazioni vanno nel Cestino di Windows; falso solo nei test.</param>
public class ArchivioFileService(string percorsoRadice, bool usaCestino = true) : IArchivioFileService
{
    public string PercorsoRadice { get; } = Path.GetFullPath(percorsoRadice);

    /// <summary>
    /// Trasforma un percorso relativo all'archivio nel percorso completo sul disco, rifiutando quelli che uscirebbero dalla
    /// cartella radice.
    /// </summary>
    public string PercorsoAssoluto(string percorsoRelativo)
    {
        var radice = Path.TrimEndingDirectorySeparator(PercorsoRadice);
        var completo = Path.GetFullPath(Path.Combine(radice, percorsoRelativo));

        var dentroLaRadice = completo.Equals(radice, StringComparison.OrdinalIgnoreCase)
                             || completo.StartsWith(radice + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        if (!dentroLaRadice)
            throw new ArgumentException($"Il percorso esce dalla radice dell'archivio: {percorsoRelativo}", nameof(percorsoRelativo));

        return completo;
    }

    /// <summary>Vero se nell'archivio esiste il file o la cartella indicati.</summary>
    public bool Esiste(string percorsoRelativo)
    {
        var p = PercorsoAssoluto(percorsoRelativo);
        return File.Exists(p) || Directory.Exists(p);
    }

    /// <summary>
    /// Crea una cartella dentro un'altra (con un nome ripulito dai caratteri non ammessi da Windows e reso unico se esiste
    /// già) e restituisce il suo percorso relativo.
    /// </summary>
    public string CreaCartella(string padreRelativo, string nome)
    {
        var pulito = NomiFileSicuri.PulisciNomeCartella(nome);
        var padre = PercorsoAssoluto(padreRelativo);
        Directory.CreateDirectory(padre);

        var nomeFinale = NomiFileSicuri.RendiUnivoco(pulito, n => Path.Exists(Path.Combine(padre, n)));
        Directory.CreateDirectory(Path.Combine(padre, nomeFinale));
        return Path.Combine(padreRelativo, nomeFinale);
    }

    /// <summary>Come <see cref="PercorsoAssoluto"/> ma rifiuta la radice: rinominarla o eliminarla sarebbe un disastro.</summary>
    private string PercorsoSottocartella(string percorsoRelativo)
    {
        var percorso = PercorsoAssoluto(percorsoRelativo);
        if (percorso.Equals(Path.TrimEndingDirectorySeparator(PercorsoRadice), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("L'operazione non è consentita sulla radice dell'archivio.", nameof(percorsoRelativo));
        return percorso;
    }

    /// <summary>
    /// Rinomina una cartella sul disco (nome ripulito e reso unico) e restituisce il nuovo percorso relativo; cambiare solo
    /// le maiuscole non è un conflitto.
    /// </summary>
    public string RinominaCartella(string percorsoRelativo, string nuovoNome)
    {
        var origine = PercorsoSottocartella(percorsoRelativo);
        if (!Directory.Exists(origine))
            throw new DirectoryNotFoundException($"Cartella non trovata: {percorsoRelativo}");

        var padreRelativo = Path.GetDirectoryName(percorsoRelativo) ?? "";
        var padre = PercorsoAssoluto(padreRelativo);
        var pulito = NomiFileSicuri.PulisciNomeCartella(nuovoNome);

        // Stesso nome (anche solo di maiuscole/minuscole) diverso da se stessa: non è un conflitto.
        var attuale = Path.GetFileName(percorsoRelativo);
        if (pulito.Equals(attuale, StringComparison.OrdinalIgnoreCase))
        {
            if (pulito != attuale)
                Directory.Move(origine, Path.Combine(padre, pulito));
            return Path.Combine(padreRelativo, pulito);
        }

        var nomeFinale = NomiFileSicuri.RendiUnivoco(pulito, n => Path.Exists(Path.Combine(padre, n)));
        Directory.Move(origine, Path.Combine(padre, nomeFinale));
        return Path.Combine(padreRelativo, nomeFinale);
    }

    /// <summary>
    /// Elimina una cartella con tutto il suo contenuto, mandandola nel Cestino di Windows (mai cancellazione definitiva).
    /// </summary>
    public void EliminaCartella(string percorsoRelativo)
    {
        var percorso = PercorsoSottocartella(percorsoRelativo);
        if (!Directory.Exists(percorso))
            return;

        if (usaCestino)
            FileSystem.DeleteDirectory(percorso, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else
            Directory.Delete(percorso, recursive: true);
    }

    /// <summary>Toglie una cartella solo se è vuota (serve a ripulire dopo un'operazione fallita).</summary>
    public void RimuoviCartellaVuota(string percorsoRelativo)
    {
        var percorso = PercorsoSottocartella(percorsoRelativo);
        if (Directory.Exists(percorso) && !Directory.EnumerateFileSystemEntries(percorso).Any())
            Directory.Delete(percorso);
    }

    /// <summary>
    /// Copia un file dentro una cartella dell'archivio (l'originale non si tocca) con un nome unico, senza lasciare file a
    /// metà se la copia fallisce; calcola anche l'impronta del contenuto.
    /// </summary>
    public FileArchiviato CopiaFile(string percorsoSorgente, string cartellaRelativa)
    {
        if (!File.Exists(percorsoSorgente))
            throw new FileNotFoundException("File da allegare non trovato.", percorsoSorgente);

        var cartella = PercorsoAssoluto(cartellaRelativa);
        Directory.CreateDirectory(cartella);

        var nome = NomiFileSicuri.PulisciNomeFile(Path.GetFileName(percorsoSorgente));
        var nomeFinale = NomiFileSicuri.RendiUnivoco(nome, n => File.Exists(Path.Combine(cartella, n)));
        var destinazione = Path.Combine(cartella, nomeFinale);

        try
        {
            // FileMode.CreateNew: se un altro processo ha creato lo stesso nome nel frattempo, fallisce invece di sovrascrivere.
            using (var origine = new FileStream(percorsoSorgente, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var copia = new FileStream(destinazione, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                origine.CopyTo(copia);
        }
        catch
        {
            // Non lasciare a metà un file troncato.
            if (File.Exists(destinazione))
                File.Delete(destinazione);
            throw;
        }

        var info = new FileInfo(destinazione);
        return new FileArchiviato(Path.Combine(cartellaRelativa, nomeFinale), nomeFinale, info.Length, CalcolaHash(destinazione));
    }

    /// <summary>
    /// Sposta un file in un'altra cartella dell'archivio (con un nome unico) e restituisce il suo nuovo percorso relativo.
    /// </summary>
    public string SpostaFile(string percorsoRelativo, string cartellaDestinazioneRelativa)
    {
        var origine = PercorsoAssoluto(percorsoRelativo);
        if (!File.Exists(origine))
            throw new FileNotFoundException("File non trovato nell'archivio.", percorsoRelativo);

        var cartella = PercorsoAssoluto(cartellaDestinazioneRelativa);
        Directory.CreateDirectory(cartella);

        var nomeFinale = NomiFileSicuri.RendiUnivoco(Path.GetFileName(origine), n => File.Exists(Path.Combine(cartella, n)));
        File.Move(origine, Path.Combine(cartella, nomeFinale));
        return Path.Combine(cartellaDestinazioneRelativa, nomeFinale);
    }

    /// <summary>Elimina un file mandandolo nel Cestino di Windows.</summary>
    public void EliminaFile(string percorsoRelativo)
    {
        var percorso = PercorsoAssoluto(percorsoRelativo);
        if (!File.Exists(percorso))
            return;

        if (usaCestino)
            FileSystem.DeleteFile(percorso, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        else
            File.Delete(percorso);
    }

    /// <summary>
    /// Cancella davvero un file appena copiato (serve a disfare una copia quando l'operazione poi fallisce).
    /// </summary>
    public void RimuoviFileCopiato(string percorsoRelativo)
    {
        var percorso = PercorsoAssoluto(percorsoRelativo);
        if (File.Exists(percorso))
            File.Delete(percorso);
    }

    /// <summary>
    /// Calcola l'impronta (SHA-256) del contenuto di un file: due file uguali hanno la stessa impronta, anche con nomi
    /// diversi.
    /// </summary>
    public string CalcolaHash(string percorsoAssoluto)
    {
        // Come nella copia: un file aperto in Word o nel lettore PDF si legge lo stesso.
        using var stream = new FileStream(percorsoAssoluto, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
