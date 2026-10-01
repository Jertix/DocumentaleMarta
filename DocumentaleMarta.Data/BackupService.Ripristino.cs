using System.IO.Compression;
using DocumentaleMarta.Core.Servizi;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DocumentaleMarta.Data;

public partial class BackupService
{
    private const string VoceDatabase = ArchivioDatabase.CartellaDati + "/" + ArchivioDatabase.NomeFileDatabase;

    // ---------- Leggere un backup ----------

    public Task<InfoBackup> LeggiBackupAsync(string percorsoZip, CancellationToken annullamento = default) =>
        Task.Run(() => LeggiBackup(percorsoZip), annullamento);

    private static InfoBackup LeggiBackup(string percorsoZip)
    {
        using var archivio = ApriBackup(percorsoZip);

        var database = archivio.Entries.FirstOrDefault(e => StessaVoce(e.FullName, VoceDatabase))
                       ?? throw new ArchivioException(
                           "Questo file non è un backup di Documentale: dentro non c'è il database (_dati/documentale.db).");

        var voci = archivio.Entries.Where(e => !EDirectory(e) && !StessaVoce(e.FullName, NomeLeggimi)).ToList();
        var data = database.LastWriteTime.Year >= 1990 ? database.LastWriteTime.LocalDateTime : (DateTime?)null;
        return new InfoBackup(percorsoZip, data, voci.Count, voci.Sum(e => e.Length));
    }

    private static ZipArchive ApriBackup(string percorsoZip)
    {
        try
        {
            return ZipFile.OpenRead(percorsoZip);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ArchivioException($"Il file «{percorsoZip}» non esiste.");
        }
        catch (InvalidDataException)
        {
            throw new ArchivioException("Questo file non è un backup valido: non è un archivio ZIP, oppure è danneggiato.");
        }
    }

    // ---------- Ripristinare ----------

    public async Task<EsitoRipristino> RipristinaAsync(
        string percorsoZip, string cartellaDestinazione, IProgress<int>? avanzamento = null, CancellationToken annullamento = default)
    {
        var destinazione = ValidaDestinazioneRipristino(cartellaDestinazione);

        // Prima si controlla il backup, poi si tocca il disco.
        await LeggiBackupAsync(percorsoZip, annullamento);

        var cartellaNuova = !Directory.Exists(destinazione);
        Directory.CreateDirectory(destinazione);
        try
        {
            var numeroFile = await Task.Run(() => Estrai(percorsoZip, destinazione, avanzamento, annullamento), annullamento);
            var mancanti = await VerificaAsync(destinazione, annullamento);
            return new EsitoRipristino(destinazione, numeroFile, mancanti);
        }
        catch
        {
            // Non deve restare niente a metà: la destinazione torna com'era (nuova = sparisce; vuota = torna vuota).
            PulisciDestinazione(destinazione, cartellaNuova);
            throw;
        }
    }

