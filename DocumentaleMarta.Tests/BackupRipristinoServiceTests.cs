using System.IO.Compression;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DocumentaleMarta.Tests;

/// <summary>Ripristinare un backup: si estrae in una cartella nuova, si controlla, e l'archivio attuale non si tocca mai.</summary>
public class BackupRipristinoServiceTests : IDisposable
{
    private static readonly DateTime Adesso = new(2026, 10, 1, 14, 30, 0);

    private readonly ArchivioDiProva _a;
    private readonly BackupService _backup;
    private readonly string _cartellaBackup;
    private readonly string _ripristino;

    public BackupRipristinoServiceTests()
    {
        var estrattore = new FintoEstrattore(".txt")
        {
            Logica = (p, _) => Task.FromResult("Preventivo carpenteria zincata per il cancello Rossi")
        };
        _a = new ArchivioDiProva(estrattore);
        _backup = new BackupService(_a.Factory, _a.Files, () => Adesso);
        _cartellaBackup = _a.Tmp.Combina("backup");
        _ripristino = _a.Tmp.Combina("ripristino");
    }

    public void Dispose() => _a.Dispose();

    /// <summary>Fatture (con "Fattura Rossi": preventivo.txt e foto.jpg, completata e archiviata) e un'area vuota.</summary>
    private async Task CreaArchivioAsync()
    {
        var fatture = await _a.Servizio.CreaAreaAsync("Fatture");
        await _a.Servizio.CreaAreaAsync("Area vuota");
        var c = await _a.CreaCartellaInAreaAsync(fatture, "Fattura Rossi", ["preventivo.txt", "foto.jpg"], new DateOnly(2026, 10, 20), "Per il cancello");
        await _a.CreaCartellaInAreaAsync(fatture, "Senza documenti", []);
        await _a.Servizio.AggiornaCartellaAsync(c.Id, c.Dati with { Completato = true, DataCompletamento = new DateOnly(2026, 10, 1) });
        await _a.Servizio.ArchiviaCartellaAsync(c.Id);
        await _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));
    }

    private async Task<string> FaiBackupAsync() => (await _backup.CreaBackupAsync(_cartellaBackup)).PercorsoZip;

    private static async Task<List<string>> AlberoAsync(IArchivioService servizio) =>
        (await servizio.CaricaAlberoAsync())
        .SelectMany(a => a.Cartelle.Select(c => $"{a.Nome}/{c.Titolo}/{c.NumeroDocumenti}/{c.Archiviata}/{c.Completato}"))
        .Concat((await servizio.CaricaAlberoAsync()).Where(a => a.Cartelle.Count == 0).Select(a => $"{a.Nome}/-"))
        .Order().ToList();

    // ---------- Il giro completo ----------

    [Fact]
    public async Task BackupERipristino_RiportanoLArchivioCom_Era_ConAlberoDocumentiRicercaEArchiviate()
    {
        await CreaArchivioAsync();
        var zip = await FaiBackupAsync();

        var esito = await _backup.RipristinaAsync(zip, _ripristino);

        Assert.Equal(_ripristino, esito.Cartella);
        Assert.Equal(3, esito.NumeroFile); // due documenti e il database
        Assert.Equal(0, esito.DocumentiMancanti);

        var files = new ArchivioFileService(_ripristino, usaCestino: false);
        var factory = new AppDbContextFactory(ArchivioDatabase.CreaOpzioni(ArchivioDatabase.PercorsoDatabase(_ripristino)));
        var ripristinato = new ArchivioService(factory, files);

        Assert.Equal(await AlberoAsync(_a.Servizio), await AlberoAsync(ripristinato));
        var documenti = await ripristinato.CaricaDocumentiAsync(null);
        Assert.Equal(["foto.jpg", "preventivo.txt"], documenti.Select(d => d.NomeFile).Order());
        Assert.All(documenti, d => Assert.True(files.Esiste(d.PercorsoRelativo)));
        Assert.Equal(
            File.ReadAllText(_a.Fisico("Fatture", "Fattura Rossi", "preventivo.txt")),
            File.ReadAllText(files.PercorsoAssoluto(documenti.Single(d => d.NomeFile == "preventivo.txt").PercorsoRelativo)));

        // L'indice del testo e lo stato "archiviata" sono arrivati: la ricerca funziona sul ripristinato.
        var ricerca = new RicercaService(factory);
        Assert.Contains((await ricerca.CercaAsync("zincata")).Risultati, r => r.Documento.NomeFile == "preventivo.txt");
        Assert.NotEmpty((await ricerca.CercaAsync("", new FiltriRicerca(Stato: StatoCartella.Archiviata))).Risultati);
    }

    [Fact]
    public async Task IlRipristino_NonTocca_LArchivioAttuale()
    {
        await CreaArchivioAsync();
        var zip = await FaiBackupAsync();
        var prima = Directory.EnumerateFiles(_a.Radice, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("_dati")).Order().ToList();
        var alberoPrima = await AlberoAsync(_a.Servizio);

        await _backup.RipristinaAsync(zip, _ripristino);

        Assert.Equal(prima, Directory.EnumerateFiles(_a.Radice, "*", SearchOption.AllDirectories).Where(f => !f.Contains("_dati")).Order());
        Assert.Equal(alberoPrima, await AlberoAsync(_a.Servizio));
    }

    [Fact]
    public async Task LeIstruzioni_NonSiEstraggonoNellArchivio()
    {
        await CreaArchivioAsync();

        await _backup.RipristinaAsync(await FaiBackupAsync(), _ripristino);

        Assert.False(File.Exists(Path.Combine(_ripristino, BackupService.NomeLeggimi)));
        Assert.True(File.Exists(Path.Combine(_ripristino, "_dati", "documentale.db")));
        Assert.True(Directory.Exists(Path.Combine(_ripristino, "Area vuota")));            // le cartelle vuote tornano
        Assert.True(Directory.Exists(Path.Combine(_ripristino, "Fatture", "Senza documenti")));
    }

    [Fact]
    public async Task LaDataDeiFile_SiConserva()
    {
        await CreaArchivioAsync();
        var data = new DateTime(2025, 3, 4, 10, 20, 30);
        File.SetLastWriteTime(_a.Fisico("Fatture", "Fattura Rossi", "preventivo.txt"), data);

        await _backup.RipristinaAsync(await FaiBackupAsync(), _ripristino);

        var ripristinato = File.GetLastWriteTime(Path.Combine(_ripristino, "Fatture", "Fattura Rossi", "preventivo.txt"));
        Assert.InRange(Math.Abs((ripristinato - data).TotalSeconds), 0, 2);
    }

    [Fact]
    public async Task IlRipristino_ChiudeIlDatabase_LaCartellaSiPuoCancellare()
    {
        await CreaArchivioAsync();
        await _backup.RipristinaAsync(await FaiBackupAsync(), _ripristino);

        Directory.Delete(_ripristino, recursive: true); // se SQLite tenesse il file aperto, qui ci sarebbe un'eccezione

        Assert.False(Directory.Exists(_ripristino));
    }

    [Fact]
    public async Task IlProgresso_ArrivaAlNumeroDiFile()
    {
        await CreaArchivioAsync();
        var visti = new List<int>();

        var esito = await _backup.RipristinaAsync(await FaiBackupAsync(), _ripristino, new ProgressoSincrono(visti.Add));

        Assert.Equal(Enumerable.Range(1, esito.NumeroFile), visti);
    }

    private sealed class ProgressoSincrono(Action<int> azione) : IProgress<int>
    {
        public void Report(int valore) => azione(valore);
    }

    // ---------- Backup di una versione precedente ----------

    [Fact]
    public async Task UnBackupDiUnaVersionePrecedente_SiAggiornaAlRipristino()
    {
        // Un database com'era prima dell'archivio (senza la colonna "Archiviata"), dentro uno ZIP come quelli dei backup.
        var vecchio = _a.Tmp.Combina("vecchio.db");
        await using (var db = new AppDbContext(ArchivioDatabase.CreaOpzioni(vecchio)))
        {
            db.GetService<IMigrator>().Migrate("IndiceTestoPerDocumento");
            await db.Database.ExecuteSqlRawAsync("INSERT INTO Aree (Id, Nome, PercorsoRelativo, Ordine) VALUES (1, 'Fatture', 'Fatture', 0)");
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO Cartelle (Id, AreaId, Titolo, Ricorrenza, Completato, PercorsoRelativo, DataCreazione) " +
                "VALUES (7, 1, 'Pagata', 0, 1, 'Fatture/Pagata', '2026-09-01 10:00:00')");
        }
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var zip = _a.Tmp.Combina("vecchio.zip");
        using (var flusso = File.Create(zip))
        using (var archivio = new ZipArchive(flusso, ZipArchiveMode.Create))
        {
            archivio.CreateEntryFromFile(vecchio, "_dati/documentale.db");
            archivio.CreateEntry("Fatture/Pagata/");
        }

        var esito = await _backup.RipristinaAsync(zip, _ripristino);

        Assert.Equal(1, esito.NumeroFile);
        var factory = new AppDbContextFactory(ArchivioDatabase.CreaOpzioni(ArchivioDatabase.PercorsoDatabase(_ripristino)));
        var cartella = (await new ArchivioService(factory, new ArchivioFileService(_ripristino, usaCestino: false)).CaricaAlberoAsync())
            .Single().Cartelle.Single();
        Assert.Equal("Pagata", cartella.Titolo);
        Assert.True(cartella.Completato);
        Assert.False(cartella.Archiviata); // la colonna nuova c'è, con il valore di partenza
    }

    // ---------- Documenti mancanti ----------

    [Fact]
    public async Task UnDocumentoElencatoNelDatabaseMaAssenteNelBackup_SiSegnala()
    {
        await CreaArchivioAsync();
        var zip = await FaiBackupAsync();
        using (var archivio = ZipFile.Open(zip, ZipArchiveMode.Update))
            archivio.GetEntry("Fatture/Fattura Rossi/foto.jpg")!.Delete();

        var esito = await _backup.RipristinaAsync(zip, _ripristino);

        Assert.Equal(1, esito.DocumentiMancanti);
        Assert.Equal(2, esito.NumeroFile); // un documento e il database
    }

    // ---------- Leggere un backup ----------

    [Fact]
    public async Task LeggereUnBackup_DicequandoEStatoFatto_QuantiFileEQuantoPesa()
    {
        await CreaArchivioAsync();
        var zip = await FaiBackupAsync();

        var info = await _backup.LeggiBackupAsync(zip);

        Assert.Equal(zip, info.PercorsoZip);
        Assert.Equal(3, info.NumeroFile);
        Assert.True(info.DimensioneDecompressa > 0);
        Assert.NotNull(info.Data);
        Assert.InRange(Math.Abs((info.Data!.Value - DateTime.Now).TotalMinutes), 0, 10); // la data del database copiato
    }

    [Fact]
    public async Task LeggereUnBackup_NonEstraeNulla()
    {
        await CreaArchivioAsync();
        var zip = await FaiBackupAsync();

        await _backup.LeggiBackupAsync(zip);

        Assert.False(Directory.Exists(_ripristino));
    }

    [Fact]
    public async Task UnFileCheNonEUnoZip_NonESiLeggeComeBackup()
    {
        var finto = _a.Tmp.CreaFile("finto.zip", "non sono uno zip");

        var errore = await Assert.ThrowsAsync<ArchivioException>(() => _backup.LeggiBackupAsync(finto));

        Assert.Contains("non è un backup valido", errore.Message);
    }

    [Fact]
    public async Task UnoZipQualsiasi_SenzaIlDatabase_NonEUnBackupDiDocumentale()
    {
        var zip = _a.Tmp.Combina("foto.zip");
        using (var flusso = File.Create(zip))
        using (var archivio = new ZipArchive(flusso, ZipArchiveMode.Create))
        using (var scrittore = new StreamWriter(archivio.CreateEntry("vacanze/foto.jpg").Open()))
            scrittore.Write("x");

        var errore = await Assert.ThrowsAsync<ArchivioException>(() => _backup.LeggiBackupAsync(zip));

        Assert.Contains("non è un backup di Documentale", errore.Message);
        Assert.Contains("_dati/documentale.db", errore.Message);
    }

    [Fact]
    public async Task UnFileCheNonEsiste_DaUnMessaggio()
    {
        var errore = await Assert.ThrowsAsync<ArchivioException>(() => _backup.LeggiBackupAsync(_a.Tmp.Combina("non-c-e.zip")));

        Assert.Contains("non esiste", errore.Message);
    }

    [Fact]
    public async Task IlDatabaseNelBackup_SiTrovaAncheConMaiuscoleEBarreDiverse()
    {
        var zip = _a.Tmp.Combina("windows.zip");
        using (var flusso = File.Create(zip))
        using (var archivio = new ZipArchive(flusso, ZipArchiveMode.Create))
        using (var scrittore = new StreamWriter(archivio.CreateEntry("_DATI\\Documentale.db").Open()))
            scrittore.Write("x");

        var info = await _backup.LeggiBackupAsync(zip);

        Assert.Equal(1, info.NumeroFile);
    }

    // ---------- Dove si può ripristinare ----------

    [Fact]
    public async Task NonSiPuoRipristinareDentroLArchivioAttuale()
    {
        await CreaArchivioAsync();
        var zip = await FaiBackupAsync();

        var errore = await Assert.ThrowsAsync<ArchivioException>(
            () => _backup.RipristinaAsync(zip, Path.Combine(_a.Radice, "Fatture", "nuova")));

        Assert.Contains("non può stare dentro l'archivio attuale", errore.Message);
        Assert.False(Directory.Exists(Path.Combine(_a.Radice, "Fatture", "nuova")));
    }

    [Fact]
    public async Task NonSiPuoRipristinareSuUnaCartellaCheContieneLArchivioAttuale()
    {
        await CreaArchivioAsync();
        var zip = await FaiBackupAsync();

        // La cartella che contiene l'archivio (qui la cartella temporanea del test) e l'archivio stesso.
        var errore1 = await Assert.ThrowsAsync<ArchivioException>(() => _backup.RipristinaAsync(zip, _a.Tmp.Percorso));
        var errore2 = await Assert.ThrowsAsync<ArchivioException>(() => _backup.RipristinaAsync(zip, _a.Radice));

        Assert.Contains("non può contenere l'archivio attuale", errore1.Message);
        Assert.Contains("non può stare dentro l'archivio attuale", errore2.Message);
    }

    [Fact]
    public async Task UnaCartellaNonVuota_SiRifiuta_SenzaToccarla()
    {
        await CreaArchivioAsync();
        var zip = await FaiBackupAsync();
        var occupata = _a.Tmp.Combina("occupata");
        Directory.CreateDirectory(occupata);
        File.WriteAllText(Path.Combine(occupata, "mio.txt"), "importante");

        var errore = await Assert.ThrowsAsync<ArchivioException>(() => _backup.RipristinaAsync(zip, occupata));

        Assert.Contains("non è vuota", errore.Message);
        Assert.Equal(["mio.txt"], Directory.GetFileSystemEntries(occupata).Select(Path.GetFileName));
        Assert.Equal("importante", File.ReadAllText(Path.Combine(occupata, "mio.txt")));
    }

    [Fact]
    public async Task UnFileAlPostoDellaCartella_SiRifiuta()
    {
        await CreaArchivioAsync();
        var zip = await FaiBackupAsync();
        var file = _a.Tmp.CreaFile("sono-un-file.txt", "x");

        var errore = await Assert.ThrowsAsync<ArchivioException>(() => _backup.RipristinaAsync(zip, file));

        Assert.Contains("è un file", errore.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UnaDestinazioneVuota_SiRifiuta(string destinazione)
    {
        await CreaArchivioAsync();
        var zip = await FaiBackupAsync();

        var errore = await Assert.ThrowsAsync<ArchivioException>(() => _backup.RipristinaAsync(zip, destinazione));

        Assert.Contains("Scegli la cartella", errore.Message);
    }

    [Fact]
    public async Task UnPercorsoNonValido_SiRifiuta()
    {
        await CreaArchivioAsync();
        var zip = await FaiBackupAsync();

        await Assert.ThrowsAsync<ArchivioException>(() => _backup.RipristinaAsync(zip, "E:\\cartella\0strana"));
    }

    [Fact]
    public async Task UnaCartellaEsistenteMaVuota_VaBene_ENonSiCancella()
    {
        await CreaArchivioAsync();
        Directory.CreateDirectory(_ripristino);

        await _backup.RipristinaAsync(await FaiBackupAsync(), _ripristino);

        Assert.True(Directory.Exists(Path.Combine(_ripristino, "Fatture")));
    }

    [Fact]
    public async Task UnaDestinazioneAnnidataInCartelleCheNonCerano_SiCrea()
    {
        await CreaArchivioAsync();
        var annidata = Path.Combine(_ripristino, "anno 2026", "ottobre");

        var esito = await _backup.RipristinaAsync(await FaiBackupAsync(), annidata);

        Assert.True(File.Exists(Path.Combine(annidata, "_dati", "documentale.db")));
        Assert.Equal(annidata, esito.Cartella);
    }

    [Fact]
    public async Task UnBackupNonValido_NonCreaNemmenoLaCartella()
    {
        var finto = _a.Tmp.CreaFile("finto.zip", "non sono uno zip");

        await Assert.ThrowsAsync<ArchivioException>(() => _backup.RipristinaAsync(finto, _ripristino));

        Assert.False(Directory.Exists(_ripristino)); // il controllo viene prima di toccare il disco
    }

    // ---------- Quando qualcosa va storto non resta niente a metà ----------

    [Fact]
    public async Task UnBackupDanneggiato_DaUnMessaggio_ELaCartellaSparisce()
    {
        await CreaArchivioAsync();
        var zip = await FaiBackupAsync();
        // Si rovina il contenuto compresso di un documento, lasciando intatto l'elenco dei file.
        var byteZip = await File.ReadAllBytesAsync(zip);
        for (var i = 100; i < 140; i++)
            byteZip[i] ^= 0xFF;
        await File.WriteAllBytesAsync(zip, byteZip);

        var errore = await Record.ExceptionAsync(() => _backup.RipristinaAsync(zip, _ripristino));

        Assert.NotNull(errore);
        Assert.False(Directory.Exists(_ripristino));
    }

    [Fact]
    public async Task UnDatabaseRovinatoNelBackup_SiRifiuta_ELaCartellaSparisce()
    {
        var zip = _a.Tmp.Combina("rovinato.zip");
        using (var flusso = File.Create(zip))
        using (var archivio = new ZipArchive(flusso, ZipArchiveMode.Create))
        {
            using (var scrittore = new BinaryWriter(archivio.CreateEntry("_dati/documentale.db").Open()))
                scrittore.Write(new byte[4096].Select((_, i) => (byte)(i * 31)).ToArray());
            archivio.CreateEntry("Fatture/");
        }

        var errore = await Assert.ThrowsAsync<ArchivioException>(() => _backup.RipristinaAsync(zip, _ripristino));

        Assert.Contains("database", errore.Message);
        Assert.False(Directory.Exists(_ripristino));
    }

    [Fact]
    public async Task UnaCartellaCheCeraGiaVuota_DopoUnErrore_TornaVuota()
    {
        var zip = _a.Tmp.Combina("rovinato.zip");
        using (var flusso = File.Create(zip))
        using (var archivio = new ZipArchive(flusso, ZipArchiveMode.Create))
        {
            using (var scrittore = new BinaryWriter(archivio.CreateEntry("_dati/documentale.db").Open()))
                scrittore.Write(new byte[4096].Select((_, i) => (byte)(i * 31)).ToArray());
            using var documento = new StreamWriter(archivio.CreateEntry("Fatture/a.pdf").Open());
            documento.Write("x");
        }
        Directory.CreateDirectory(_ripristino);

        await Assert.ThrowsAsync<ArchivioException>(() => _backup.RipristinaAsync(zip, _ripristino));

        Assert.True(Directory.Exists(_ripristino)); // la cartella c'era e resta
        Assert.Empty(Directory.GetFileSystemEntries(_ripristino));
    }

    [Fact]
    public async Task UnBackupCostruitoPerScrivereFuoriDallaCartella_SiRifiuta_SenzaScrivereNulla()
    {
        var zip = _a.Tmp.Combina("attacco.zip");
        using (var flusso = File.Create(zip))
        using (var archivio = new ZipArchive(flusso, ZipArchiveMode.Create))
        {
            using (var db = new StreamWriter(archivio.CreateEntry("_dati/documentale.db").Open()))
                db.Write("x");
            using var cattivo = new StreamWriter(archivio.CreateEntry("../cattivo.txt").Open());
            cattivo.Write("fuori dalla cartella");
        }

        var errore = await Assert.ThrowsAsync<ArchivioException>(() => _backup.RipristinaAsync(zip, _ripristino));

        Assert.Contains("percorso non valido", errore.Message);
        Assert.False(File.Exists(_a.Tmp.Combina("cattivo.txt")));
        Assert.False(Directory.Exists(_ripristino));
    }

    [Theory]
    [InlineData("/assoluto.txt")]
    [InlineData("C:/Windows/sistema.txt")]
    [InlineData("a/../../fuori.txt")]
    public async Task UnPercorsoAssolutoOConPuntiPuntoNelBackup_SiRifiuta(string nomeVoce)
    {
        var zip = _a.Tmp.Combina("strano.zip");
        using (var flusso = File.Create(zip))
        using (var archivio = new ZipArchive(flusso, ZipArchiveMode.Create))
        {
            using (var db = new StreamWriter(archivio.CreateEntry("_dati/documentale.db").Open()))
                db.Write("x");
            using var voce = new StreamWriter(archivio.CreateEntry(nomeVoce).Open());
            voce.Write("x");
        }

        await Assert.ThrowsAsync<ArchivioException>(() => _backup.RipristinaAsync(zip, _ripristino));

        Assert.False(Directory.Exists(_ripristino));
        Assert.False(File.Exists(_a.Tmp.Combina("fuori.txt")));
    }

    [Fact]
    public async Task Annullando_NonRestaNiente()
    {
        await CreaArchivioAsync();
        var zip = await FaiBackupAsync();
        using var annullamento = new CancellationTokenSource();
        await annullamento.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _backup.RipristinaAsync(zip, _ripristino, null, annullamento.Token));

        Assert.False(Directory.Exists(_ripristino));
    }

    [Fact]
    public async Task IlBackupPuoEssereRipristinatoPiuVolte_InCartelleDiverse()
    {
        await CreaArchivioAsync();
        var zip = await FaiBackupAsync();

        var uno = await _backup.RipristinaAsync(zip, _a.Tmp.Combina("uno"));
        var due = await _backup.RipristinaAsync(zip, _a.Tmp.Combina("due"));

        Assert.NotEqual(uno.Cartella, due.Cartella);
        Assert.True(File.Exists(Path.Combine(uno.Cartella, "_dati", "documentale.db")));
        Assert.True(File.Exists(Path.Combine(due.Cartella, "_dati", "documentale.db")));
    }
}
