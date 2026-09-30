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

    private string Fisico(params string[] parti) => _files.PercorsoAssoluto(Path.Combine(parti));

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

    // ---------- Dettaglio, aggiornamento, allegati ----------

    private static DatiCartella Dati(string titolo, string? descrizione = null, DateOnly? scadenza = null,
        bool completato = false, DateOnly? dataCompletamento = null) =>
        new(titolo, descrizione, scadenza, completato, dataCompletamento);

    [Fact]
    public async Task CaricaCartella_Inesistente_RestituisceNull() =>
        Assert.Null(await _servizio.CaricaCartellaAsync(999));

    [Fact]
    public async Task CreaCartellaConDati_SalvaICampi_CopiaIFile_LasciaGliOriginali()
    {
        var areaId = await _servizio.CreaAreaAsync("Fatture");
        var f1 = _tmp.CreaFile(Path.Combine("scan", "a.pdf"), "uno");
        var f2 = _tmp.CreaFile(Path.Combine("scan", "b.jpg"), "due");

        var d = await _servizio.CreaCartellaConDatiAsync(
            areaId, Dati("Fattura 12", "note", new DateOnly(2026, 12, 31)), [f1, f2]);

        Assert.Equal("Fatture", d.NomeArea);
        Assert.Equal("Fattura 12", d.Dati.Titolo);
        Assert.Equal("note", d.Dati.Descrizione);
        Assert.Equal(new DateOnly(2026, 12, 31), d.Dati.DataScadenza);
        Assert.Equal(Path.Combine("Fatture", "Fattura 12"), d.PercorsoRelativo);
        Assert.Equal(["a.pdf", "b.jpg"], d.Documenti.Select(x => x.NomeFile));
        Assert.Equal([".pdf", ".jpg"], d.Documenti.Select(x => x.Estensione));
        Assert.Equal("uno", File.ReadAllText(Fisico(d.Documenti[0].PercorsoRelativo)));
        Assert.True(File.Exists(f1) && File.Exists(f2));

        using var db = _factory.CreateDbContext();
        Assert.Equal(2, db.Documenti.Count(x => x.CartellaId == d.Id));
        Assert.All(db.Documenti, x => Assert.Equal(64, x.Hash.Length));
        Assert.All(db.Documenti, x => Assert.Equal(StatoIndicizzazione.DaIndicizzare, x.StatoIndicizzazione));
    }

    [Fact]
    public async Task CreaCartellaConDati_SenzaFile_Funziona()
    {
        var areaId = await _servizio.CreaAreaAsync("Fatture");

        var d = await _servizio.CreaCartellaConDatiAsync(areaId, Dati("Vuota"), []);

        Assert.Empty(d.Documenti);
        Assert.True(Directory.Exists(Fisico(d.PercorsoRelativo)));
    }

    [Fact]
    public async Task CreaCartellaConDati_SecondoFileMancante_NonLasciaNulla()
    {
        var areaId = await _servizio.CreaAreaAsync("Fatture");
        var buono = _tmp.CreaFile("a.pdf");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _servizio.CreaCartellaConDatiAsync(areaId, Dati("Pratica"), [buono, _tmp.Combina("non-esiste.pdf")]));

        Assert.False(Directory.Exists(Fisico("Fatture", "Pratica"))); // né la cartella né la copia del primo file
        using var db = _factory.CreateDbContext();
        Assert.Empty(db.Cartelle);
        Assert.Empty(db.Documenti);
        Assert.True(File.Exists(buono));
    }

    [Fact]
    public async Task CreaCartellaConDati_TitoloVuoto_RifiutatoSenzaCopiare()
    {
        var areaId = await _servizio.CreaAreaAsync("Fatture");

        await Assert.ThrowsAsync<ArchivioException>(() =>
            _servizio.CreaCartellaConDatiAsync(areaId, Dati("  "), [_tmp.CreaFile("a.pdf")]));

        Assert.Empty(Directory.GetDirectories(Fisico("Fatture")));
    }

    [Fact]
    public async Task CreaCartellaConDati_NonCompletata_LaDataDiCompletamentoVieneIgnorata()
    {
        var areaId = await _servizio.CreaAreaAsync("A");

        var d = await _servizio.CreaCartellaConDatiAsync(
            areaId, Dati("X", completato: false, dataCompletamento: new DateOnly(2026, 1, 1)), []);

        Assert.Null(d.Dati.DataCompletamento);
    }

    [Fact]
    public async Task CreaCartellaConDati_Completata_ConservaLaData()
    {
        var areaId = await _servizio.CreaAreaAsync("A");

        var d = await _servizio.CreaCartellaConDatiAsync(
            areaId, Dati("X", completato: true, dataCompletamento: new DateOnly(2026, 9, 30)), []);

        Assert.True(d.Dati.Completato);
        Assert.Equal(new DateOnly(2026, 9, 30), d.Dati.DataCompletamento);
    }

    [Fact]
    public async Task AggiornaCartella_CambiaICampiSenzaToccareLaCartellaFisica()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        var id = await _servizio.CreaCartellaAsync(areaId, "Pratica");

        var d = await _servizio.AggiornaCartellaAsync(
            id, Dati("Pratica", "descrizione", new DateOnly(2027, 1, 15), true, new DateOnly(2026, 10, 1)));

        Assert.Equal("descrizione", d.Dati.Descrizione);
        Assert.Equal(new DateOnly(2027, 1, 15), d.Dati.DataScadenza);
        Assert.True(d.Dati.Completato);
        Assert.Equal(new DateOnly(2026, 10, 1), d.Dati.DataCompletamento);
        Assert.Equal(Path.Combine("A", "Pratica"), d.PercorsoRelativo);
    }

    [Fact]
    public async Task AggiornaCartella_DescrizioneVuota_DiventaNull_ECompletatoFalsoAzzeraLaData()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        var id = await _servizio.CreaCartellaAsync(areaId, "Pratica");
        await _servizio.AggiornaCartellaAsync(id, Dati("Pratica", "x", null, true, new DateOnly(2026, 1, 1)));

        var d = await _servizio.AggiornaCartellaAsync(id, Dati("Pratica", "   ", null, false, new DateOnly(2026, 1, 1)));

        Assert.Null(d.Dati.Descrizione);
        Assert.False(d.Dati.Completato);
        Assert.Null(d.Dati.DataCompletamento);
    }

    [Fact]
    public async Task AggiornaCartella_CambioTitolo_RinominaLaCartellaEIPercorsiDeiDocumenti()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        var d0 = await _servizio.CreaCartellaConDatiAsync(areaId, Dati("Vecchio"), [_tmp.CreaFile("a.pdf")]);

        var d = await _servizio.AggiornaCartellaAsync(d0.Id, Dati("Nuovo", "nota"));

        Assert.Equal("Nuovo", d.Dati.Titolo);
        Assert.Equal(Path.Combine("A", "Nuovo"), d.PercorsoRelativo);
        Assert.Equal(Path.Combine("A", "Nuovo", "a.pdf"), d.Documenti.Single().PercorsoRelativo);
        Assert.True(File.Exists(Fisico(d.Documenti.Single().PercorsoRelativo)));
        Assert.False(Directory.Exists(Fisico("A", "Vecchio")));
    }

    [Fact]
    public async Task AggiornaCartella_TitoloVuoto_Rifiutato()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        var id = await _servizio.CreaCartellaAsync(areaId, "Pratica");

        await Assert.ThrowsAsync<ArchivioException>(() => _servizio.AggiornaCartellaAsync(id, Dati(" ")));

        Assert.True(Directory.Exists(Fisico("A", "Pratica")));
    }

    [Fact]
    public async Task AggiornaCartella_FileAperto_LaRinominaFallisceENessunCampoCambia()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        var d0 = await _servizio.CreaCartellaConDatiAsync(areaId, Dati("Vecchio", "originale"), [_tmp.CreaFile("a.pdf")]);

        using (new FileStream(Fisico(d0.Documenti[0].PercorsoRelativo), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => _servizio.AggiornaCartellaAsync(d0.Id, Dati("Nuovo", "modificata")));
        }

        var d = (await _servizio.CaricaCartellaAsync(d0.Id))!;
        Assert.Equal("Vecchio", d.Dati.Titolo);
        Assert.Equal("originale", d.Dati.Descrizione);
        Assert.True(File.Exists(Fisico(d.Documenti[0].PercorsoRelativo)));
    }

    [Fact]
    public async Task AggiornaCartella_Inesistente_DaErroreChiaro() =>
        await Assert.ThrowsAsync<ArchivioException>(() => _servizio.AggiornaCartellaAsync(999, Dati("X")));

    [Fact]
    public async Task AllegaDocumenti_CopiaISalvaLeRighe_ConNomiUnivoci()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        var cartellaId = await _servizio.CreaCartellaAsync(areaId, "Pratica");
        var primo = _tmp.CreaFile(Path.Combine("x", "doc.txt"), "uno");
        var secondo = _tmp.CreaFile(Path.Combine("y", "doc.txt"), "due");

        var nuovi = await _servizio.AllegaDocumentiAsync(cartellaId, [primo, secondo]);

        Assert.Equal(["doc.txt", "doc (1).txt"], nuovi.Select(d => d.NomeFile));
        Assert.All(nuovi, d => Assert.True(d.Id > 0));
        Assert.Equal("due", File.ReadAllText(Fisico(nuovi[1].PercorsoRelativo)));
        var dettaglio = (await _servizio.CaricaCartellaAsync(cartellaId))!;
        Assert.Equal(2, dettaglio.Documenti.Count);
    }

    [Fact]
    public async Task AllegaDocumenti_SecondoFileMancante_NonAggiungeNulla()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        var cartellaId = await _servizio.CreaCartellaAsync(areaId, "Pratica");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _servizio.AllegaDocumentiAsync(cartellaId, [_tmp.CreaFile("a.txt"), _tmp.Combina("manca.txt")]));

        Assert.Empty(Directory.GetFiles(Fisico("A", "Pratica")));
        using var db = _factory.CreateDbContext();
        Assert.Empty(db.Documenti);
    }

    [Fact]
    public async Task AllegaDocumenti_ListaVuota_NonFaNulla()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        var cartellaId = await _servizio.CreaCartellaAsync(areaId, "Pratica");

        Assert.Empty(await _servizio.AllegaDocumentiAsync(cartellaId, []));
    }

    [Fact]
    public async Task AllegaDocumenti_CartellaInesistente_DaErroreChiaro() =>
        await Assert.ThrowsAsync<ArchivioException>(() => _servizio.AllegaDocumentiAsync(999, [_tmp.CreaFile("a.txt")]));

    [Fact]
    public async Task EliminaDocumento_RimuoveRigaEFile_LasciaGliAltri()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        var d = await _servizio.CreaCartellaConDatiAsync(areaId, Dati("P"), [_tmp.CreaFile("a.txt"), _tmp.CreaFile("b.txt")]);

        await _servizio.EliminaDocumentoAsync(d.Documenti[0].Id);

        Assert.False(File.Exists(Fisico(d.Documenti[0].PercorsoRelativo)));
        Assert.True(File.Exists(Fisico(d.Documenti[1].PercorsoRelativo)));
        Assert.Equal(["b.txt"], (await _servizio.CaricaCartellaAsync(d.Id))!.Documenti.Select(x => x.NomeFile));
    }

    [Fact]
    public async Task EliminaDocumento_FileGiaSparitoDalDisco_EliminaComunqueLaRiga()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        var d = await _servizio.CreaCartellaConDatiAsync(areaId, Dati("P"), [_tmp.CreaFile("a.txt")]);
        File.Delete(Fisico(d.Documenti[0].PercorsoRelativo));

        await _servizio.EliminaDocumentoAsync(d.Documenti[0].Id);

        Assert.Empty((await _servizio.CaricaCartellaAsync(d.Id))!.Documenti);
    }

    [Fact]
    public async Task EliminaDocumento_FileAperto_FallisceELaRigaResta()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        var d = await _servizio.CreaCartellaConDatiAsync(areaId, Dati("P"), [_tmp.CreaFile("a.txt")]);

        using (new FileStream(Fisico(d.Documenti[0].PercorsoRelativo), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => _servizio.EliminaDocumentoAsync(d.Documenti[0].Id));
        }

        Assert.Single((await _servizio.CaricaCartellaAsync(d.Id))!.Documenti);
    }

    [Fact]
    public async Task EliminaDocumento_Inesistente_DaErroreChiaro() =>
        await Assert.ThrowsAsync<ArchivioException>(() => _servizio.EliminaDocumentoAsync(999));

    // ---------- Elenco documenti (radice e aree) ----------

    private async Task<(int Area1, int Area2)> DueAreeConDocumentiAsync()
    {
        var a1 = await _servizio.CreaAreaAsync("Fatture");
        var a2 = await _servizio.CreaAreaAsync("INPS");
        var c1 = await _servizio.CreaCartellaConDatiAsync(a1, Dati("Fattura 1", scadenza: new DateOnly(2026, 11, 30)), []);
        var c2 = await _servizio.CreaCartellaConDatiAsync(a2, Dati("Contributi"), []);

        using var db = _factory.CreateDbContext();
        db.Documenti.AddRange(
            new Documento { CartellaId = c1.Id, NomeFile = "vecchio.pdf", Estensione = ".pdf", Dimensione = 10, Hash = "1",
                            PercorsoRelativo = @"Fatture\Fattura 1\vecchio.pdf", DataCaricamento = new DateTime(2026, 1, 1) },
            new Documento { CartellaId = c2.Id, NomeFile = "nuovo.docx", Estensione = ".docx", Dimensione = 20, Hash = "2",
                            PercorsoRelativo = @"INPS\Contributi\nuovo.docx", DataCaricamento = new DateTime(2026, 9, 1) },
            new Documento { CartellaId = c1.Id, NomeFile = "medio.jpg", Estensione = ".jpg", Dimensione = 30, Hash = "3",
                            PercorsoRelativo = @"Fatture\Fattura 1\medio.jpg", DataCaricamento = new DateTime(2026, 5, 1) });
        db.SaveChanges();
        return (a1, a2);
    }

    [Fact]
    public async Task CaricaDocumenti_Tutti_DalPiuRecenteAlPiuVecchio_ConCartellaEAreaEScadenza()
    {
        await DueAreeConDocumentiAsync();

        var elenco = await _servizio.CaricaDocumentiAsync(null);

        Assert.Equal(["nuovo.docx", "medio.jpg", "vecchio.pdf"], elenco.Select(d => d.NomeFile));
        var nuovo = elenco[0];
        Assert.Equal("Contributi", nuovo.TitoloCartella);
        Assert.Equal("INPS", nuovo.NomeArea);
        Assert.Null(nuovo.ScadenzaCartella);
        Assert.Equal(20, nuovo.Dimensione);
        Assert.Equal(new DateOnly(2026, 11, 30), elenco[1].ScadenzaCartella);
        Assert.Equal("Fatture", elenco[1].NomeArea);
    }

    [Fact]
    public async Task CaricaDocumenti_DiUnArea_SoloQuelliDiQuellArea()
    {
        var (a1, a2) = await DueAreeConDocumentiAsync();

        Assert.Equal(["medio.jpg", "vecchio.pdf"], (await _servizio.CaricaDocumentiAsync(a1)).Select(d => d.NomeFile));
        Assert.Equal(["nuovo.docx"], (await _servizio.CaricaDocumentiAsync(a2)).Select(d => d.NomeFile));
    }

    [Fact]
    public async Task CaricaDocumenti_AreaSenzaDocumentiOInesistente_ListaVuota()
    {
        var area = await _servizio.CreaAreaAsync("Vuota");

        Assert.Empty(await _servizio.CaricaDocumentiAsync(area));
        Assert.Empty(await _servizio.CaricaDocumentiAsync(999));
        Assert.Empty(await _servizio.CaricaDocumentiAsync(null));
    }

    // ---------- Scadenze ----------

    [Fact]
    public async Task CaricaAlbero_PortaScadenzaECompletato()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        await _servizio.CreaCartellaConDatiAsync(areaId, Dati("Con scadenza", scadenza: new DateOnly(2026, 11, 5)), []);
        await _servizio.CreaCartellaConDatiAsync(areaId, Dati("Completata", scadenza: new DateOnly(2026, 9, 1),
            completato: true, dataCompletamento: new DateOnly(2026, 8, 30)), []);
        await _servizio.CreaCartellaConDatiAsync(areaId, Dati("Senza"), []);

        var cartelle = (await _servizio.CaricaAlberoAsync()).Single().Cartelle;

        var conScadenza = cartelle.Single(c => c.Titolo == "Con scadenza");
        Assert.Equal(new DateOnly(2026, 11, 5), conScadenza.DataScadenza);
        Assert.False(conScadenza.Completato);
        Assert.True(cartelle.Single(c => c.Titolo == "Completata").Completato);
        Assert.Null(cartelle.Single(c => c.Titolo == "Senza").DataScadenza);
    }

    [Fact]
    public async Task CaricaDocumenti_PortaLoStatoCompletatoDellaCartella()
    {
        var areaId = await _servizio.CreaAreaAsync("A");
        await _servizio.CreaCartellaConDatiAsync(areaId, Dati("Aperta"), [_tmp.CreaFile("a.txt")]);
        await _servizio.CreaCartellaConDatiAsync(areaId, Dati("Chiusa", completato: true, dataCompletamento: new DateOnly(2026, 1, 1)),
            [_tmp.CreaFile("b.txt")]);

        var documenti = await _servizio.CaricaDocumentiAsync(null);

        Assert.False(documenti.Single(d => d.NomeFile == "a.txt").CartellaCompletata);
        Assert.True(documenti.Single(d => d.NomeFile == "b.txt").CartellaCompletata);
    }

    [Fact]
    public async Task CaricaScadenze_SoloCartelleNonCompletateConScadenza_DallaPiuVicina()
    {
        var a1 = await _servizio.CreaAreaAsync("Fatture");
        var a2 = await _servizio.CreaAreaAsync("INPS");
        await _servizio.CreaCartellaConDatiAsync(a1, Dati("Lontana", scadenza: new DateOnly(2027, 1, 31)), []);
        await _servizio.CreaCartellaConDatiAsync(a2, Dati("Scaduta", scadenza: new DateOnly(2026, 8, 1)), [_tmp.CreaFile("x.pdf"), _tmp.CreaFile("y.pdf")]);
        await _servizio.CreaCartellaConDatiAsync(a1, Dati("Vicina", scadenza: new DateOnly(2026, 10, 10)), []);
        await _servizio.CreaCartellaConDatiAsync(a1, Dati("Finita", scadenza: new DateOnly(2026, 9, 1), completato: true,
            dataCompletamento: new DateOnly(2026, 8, 30)), []);
        await _servizio.CreaCartellaConDatiAsync(a1, Dati("Senza scadenza"), []);

        var scadenze = await _servizio.CaricaScadenzeAsync();

        Assert.Equal(["Scaduta", "Vicina", "Lontana"], scadenze.Select(s => s.Titolo));
        Assert.Equal("INPS", scadenze[0].NomeArea);
        Assert.Equal(new DateOnly(2026, 8, 1), scadenze[0].DataScadenza);
        Assert.Equal(2, scadenze[0].NumeroDocumenti);
        Assert.Equal(a1, scadenze[1].AreaId);
    }

    [Fact]
    public async Task CaricaScadenze_AParitaDiDataOrdinaPerTitolo()
    {
        var area = await _servizio.CreaAreaAsync("A");
        var data = new DateOnly(2026, 10, 10);
        await _servizio.CreaCartellaConDatiAsync(area, Dati("Zeta", scadenza: data), []);
        await _servizio.CreaCartellaConDatiAsync(area, Dati("alfa", scadenza: data), []);

        Assert.Equal(["alfa", "Zeta"], (await _servizio.CaricaScadenzeAsync()).Select(s => s.Titolo));
    }

    [Fact]
    public async Task CaricaScadenze_SpuntandoCompletato_LaCartellaSparisceDallElenco()
    {
        var area = await _servizio.CreaAreaAsync("A");
        var d = await _servizio.CreaCartellaConDatiAsync(area, Dati("P", scadenza: new DateOnly(2026, 10, 10)), []);
        Assert.Single(await _servizio.CaricaScadenzeAsync());

        await _servizio.AggiornaCartellaAsync(d.Id, d.Dati with { Completato = true, DataCompletamento = new DateOnly(2026, 10, 1) });

        Assert.Empty(await _servizio.CaricaScadenzeAsync());
    }

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
