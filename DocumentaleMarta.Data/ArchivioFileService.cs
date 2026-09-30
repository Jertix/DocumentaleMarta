using System.Security.Cryptography;
using DocumentaleMarta.Core.Servizi;
using Microsoft.VisualBasic.FileIO;

namespace DocumentaleMarta.Data;

/// <param name="percorsoRadice">Cartella radice dell'archivio (es. C:\Documentale).</param>
/// <param name="usaCestino">Se vero le eliminazioni vanno nel Cestino di Windows; falso solo nei test.</param>
public class ArchivioFileService(string percorsoRadice, bool usaCestino = true) : IArchivioFileService
{
    public string PercorsoRadice { get; } = Path.GetFullPath(percorsoRadice);

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

    public bool Esiste(string percorsoRelativo)
    {
        var p = PercorsoAssoluto(percorsoRelativo);
        return File.Exists(p) || Directory.Exists(p);
    }

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

    public void RimuoviCartellaVuota(string percorsoRelativo)
    {
        var percorso = PercorsoSottocartella(percorsoRelativo);
        if (Directory.Exists(percorso) && !Directory.EnumerateFileSystemEntries(percorso).Any())
            Directory.Delete(percorso);
    }

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

    private static string CalcolaHash(string percorso)
    {
        using var stream = File.OpenRead(percorso);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
