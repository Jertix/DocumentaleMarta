using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;
using Microsoft.EntityFrameworkCore;

namespace DocumentaleMarta.Tests;

public class ArchivioServiceTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();
    private readonly string _radice;
    private readonly AppDbContextFactory _factory;
    private readonly ArchivioFileService _files;
    private readonly ArchivioService _servizio;

    public ArchivioServiceTests()
    {
        _radice = _tmp.Combina("Documentale");
        var percorsoDb = ArchivioDatabase.Inizializza(_radice);
        _factory = new AppDbContextFactory(ArchivioDatabase.CreaOpzioni(percorsoDb));
        _files = new ArchivioFileService(_radice, usaCestino: false);
        _servizio = new ArchivioService(_factory, _files);
    }

    public void Dispose() => _tmp.Dispose();

    private string Fisico(string relativo) => _files.PercorsoAssoluto(relativo);

    // ---------- Aree ----------

    [Fact]
    public async Task CreaArea_CreaRigaECartellaFisica()
    {
        var id = await _servizio.CreaAreaAsync("  Fatture  ");

        using var db = _factory.CreateDbContext();
        var area = db.Aree.Single(a => a.Id == id);
        Assert.Equal("Fatture", area.Nome);
        Assert.Equal("Fatture", area.PercorsoRelativo);
        Assert.True(Directory.Exists(Fisico("Fatture")));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreaArea_NomeVuoto_Rifiutato(string nome)
    {
        await Assert.ThrowsAsync<ArchivioException>(() => _servizio.CreaAreaAsync(nome));
        Assert.Empty((await _servizio.CaricaAlberoAsync()));
    }

    [Fact]
    public async Task CreaArea_NomeTroppoLungo_Rifiutato() =>
        await Assert.ThrowsAsync<ArchivioException>(() => _servizio.CreaAreaAsync(new string('a', 101)));

    [Theory]
    [InlineData("Fatture")]
    [InlineData("FATTURE")]
    [InlineData("  fatture ")]
    public async Task CreaArea_NomeGiaUsato_RifiutatoSenzaCreareCartelle(string secondoNome)
    {
        await _servizio.CreaAreaAsync("Fatture");

        var ex = await Assert.ThrowsAsync<ArchivioException>(() => _servizio.CreaAreaAsync(secondoNome));

        Assert.Contains("Esiste già", ex.Message);
        Assert.Single(Directory.GetDirectories(_radice), d => !d.EndsWith("_dati"));
    }

    [Fact]
    public async Task CreaArea_CaratteriVietati_LaCartellaFisicaEPulita_IlNomeResta()
    {
        var id = await _servizio.CreaAreaAsync("Enti: INPS/INAIL");

        using var db = _factory.CreateDbContext();
        var area = db.Aree.Single(a => a.Id == id);
        Assert.Equal("Enti: INPS/INAIL", area.Nome);
        Assert.Equal("Enti_ INPS_INAIL", area.PercorsoRelativo);
        Assert.True(Directory.Exists(Fisico(area.PercorsoRelativo)));
    }

    [Fact]
    public async Task RinominaArea_RinominaCartellaFisica_EAggiornaPercorsiDiCartelleEDocumenti()
    {
        var areaId = await _servizio.CreaAreaAsync("Vecchia");
        var cartellaId = await _servizio.CreaCartellaAsync(areaId, "Pratica");
        AggiungiDocumento(cartellaId, "doc.txt");

        await _servizio.RinominaAreaAsync(areaId, "Nuova");

        using var db = _factory.CreateDbContext();
        var area = db.Aree.Single();
        var cartella = db.Cartelle.Single();
        var documento = db.Documenti.Single();
        Assert.Equal("Nuova", area.Nome);
        Assert.Equal("Nuova", area.PercorsoRelativo);
        Assert.Equal(Path.Combine("Nuova", "Pratica"), cartella.PercorsoRelativo);
        Assert.Equal(Path.Combine("Nuova", "Pratica", "doc.txt"), documento.PercorsoRelativo);
        Assert.False(Directory.Exists(Fisico("Vecchia")));
        Assert.True(File.Exists(Fisico(documento.PercorsoRelativo)));
    }

    [Fact]
    public async Task RinominaArea_SoloMaiuscole_Funziona()
    {
        var id = await _servizio.CreaAreaAsync("inps");

        await _servizio.RinominaAreaAsync(id, "INPS");

        using var db = _factory.CreateDbContext();
        Assert.Equal("INPS", db.Aree.Single().Nome);
        Assert.Equal("INPS", Path.GetFileName(Directory.GetDirectories(_radice).Single(d => !d.EndsWith("_dati"))));
    }

    [Fact]
    public async Task RinominaArea_NomeDiUnAltraArea_RifiutataEDiscoInvariato()
    {
        await _servizio.CreaAreaAsync("A");
        var b = await _servizio.CreaAreaAsync("B");

        await Assert.ThrowsAsync<ArchivioException>(() => _servizio.RinominaAreaAsync(b, "a"));

        Assert.True(Directory.Exists(Fisico("B")));
        Assert.Equal(["A", "B"], (await _servizio.CaricaAlberoAsync()).Select(a => a.Nome));
    }

    [Fact]
    public async Task RinominaArea_StessoNome_NonFaNulla()
    {
        var id = await _servizio.CreaAreaAsync("A");
        await _servizio.RinominaAreaAsync(id, "A");
        Assert.True(Directory.Exists(Fisico("A")));
    }

    [Fact]
    public async Task RinominaArea_CartellaFisicaSparita_LaRicreaConIlNuovoNome()
    {
        var id = await _servizio.CreaAreaAsync("Vecchia");
        Directory.Delete(Fisico("Vecchia"));

        await _servizio.RinominaAreaAsync(id, "Nuova");

        Assert.True(Directory.Exists(Fisico("Nuova")));
        using var db = _factory.CreateDbContext();
        Assert.Equal("Nuova", db.Aree.Single().PercorsoRelativo);
    }

    [Fact]
    public async Task RinominaArea_FileAperto_FallisceELoStatoResta()
    {
        var areaId = await _servizio.CreaAreaAsync("Vecchia");
        var cartellaId = await _servizio.CreaCartellaAsync(areaId, "Pratica");
        var percorsoDoc = AggiungiDocumento(cartellaId, "doc.txt");

        using (new FileStream(Fisico(percorsoDoc), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<Exception>(() => _servizio.RinominaAreaAsync(areaId, "Nuova"));
        }

        using var db = _factory.CreateDbContext();
        Assert.Equal("Vecchia", db.Aree.Single().Nome);
        Assert.Equal("Vecchia", db.Aree.Single().PercorsoRelativo);
        Assert.True(File.Exists(Fisico(percorsoDoc)));
    }

    [Fact]
    public async Task EliminaArea_RimuoveRigheACascataECartellaFisica()
    {
        var areaId = await _servizio.CreaAreaAsync("Da eliminare");
        var cartellaId = await _servizio.CreaCartellaAsync(areaId, "Pratica");
        AggiungiDocumento(cartellaId, "doc.txt");
        await _servizio.CreaAreaAsync("Resta");

        await _servizio.EliminaAreaAsync(areaId);

        using var db = _factory.CreateDbContext();
        Assert.Equal(["Resta"], db.Aree.Select(a => a.Nome).ToList());
        Assert.Empty(db.Cartelle);
        Assert.Empty(db.Documenti);
        Assert.False(Directory.Exists(Fisico("Da eliminare")));
        Assert.True(Directory.Exists(Fisico("Resta")));
    }

    [Fact]
    public async Task EliminaArea_CartellaFisicaGiaSparita_EliminaComunqueLaRiga()
    {
        var id = await _servizio.CreaAreaAsync("A");
        Directory.Delete(Fisico("A"));

        await _servizio.EliminaAreaAsync(id);

        Assert.Empty(await _servizio.CaricaAlberoAsync());
    }

    [Fact]
    public async Task EliminaArea_FileAperto_FallisceEIlDatabaseNonCambia()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        var cartellaId = await _servizio.CreaCartellaAsync(areaId, "Pratica");
        var percorsoDoc = AggiungiDocumento(cartellaId, "doc.txt");

        using (new FileStream(Fisico(percorsoDoc), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => _servizio.EliminaAreaAsync(areaId));
        }

        using var db = _factory.CreateDbContext();
        Assert.Single(db.Aree);
        Assert.Single(db.Cartelle);
        Assert.Single(db.Documenti);
        Assert.True(File.Exists(Fisico(percorsoDoc)));
    }

    [Fact]
    public async Task EliminaArea_Inesistente_DaErroreChiaro() =>
        await Assert.ThrowsAsync<ArchivioException>(() => _servizio.EliminaAreaAsync(999));

    // ---------- Cartelle ----------

    [Fact]
    public async Task CreaCartella_CreaRigaECartellaFisicaDentroLArea()
    {
        var areaId = await _servizio.CreaAreaAsync("Fatture");

        var id = await _servizio.CreaCartellaAsync(areaId, "Fattura 1/2026");

        using var db = _factory.CreateDbContext();
        var cartella = db.Cartelle.Single(c => c.Id == id);
        Assert.Equal("Fattura 1/2026", cartella.Titolo);
        Assert.Equal(Path.Combine("Fatture", "Fattura 1_2026"), cartella.PercorsoRelativo);
        Assert.Equal(areaId, cartella.AreaId);
        Assert.True(Directory.Exists(Fisico(cartella.PercorsoRelativo)));
        Assert.False(cartella.Completato);
        Assert.Null(cartella.DataScadenza);
        Assert.True((DateTime.Now - cartella.DataCreazione).TotalMinutes < 1);
    }

    [Fact]
    public async Task CreaCartella_StessoTitoloDueVolte_Ammesso_ConCartellaFisicaDiversa()
    {
        var areaId = await _servizio.CreaAreaAsync("Scadenze");

        await _servizio.CreaCartellaAsync(areaId, "F24");
        await _servizio.CreaCartellaAsync(areaId, "F24");

        using var db = _factory.CreateDbContext();
        Assert.Equal([Path.Combine("Scadenze", "F24"), Path.Combine("Scadenze", "F24 (1)")],
            db.Cartelle.OrderBy(c => c.Id).Select(c => c.PercorsoRelativo).ToList());
    }

    [Fact]
    public async Task CreaCartella_AreaInesistente_DaErroreChiaro() =>
        await Assert.ThrowsAsync<ArchivioException>(() => _servizio.CreaCartellaAsync(999, "X"));

    [Fact]
    public async Task CreaCartella_TitoloVuoto_Rifiutato()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        await Assert.ThrowsAsync<ArchivioException>(() => _servizio.CreaCartellaAsync(areaId, " "));
        Assert.Empty(Directory.GetDirectories(Fisico("A")));
    }

    [Fact]
    public async Task RinominaCartella_RinominaCartellaFisica_EAggiornaIDocumenti()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        var cartellaId = await _servizio.CreaCartellaAsync(areaId, "Vecchia");
        AggiungiDocumento(cartellaId, "doc.txt");

        await _servizio.RinominaCartellaAsync(cartellaId, "Nuova");

        using var db = _factory.CreateDbContext();
        var cartella = db.Cartelle.Single();
        Assert.Equal("Nuova", cartella.Titolo);
        Assert.Equal(Path.Combine("A", "Nuova"), cartella.PercorsoRelativo);
        Assert.Equal(Path.Combine("A", "Nuova", "doc.txt"), db.Documenti.Single().PercorsoRelativo);
        Assert.False(Directory.Exists(Fisico(Path.Combine("A", "Vecchia"))));
        Assert.True(File.Exists(Fisico(Path.Combine("A", "Nuova", "doc.txt"))));
    }

    [Fact]
    public async Task RinominaCartella_ConTitoloDiUnAltraCartella_Ammesso_ConCartellaFisicaDiversa()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        await _servizio.CreaCartellaAsync(areaId, "F24");
        var seconda = await _servizio.CreaCartellaAsync(areaId, "Altro");

        await _servizio.RinominaCartellaAsync(seconda, "F24");

        using var db = _factory.CreateDbContext();
        Assert.Equal(Path.Combine("A", "F24 (1)"), db.Cartelle.Single(c => c.Id == seconda).PercorsoRelativo);
    }

    [Fact]
    public async Task EliminaCartella_RimuoveRigaDocumentiECartellaFisica_LasciaLeAltre()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        var daEliminare = await _servizio.CreaCartellaAsync(areaId, "Via");
        var resta = await _servizio.CreaCartellaAsync(areaId, "Resta");
        AggiungiDocumento(daEliminare, "doc.txt");

        await _servizio.EliminaCartellaAsync(daEliminare);

        using var db = _factory.CreateDbContext();
        Assert.Equal(resta, db.Cartelle.Single().Id);
        Assert.Empty(db.Documenti);
        Assert.False(Directory.Exists(Fisico(Path.Combine("A", "Via"))));
        Assert.True(Directory.Exists(Fisico(Path.Combine("A", "Resta"))));
    }

    [Fact]
    public async Task EliminaCartella_FileAperto_FallisceEIlDatabaseNonCambia()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        var cartellaId = await _servizio.CreaCartellaAsync(areaId, "Pratica");
        var percorsoDoc = AggiungiDocumento(cartellaId, "doc.txt");

        using (new FileStream(Fisico(percorsoDoc), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => _servizio.EliminaCartellaAsync(cartellaId));
        }

        using var db = _factory.CreateDbContext();
        Assert.Single(db.Cartelle);
        Assert.Single(db.Documenti);
    }

    // ---------- Albero ----------

    [Fact]
    public async Task CaricaAlbero_OrdinaAlfabeticamenteSenzaDistinguereMaiuscole_EContaIDocumenti()
    {
        await _servizio.CreaAreaAsync("zeta");
        var alfa = await _servizio.CreaAreaAsync("Alfa");
        await _servizio.CreaAreaAsync("èlite");
        var c2 = await _servizio.CreaCartellaAsync(alfa, "beta");
        await _servizio.CreaCartellaAsync(alfa, "Alpha");
        AggiungiDocumento(c2, "a.txt");
        AggiungiDocumento(c2, "b.txt");

        var albero = await _servizio.CaricaAlberoAsync();

        Assert.Equal(["Alfa", "èlite", "zeta"], albero.Select(a => a.Nome));
        Assert.Equal(["Alpha", "beta"], albero[0].Cartelle.Select(c => c.Titolo));
        Assert.Equal([0, 2], albero[0].Cartelle.Select(c => c.NumeroDocumenti));
        Assert.Empty(albero[2].Cartelle);
    }

    [Fact]
    public async Task CaricaAlbero_DatabaseVuoto_ListaVuota() =>
        Assert.Empty(await _servizio.CaricaAlberoAsync());

    // ---------- Supporto ----------

    /// <summary>Inserisce un documento nel database e il file corrispondente sul disco. Restituisce il percorso relativo.</summary>
    private string AggiungiDocumento(int cartellaId, string nomeFile)
    {
        using var db = _factory.CreateDbContext();
        var cartella = db.Cartelle.Single(c => c.Id == cartellaId);
        var relativo = Path.Combine(cartella.PercorsoRelativo, nomeFile);
        Directory.CreateDirectory(Path.GetDirectoryName(Fisico(relativo))!);
        File.WriteAllText(Fisico(relativo), "contenuto");

        db.Documenti.Add(new Documento
        {
            CartellaId = cartellaId,
            NomeFile = nomeFile,
            PercorsoRelativo = relativo,
            Estensione = Path.GetExtension(nomeFile),
            Hash = "H",
            DataCaricamento = DateTime.Now
        });
        db.SaveChanges();
        return relativo;
    }
}
