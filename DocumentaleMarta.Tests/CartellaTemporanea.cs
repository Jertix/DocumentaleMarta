using Microsoft.Data.Sqlite;

namespace DocumentaleMarta.Tests;

/// <summary>Cartella temporanea che sparisce a fine test (rilasciando prima i file SQLite ancora in pool).</summary>
public sealed class CartellaTemporanea : IDisposable
{
    public string Percorso { get; } = Path.Combine(Path.GetTempPath(), "DocumentaleMartaTest_" + Guid.NewGuid().ToString("N"));

    public CartellaTemporanea() => Directory.CreateDirectory(Percorso);

    public string Combina(params string[] parti) => Path.Combine([Percorso, .. parti]);

    public string CreaFile(string nome, string contenuto = "contenuto di prova")
    {
        var percorso = Combina(nome);
        Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);
        File.WriteAllText(percorso, contenuto);
        return percorso;
    }

    public void Dispose()
    {
        RilasciaDatabaseInPool();
        try { Directory.Delete(Percorso, recursive: true); }
        catch (IOException) { /* un antivirus può trattenere il file per un attimo: il sistema pulirà la cartella temp */ }
    }

    /// <summary>
    /// Chiude le connessioni in pool dei soli database di questa cartella. <c>ClearAllPools</c> toccherebbe anche quelle
    /// dei test che girano in parallelo e li farebbe fallire a caso ("database is locked", "disposed object").
    /// </summary>
    private void RilasciaDatabaseInPool()
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(Percorso, "*.db", SearchOption.AllDirectories))
            {
                // Il pool si individua dalla stringa di connessione: deve essere quella usata da ArchivioDatabase.CreaOpzioni.
                using var connessione = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file }.ToString());
                SqliteConnection.ClearPool(connessione);
            }
        }
        catch (DirectoryNotFoundException)
        {
            // Il test ha già cancellato la cartella.
        }
    }
}
