using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DocumentaleMarta.Tests;

public class DatabaseTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();
    private readonly string _percorsoDb;

    public DatabaseTests() => _percorsoDb = ArchivioDatabase.Inizializza(_tmp.Combina("Documentale"));

    public void Dispose() => _tmp.Dispose();

    private AppDbContext NuovoContesto() => new(ArchivioDatabase.CreaOpzioni(_percorsoDb));

    [Fact]
    public void Inizializza_CreaIlDatabaseDentroLaRadice()
    {
        Assert.Equal(_tmp.Combina("Documentale", "_dati", "documentale.db"), _percorsoDb);
        Assert.True(File.Exists(_percorsoDb));
    }

    [Fact]
    public void Inizializza_DueVolte_NonDaErrori() =>
        ArchivioDatabase.Inizializza(_tmp.Combina("Documentale"));

    [Fact]
    public void SalvaERilegge_AreaCartellaDocumento_ConDateEnum()
    {
        using (var db = NuovoContesto())
        {
            var area = new Area { Nome = "Fatture", PercorsoRelativo = "Fatture" };
            var cartella = new Cartella
            {
                Area = area,
                Titolo = "Fattura 123",
                Descrizione = "descrizione",
                DataScadenza = new DateOnly(2026, 12, 31),
                Ricorrenza = Ricorrenza.Trimestrale,
                PercorsoRelativo = @"Fatture\Fattura 123",
                DataCreazione = new DateTime(2026, 10, 1, 9, 30, 0)
            };
            cartella.Documenti.Add(new Documento
            {
                NomeFile = "f.pdf", PercorsoRelativo = @"Fatture\Fattura 123\f.pdf", Estensione = ".pdf",
                Dimensione = 1234, Hash = "ABC", DataCaricamento = new DateTime(2026, 10, 1, 9, 31, 0)
            });
            db.Cartelle.Add(cartella);
            db.SaveChanges();
        }

        using var lettura = NuovoContesto();
        var letta = lettura.Cartelle.Include(c => c.Area).Include(c => c.Documenti).Single();
        Assert.Equal("Fatture", letta.Area.Nome);
        Assert.Equal(new DateOnly(2026, 12, 31), letta.DataScadenza);
        Assert.Equal(Ricorrenza.Trimestrale, letta.Ricorrenza);
        Assert.Null(letta.DataCompletamento);
        Assert.Equal(StatoIndicizzazione.DaIndicizzare, letta.Documenti.Single().StatoIndicizzazione);
    }

    [Fact]
    public void EliminandoUnArea_SiEliminanoACascataCartelleEDocumenti()
    {
        using var db = NuovoContesto();
        var area = new Area { Nome = "INPS", PercorsoRelativo = "INPS" };
        var cartella = new Cartella { Area = area, Titolo = "C", PercorsoRelativo = @"INPS\C" };
        cartella.Documenti.Add(new Documento { NomeFile = "d.txt", PercorsoRelativo = @"INPS\C\d.txt", Hash = "H" });
        db.Cartelle.Add(cartella);
        db.SaveChanges();

        db.Aree.Remove(area);
        db.SaveChanges();

        Assert.Empty(db.Cartelle);
        Assert.Empty(db.Documenti);
    }

    [Fact]
    public void NomeArea_EUnivoco_SenzaDistinguereMaiuscole()
    {
        using var db = NuovoContesto();
        db.Aree.Add(new Area { Nome = "Fatture", PercorsoRelativo = "Fatture" });
        db.SaveChanges();

        db.Aree.Add(new Area { Nome = "FATTURE", PercorsoRelativo = "Fatture (1)" });
        Assert.Throws<DbUpdateException>(() => db.SaveChanges());
    }

    [Fact]
    public void TabellaFts5_Esiste_EFunzionaConAccentiEMaiuscole()
    {
        using var db = NuovoContesto();
        db.Database.ExecuteSqlRaw("INSERT INTO documenti_fts(rowid, contenuto) VALUES (1, 'Fattura per la società Metalli è Più')");
        db.Database.ExecuteSqlRaw("INSERT INTO documenti_fts(rowid, contenuto) VALUES (2, 'Verbale di riunione')");

        var trovati = db.Database
            .SqlQueryRaw<long>("SELECT rowid AS Value FROM documenti_fts WHERE documenti_fts MATCH 'societa AND piu'")
            .ToList();

        Assert.Equal([1L], trovati);
    }

    [Fact]
    public void TabellaFts5_HaSoloLaColonnaDelTesto_LIdDelDocumentoELARowid()
    {
        using var db = NuovoContesto();

        var colonne = db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('documenti_fts')").ToList();

        Assert.Equal(["contenuto"], colonne);
    }

    [Fact]
    public void Trigger_EliminandoUnDocumento_LoTogliDallIndice()
    {
        using var db = NuovoContesto();
        var cartella = new Cartella { Area = new Area { Nome = "A", PercorsoRelativo = "A" }, Titolo = "C", PercorsoRelativo = @"A\C" };
        cartella.Documenti.Add(new Documento { NomeFile = "uno.txt", PercorsoRelativo = @"A\C\uno.txt", Hash = "1" });
        cartella.Documenti.Add(new Documento { NomeFile = "due.txt", PercorsoRelativo = @"A\C\due.txt", Hash = "2" });
        db.Cartelle.Add(cartella);
        db.SaveChanges();
        var (uno, due) = (cartella.Documenti[0].Id, cartella.Documenti[1].Id);
        db.Database.ExecuteSqlRaw("INSERT INTO documenti_fts(rowid, contenuto) VALUES ({0}, 'alfa'), ({1}, 'beta')", uno, due);

        db.Documenti.Remove(cartella.Documenti[0]);
        db.SaveChanges();

        var restano = db.Database.SqlQueryRaw<long>("SELECT rowid AS Value FROM documenti_fts").ToList();
        Assert.Equal([(long)due], restano);
    }

    [Fact]
    public void Trigger_EliminandoUnAreaACascata_SvuotaLIndiceDeiSuoiDocumenti()
    {
        using var db = NuovoContesto();
        var area = new Area { Nome = "A", PercorsoRelativo = "A" };
        var cartella = new Cartella { Area = area, Titolo = "C", PercorsoRelativo = @"A\C" };
        cartella.Documenti.Add(new Documento { NomeFile = "uno.txt", PercorsoRelativo = @"A\C\uno.txt", Hash = "1" });
        db.Cartelle.Add(cartella);
        var altra = new Cartella { Area = new Area { Nome = "B", PercorsoRelativo = "B" }, Titolo = "C2", PercorsoRelativo = @"B\C2" };
        altra.Documenti.Add(new Documento { NomeFile = "tre.txt", PercorsoRelativo = @"B\C2\tre.txt", Hash = "3" });
        db.Cartelle.Add(altra);
        db.SaveChanges();
        db.Database.ExecuteSqlRaw("INSERT INTO documenti_fts(rowid, contenuto) VALUES ({0}, 'alfa'), ({1}, 'gamma')",
            cartella.Documenti[0].Id, altra.Documenti[0].Id);

        db.Aree.Remove(area); // il database elimina a cascata cartelle e documenti
        db.SaveChanges();

        var restano = db.Database.SqlQueryRaw<long>("SELECT rowid AS Value FROM documenti_fts").ToList();
        Assert.Equal([(long)altra.Documenti[0].Id], restano);
    }
}