    /// <summary>
    /// Il ripristino non tocca mai l'archivio attuale: la destinazione non può stare dentro di lui né contenerlo,
    /// e deve essere nuova o vuota (niente file mescolati).
    /// </summary>
    private string ValidaDestinazioneRipristino(string cartella)
    {
        if (string.IsNullOrWhiteSpace(cartella))
            throw new ArchivioException("Scegli la cartella in cui ripristinare l'archivio.");

        string completo;
        try { completo = Path.TrimEndingDirectorySeparator(Path.GetFullPath(cartella)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArchivioException($"Il percorso della cartella di ripristino non è valido: {cartella}");
        }

        var radice = Path.TrimEndingDirectorySeparator(files.PercorsoRadice);
        if (EDentro(completo, radice))
            throw new ArchivioException(
                $"La cartella di ripristino non può stare dentro l'archivio attuale ({files.PercorsoRadice}): scegline un'altra.");
        if (EDentro(radice, completo))
            throw new ArchivioException(
                $"La cartella di ripristino non può contenere l'archivio attuale ({files.PercorsoRadice}): scegline un'altra.");

        if (File.Exists(completo))
            throw new ArchivioException($"«{completo}» è un file, non una cartella.");
        if (Directory.Exists(completo) && Directory.EnumerateFileSystemEntries(completo).Any())
            throw new ArchivioException(
                $"La cartella «{completo}» non è vuota. Scegline una vuota o nuova, così i file del backup non si mescolano con altri.");

        return completo;
    }

    private static int Estrai(string percorsoZip, string destinazione, IProgress<int>? avanzamento, CancellationToken annullamento)
    {
        try
        {
            using var archivio = ApriBackup(percorsoZip);
            var radice = Path.TrimEndingDirectorySeparator(destinazione);
            var numeroFile = 0;

            foreach (var voce in archivio.Entries)
            {
                annullamento.ThrowIfCancellationRequested();
                if (StessaVoce(voce.FullName, NomeLeggimi))
                    continue; // le istruzioni non fanno parte dell'archivio

                var percorso = PercorsoSicuro(radice, voce.FullName);
                if (EDirectory(voce))
                {
                    Directory.CreateDirectory(percorso);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);
                voce.ExtractToFile(percorso, overwrite: false); // mantiene anche la data del file
                numeroFile++;
                avanzamento?.Report(numeroFile);
            }
            return numeroFile;
        }
        catch (InvalidDataException ex)
        {
            throw new ArchivioException($"Il backup è danneggiato o incompleto: {ex.Message}");
        }
    }

    /// <summary>
    /// Il percorso dove estrarre una voce dello ZIP, solo se sta dentro la destinazione. Un backup costruito apposta
    /// potrebbe contenere "../../qualcosa" per scrivere fuori dalla cartella scelta.
    /// </summary>
    private static string PercorsoSicuro(string radice, string nomeVoce)
    {
        var relativo = nomeVoce.Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(relativo) || relativo.Contains(':'))
            throw new InvalidDataException($"percorso non valido «{nomeVoce}»");

        var completo = Path.GetFullPath(Path.Combine(radice, relativo));
        if (!completo.Equals(radice, StringComparison.OrdinalIgnoreCase) && !EDentro(completo, radice))
            throw new InvalidDataException($"percorso non valido «{nomeVoce}»");
        return completo;
    }

    /// <summary>
    /// Porta il database all'ultima versione (il backup può essere di una versione precedente), controlla che sia integro e
    /// che i file dei documenti ci siano. Restituisce quanti documenti non hanno il loro file.
    /// </summary>
    private static async Task<int> VerificaAsync(string destinazione, CancellationToken annullamento)
    {
        var percorsoDatabase = ArchivioDatabase.PercorsoDatabase(destinazione);
        try
        {
            ArchivioDatabase.Inizializza(destinazione);

            await using var db = new AppDbContext(ArchivioDatabase.CreaOpzioni(percorsoDatabase));
            var integrita = await db.Database
                .SqlQueryRaw<string>("SELECT integrity_check AS Value FROM pragma_integrity_check()")
                .ToListAsync(annullamento);
            if (integrita is not ["ok"])
                throw new ArchivioException("Il database contenuto nel backup risulta danneggiato: il backup non si può usare.");

            var percorsi = await db.Documenti.AsNoTracking().Select(d => d.PercorsoRelativo).ToListAsync(annullamento);
            return percorsi.Count(p => !File.Exists(Path.Combine(destinazione, p)));
        }
        catch (Exception ex) when (ex is SqliteException or DbUpdateException)
        {
            throw new ArchivioException($"Il database contenuto nel backup non si riesce a leggere: {ex.Message}");
        }
        finally
        {
            // Senza questo SQLite terrebbe il file aperto: non si potrebbe cancellare né spostare la cartella.
            using var connessione = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = percorsoDatabase }.ToString());
            SqliteConnection.ClearPool(connessione);
        }
    }

    private static void PulisciDestinazione(string destinazione, bool eraNuova)
    {
        try
        {
            if (eraNuova)
            {
                Directory.Delete(destinazione, recursive: true);
                return;
            }

            // Una cartella che c'era già (vuota): si toglie solo quello che si è estratto.
            foreach (var cartella in Directory.EnumerateDirectories(destinazione))
                Directory.Delete(cartella, recursive: true);
            foreach (var file in Directory.EnumerateFiles(destinazione))
                File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Pazienza: l'errore vero è quello che ha fatto fallire il ripristino.
        }
    }

    private static bool EDirectory(ZipArchiveEntry voce) => voce.FullName.EndsWith('/') || voce.FullName.EndsWith('\\');

    private static bool StessaVoce(string a, string b) =>
        a.Replace('\\', '/').Equals(b, StringComparison.OrdinalIgnoreCase);
}
