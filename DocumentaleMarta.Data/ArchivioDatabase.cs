using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DocumentaleMarta.Data;

/// <summary>Posizione del database e sua creazione/aggiornamento.</summary>
public static class ArchivioDatabase
{
    public const string CartellaDati = "_dati";
    public const string NomeFileDatabase = "documentale.db";

    /// <summary>Il database sta dentro la radice, così un backup copia un'unica cartella.</summary>
    public static string PercorsoDatabase(string percorsoRadice) =>
        Path.Combine(percorsoRadice, CartellaDati, NomeFileDatabase);

    public static DbContextOptions<AppDbContext> CreaOpzioni(string percorsoDatabase)
    {
        var connessione = new SqliteConnectionStringBuilder { DataSource = percorsoDatabase }.ToString();
        return new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connessione).Options;
    }

    /// <summary>Crea la cartella dei dati e applica le migrazioni. Restituisce il percorso del database.</summary>
    public static string Inizializza(string percorsoRadice)
    {
        var percorso = PercorsoDatabase(percorsoRadice);
        Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);

        using var db = new AppDbContext(CreaOpzioni(percorso));
        db.Database.Migrate();
        return percorso;
    }
}
