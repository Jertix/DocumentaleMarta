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
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(Percorso, recursive: true); }
        catch (IOException) { /* un antivirus può trattenere il file per un attimo: il sistema pulirà la cartella temp */ }
    }
}
