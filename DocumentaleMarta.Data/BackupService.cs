using System.IO.Compression;
using DocumentaleMarta.Core.Servizi;
using Microsoft.EntityFrameworkCore;

namespace DocumentaleMarta.Data;

/// <param name="adesso">L'ora corrente; si cambia solo nei test.</param>
/// <param name="cartellaTemporanea">Dove si fa la copia temporanea del database durante il backup; di norma quella di Windows. Si cambia solo nei test.</param>
public partial class BackupService(
    IDbContextFactory<AppDbContext> dbFactory, IArchivioFileService files, Func<DateTime>? adesso = null,
    string? cartellaTemporanea = null) : IBackupService
{
    /// <summary>Il file di istruzioni che si mette nello ZIP, accanto ai documenti.</summary>
    public const string NomeLeggimi = "_LEGGIMI-ripristino.txt";

    private const int BufferCopia = 81920;

    /// <summary>
    /// Crea il backup: controlla la cartella di destinazione, fa una copia coerente del database, scrive un file ZIP con
    /// tutto l'archivio (prima in un file temporaneo, poi lo rinomina) e ripulisce i file di lavoro.
    /// </summary>
    public async Task<EsitoBackup> CreaBackupAsync(
        string cartellaDestinazione, IProgress<int>? avanzamento = null, CancellationToken annullamento = default)
    {
        var destinazione = ValidaDestinazione(cartellaDestinazione);
        Directory.CreateDirectory(destinazione);
        var ora = (adesso ?? (() => DateTime.Now))();

        var copiaDatabase = Path.Combine(cartellaTemporanea ?? Path.GetTempPath(), $"documentale-backup-{Guid.NewGuid():N}.db");
        string? zipTemporaneo = null;
        try
        {
            await CopiaDatabaseAsync(copiaDatabase, annullamento);

            var nomeZip = NomiFileSicuri.RendiUnivoco(
                $"Documentale-backup-{ora:yyyy-MM-dd-HHmm}.zip", n => File.Exists(Path.Combine(destinazione, n)));
            var percorsoZip = Path.Combine(destinazione, nomeZip);
            zipTemporaneo = percorsoZip + ".tmp";

            var numeroFile = await Task.Run(
                () => ScriviZip(zipTemporaneo, copiaDatabase, ora, avanzamento, annullamento), annullamento);

            File.Move(zipTemporaneo, percorsoZip);
            zipTemporaneo = null;
            return new EsitoBackup(percorsoZip, numeroFile, new FileInfo(percorsoZip).Length, ora);
        }
        finally
        {
            Elimina(copiaDatabase);
            if (zipTemporaneo is not null)
                Elimina(zipTemporaneo);
        }
    }

    /// <summary>La destinazione non può stare dentro l'archivio: il backup conterrebbe se stesso, e perdere l'archivio perderebbe anche i backup.</summary>
    private string ValidaDestinazione(string cartella)
    {
        if (string.IsNullOrWhiteSpace(cartella))
            throw new ArchivioException("Scegli la cartella in cui salvare i backup.");

        string completo;
        try { completo = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cartella)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArchivioException($"Il percorso della cartella dei backup non è valido: {cartella}");
        }

        var radice = Path.TrimEndingDirectorySeparator(files.PercorsoRadice);
        if (completo.Equals(radice, StringComparison.OrdinalIgnoreCase)
            || completo.StartsWith(radice + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArchivioException(
                $"La cartella dei backup non può stare dentro l'archivio ({files.PercorsoRadice}). Scegline una fuori, meglio se su un altro disco.");

        return completo;
    }

    /// <summary>
    /// Copia coerente del database fatta da SQLite stesso: il file originale è in uso (e può avere modifiche
    /// non ancora scritte), quindi non basta copiarlo.
    /// </summary>
    private async Task CopiaDatabaseAsync(string destinazione, CancellationToken annullamento)
    {
        await using var db = await dbFactory.CreateDbContextAsync(annullamento);
        await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", [destinazione], annullamento);
    }

    /// <summary>
    /// Scrive lo ZIP: le cartelle vuote, tutti i file dell'archivio (tranne il database vivo), la copia coerente del
    /// database e il file di istruzioni; poi lo riapre per verificarlo. Restituisce quanti file ha messo.
    /// </summary>
    private int ScriviZip(string percorsoZip, string copiaDatabase, DateTime ora, IProgress<int>? avanzamento, CancellationToken annullamento)
    {
        var radice = Path.TrimEndingDirectorySeparator(files.PercorsoRadice);
        var cartellaDati = Path.Combine(radice, ArchivioDatabase.CartellaDati);
        var numeroFile = 0;
        var numeroVoci = 0;

        using (var flusso = new FileStream(percorsoZip, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        using (var zip = new ZipArchive(flusso, ZipArchiveMode.Create))
        {
            foreach (var cartella in Directory.EnumerateDirectories(radice, "*", SearchOption.AllDirectories))
            {
                annullamento.ThrowIfCancellationRequested();
                // Le cartelle vuote (un'area senza cartelle, una cartella senza documenti) esistono anche nell'albero.
                if (!Directory.EnumerateFileSystemEntries(cartella).Any() && !EDentro(cartella, cartellaDati))
                {
                    zip.CreateEntry(NomeVoce(radice, cartella) + "/");
                    numeroVoci++;
                }
            }

            foreach (var file in Directory.EnumerateFiles(radice, "*", SearchOption.AllDirectories))
            {
                annullamento.ThrowIfCancellationRequested();
                if (EDatabaseOriginale(file, cartellaDati))
                    continue; // al suo posto va la copia coerente, aggiunta sotto

                if (AggiungiFile(zip, file, NomeVoce(radice, file)))
                {
                    numeroFile++;
                    numeroVoci++;
                    avanzamento?.Report(numeroFile);
                }
            }

            AggiungiFile(zip, copiaDatabase, ArchivioDatabase.CartellaDati + "/" + ArchivioDatabase.NomeFileDatabase);
            numeroFile++;
            numeroVoci++;
            avanzamento?.Report(numeroFile);

            var leggimi = zip.CreateEntry(NomeLeggimi);
            using (var scrittore = new StreamWriter(leggimi.Open(), new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true)))
                scrittore.Write(TestoLeggimi(ora));
            numeroVoci++;
        }

        // Una verifica veloce: il file si riapre e ha tutte le voci che ci aspettiamo.
        using (var verifica = ZipFile.OpenRead(percorsoZip))
        {
            if (verifica.Entries.Count != numeroVoci)
                throw new IOException("Il file di backup risulta incompleto: riprova.");
        }

        return numeroFile;
    }

    /// <summary>Aggiunge un file allo ZIP leggendolo anche se è aperto in un altro programma. False se nel frattempo è sparito.</summary>
    private static bool AggiungiFile(ZipArchive zip, string percorso, string nomeVoce)
    {
        try
        {
            using var origine = new FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, BufferCopia);
            var voce = zip.CreateEntry(nomeVoce, CompressionLevel.Fastest);
            voce.LastWriteTime = new DateTimeOffset(File.GetLastWriteTime(percorso));
            using var destinazione = voce.Open();
            origine.CopyTo(destinazione, BufferCopia);
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Il nome che un file ha dentro lo ZIP: il percorso relativo all'archivio con la barra normale.</summary>
    private static string NomeVoce(string radice, string percorso) =>
        Path.GetRelativePath(radice, percorso).Replace(Path.DirectorySeparatorChar, '/');

    /// <summary>Vero se il percorso è la cartella indicata o sta al suo interno.</summary>
    private static bool EDentro(string percorso, string cartella) =>
        percorso.Equals(cartella, StringComparison.OrdinalIgnoreCase)
        || percorso.StartsWith(cartella + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>Il database vivo e i suoi file accessori (journal, WAL): non si copiano così come sono.</summary>
    private static bool EDatabaseOriginale(string file, string cartellaDati) =>
        Path.GetDirectoryName(file)!.Equals(cartellaDati, StringComparison.OrdinalIgnoreCase)
        && Path.GetFileName(file).StartsWith(ArchivioDatabase.NomeFileDatabase, StringComparison.OrdinalIgnoreCase);

    /// <summary>Cancella un file di lavoro; se non ci riesce non importa.</summary>
    private static void Elimina(string percorso)
    {
        try { File.Delete(percorso); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Il testo del file di istruzioni che si mette nello ZIP: cosa contiene e come ripristinarlo.</summary>
    private static string TestoLeggimi(DateTime ora) => string.Join("\r\n",
        "Backup di Documentale",
        $"Creato il {ora:dd/MM/yyyy} alle {ora:HH:mm}.",
        "",
        "Questo file ZIP contiene tutto l'archivio: i documenti, con le loro cartelle, e il database (cartella _dati).",
        "",
        "Il modo più semplice per ripristinarlo: apri Documentale, premi «Ripristina…» nella barra in alto e scegli questo file.",
        "Il programma lo estrae in una cartella nuova, senza toccare l'archivio attuale, e ti chiede se vuoi usarlo.",
        "",
        "Oppure, a mano:",
        "1. Chiudi Documentale.",
        "2. Estrai tutto il contenuto di questo ZIP in una cartella vuota, ad esempio C:/Documentale-ripristino",
        "3. Apri il file delle impostazioni (in Documentale: pulsante «i», voce «File delle impostazioni»).",
        "4. Alla riga \"PercorsoRadice\" scrivi la cartella dove hai estratto i file, usando la barra normale (/) al posto di quella rovesciata:",
        "   \"PercorsoRadice\": \"C:/Documentale-ripristino\"",
        "5. Salva il file e riapri Documentale.",
        "");
}
