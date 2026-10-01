using System.IO.Compression;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;

namespace DocumentaleMarta.Tests;

public class BackupServiceTests : IDisposable
{
    private static readonly DateTime Adesso = new(2026, 10, 1, 14, 30, 0);

    private readonly ArchivioDiProva _a;
    private readonly BackupService _backup;
    private readonly string _destinazione;
    private readonly string _temporanea;

    public BackupServiceTests()
    {
        var estrattore = new FintoEstrattore(".txt")
        {
            Logica = (p, _) => Task.FromResult("Preventivo carpenteria zincata per il cancello Rossi")
        };
        _a = new ArchivioDiProva(estrattore);
        _temporanea = _a.Tmp.Combina("temporanea");
        Directory.CreateDirectory(_temporanea);
        _backup = new BackupService(_a.Factory, _a.Files, () => Adesso, _temporanea);
        _destinazione = _a.Tmp.Combina("backup");
    }

    public void Dispose() => _a.Dispose();

    private async Task CreaArchivioAsync()
    {
        var fatture = await _a.Servizio.CreaAreaAsync("Fatture");
        await _a.Servizio.CreaAreaAsync("Area vuota");
        await _a.CreaCartellaInAreaAsync(fatture, "Fattura Rossi", ["preventivo.txt", "foto.jpg"], new DateOnly(2026, 10, 20), "Per il cancello");
        await _a.CreaCartellaInAreaAsync(fatture, "Cartella senza documenti", []);
        await _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));
    }

    private static List<string> Voci(string zip)
    {
        using var archivio = ZipFile.OpenRead(zip);
        return archivio.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToList();
    }

    // ---------- Contenuto ----------

    [Fact]
    public async Task IlBackup_ContieneDocumenti_Database_EIstruzioni()
    {
        await CreaArchivioAsync();

        var esito = await _backup.CreaBackupAsync(_destinazione);

        Assert.Equal(Path.Combine(_destinazione, "Documentale-backup-2026-10-01-1430.zip"), esito.PercorsoZip);
        Assert.Equal(Adesso, esito.Data);
        Assert.Equal(new FileInfo(esito.PercorsoZip).Length, esito.Dimensione);
        Assert.Equal(
        [
            "Area vuota/",
            "Fatture/Cartella senza documenti/",
            "Fatture/Fattura Rossi/foto.jpg",
            "Fatture/Fattura Rossi/preventivo.txt",
            BackupService.NomeLeggimi,
            "_dati/documentale.db"
        ], Voci(esito.PercorsoZip));
        Assert.Equal(3, esito.NumeroFile); // due documenti e il database
    }

    [Fact]
    public async Task LeIstruzioni_SpieganoComeRipristinare()
    {
        await CreaArchivioAsync();
        var esito = await _backup.CreaBackupAsync(_destinazione);

        using var zip = ZipFile.OpenRead(esito.PercorsoZip);
        using var lettore = new StreamReader(zip.GetEntry(BackupService.NomeLeggimi)!.Open());
        var testo = await lettore.ReadToEndAsync();

        Assert.Contains("01/10/2026", testo);
        Assert.Contains("14:30", testo);
        Assert.Contains("PercorsoRadice", testo);
        Assert.Contains("Chiudi Documentale", testo);
    }

    [Fact]
    public async Task IlDatabaseVivoENonSuoiFileAccessori_NonSiCopianoCosiCome()
    {
        await CreaArchivioAsync();
        File.WriteAllText(Path.Combine(_a.Radice, "_dati", "documentale.db-journal"), "residuo");

        var esito = await _backup.CreaBackupAsync(_destinazione);

        Assert.Equal(["_dati/documentale.db"], Voci(esito.PercorsoZip).Where(v => v.StartsWith("_dati/")));
    }

    [Fact]
    public async Task ConUnArchivioVuoto_IlBackupContieneSoloIlDatabase()
    {
        var esito = await _backup.CreaBackupAsync(_destinazione);

        Assert.Equal(1, esito.NumeroFile);
        Assert.Equal([BackupService.NomeLeggimi, "_dati/documentale.db"], Voci(esito.PercorsoZip));
    }

    [Fact]
    public async Task LaDataDelFileSiConserva()
    {
        await CreaArchivioAsync();
        var fisico = _a.Fisico("Fatture", "Fattura Rossi", "preventivo.txt");
        var data = new DateTime(2025, 3, 4, 10, 20, 30);
        File.SetLastWriteTime(fisico, data);

        var esito = await _backup.CreaBackupAsync(_destinazione);

        using var zip = ZipFile.OpenRead(esito.PercorsoZip);
        var voce = zip.GetEntry("Fatture/Fattura Rossi/preventivo.txt")!;
        Assert.InRange(Math.Abs((voce.LastWriteTime.LocalDateTime - data).TotalSeconds), 0, 2); // lo ZIP ha risoluzione di 2 secondi
    }

    // ---------- Ripristino: lo ZIP si riapre come archivio ----------

    [Fact]
    public async Task ILBackup_EstrattoInUnaNuovaCartella_SiApreComeArchivio_ConAlberoDocumentiERicerca()
    {
        await CreaArchivioAsync();
        var originale = await _a.Servizio.CaricaAlberoAsync();
        var esito = await _backup.CreaBackupAsync(_destinazione);

        var ripristino = _a.Tmp.Combina("ripristino");
        ZipFile.ExtractToDirectory(esito.PercorsoZip, ripristino);

        var files = new ArchivioFileService(ripristino, usaCestino: false);
        var factory = new AppDbContextFactory(ArchivioDatabase.CreaOpzioni(ArchivioDatabase.PercorsoDatabase(ripristino)));
        var archivio = new ArchivioService(factory, files);
        var ricostruito = await archivio.CaricaAlberoAsync();

        Assert.Equal(
            originale.Select(a => (a.Nome, Cartelle: a.Cartelle.Select(c => (c.Titolo, c.NumeroDocumenti, c.DataScadenza)).ToList())),
            ricostruito.Select(a => (a.Nome, Cartelle: a.Cartelle.Select(c => (c.Titolo, c.NumeroDocumenti, c.DataScadenza)).ToList())));

        // I documenti sono dove il database dice che sono, con lo stesso contenuto.
        var documenti = await archivio.CaricaDocumentiAsync(null);
        Assert.Equal(2, documenti.Count);
        Assert.All(documenti, d => Assert.True(files.Esiste(d.PercorsoRelativo), d.PercorsoRelativo));
        Assert.Equal(
            File.ReadAllText(_a.Fisico("Fatture", "Fattura Rossi", "preventivo.txt")),
            File.ReadAllText(files.PercorsoAssoluto(documenti.Single(d => d.NomeFile == "preventivo.txt").PercorsoRelativo)));

        // Anche l'indice del testo è arrivato: la ricerca nel contenuto funziona sul backup.
        var trovati = await new RicercaService(factory).CercaAsync("zincata");
        Assert.Contains(trovati.Risultati, r => r.Documento.NomeFile == "preventivo.txt");
    }

    [Fact]
    public async Task IlDatabaseDelBackup_HaLeModificheAppenaFatte()
    {
        await CreaArchivioAsync();
        var cartellaId = (await _a.Servizio.CaricaAlberoAsync()).Single(a => a.Nome == "Fatture").Cartelle.First().Id;
        await _a.Servizio.AllegaDocumentiAsync(cartellaId, [_a.Tmp.CreaFile("nuovo/appena-aggiunto.txt", "fresco")]);

        var esito = await _backup.CreaBackupAsync(_destinazione);

        var ripristino = _a.Tmp.Combina("ripristino");
        ZipFile.ExtractToDirectory(esito.PercorsoZip, ripristino);
        var factory = new AppDbContextFactory(ArchivioDatabase.CreaOpzioni(ArchivioDatabase.PercorsoDatabase(ripristino)));
        var documenti = await new ArchivioService(factory, new ArchivioFileService(ripristino, usaCestino: false)).CaricaDocumentiAsync(null);
        Assert.Contains(documenti, d => d.NomeFile == "appena-aggiunto.txt");
    }

    // ---------- Cosa succede intorno ----------

    [Fact]
    public async Task LaCartellaDiDestinazione_SeNonEsiste_SiCrea()
    {
        await CreaArchivioAsync();
        var annidata = Path.Combine(_destinazione, "anno 2026", "ottobre");

        var esito = await _backup.CreaBackupAsync(annidata);

        Assert.True(File.Exists(esito.PercorsoZip));
        Assert.Equal(annidata, Path.GetDirectoryName(esito.PercorsoZip));
    }

    [Fact]
    public async Task DueBackupNelloStessoMinuto_NonSiSovrascrivono()
    {
        await CreaArchivioAsync();

        var primo = await _backup.CreaBackupAsync(_destinazione);
        var secondo = await _backup.CreaBackupAsync(_destinazione);

        Assert.NotEqual(primo.PercorsoZip, secondo.PercorsoZip);
        Assert.Equal("Documentale-backup-2026-10-01-1430 (1).zip", Path.GetFileName(secondo.PercorsoZip));
        Assert.True(File.Exists(primo.PercorsoZip));
    }

    [Fact]
    public async Task NonRestanoFileTemporanei_NeInDestinazioneNeInTemp()
    {
        await CreaArchivioAsync();

        await _backup.CreaBackupAsync(_destinazione);

        Assert.Equal(["Documentale-backup-2026-10-01-1430.zip"], Directory.GetFiles(_destinazione).Select(Path.GetFileName));
        Assert.Empty(Directory.GetFileSystemEntries(_temporanea)); // la copia temporanea del database è sparita
    }

    [Fact]
    public async Task IlProgresso_ArrivaAlNumeroDiFile()
    {
        await CreaArchivioAsync();
        var visti = new List<int>();

        var esito = await _backup.CreaBackupAsync(_destinazione, new ProgressoSincrono(visti.Add));

        Assert.Equal(Enumerable.Range(1, esito.NumeroFile), visti);
    }

    private sealed class ProgressoSincrono(Action<int> azione) : IProgress<int>
    {
        public void Report(int valore) => azione(valore);
    }

    // ---------- Destinazioni non valide ----------

    [Fact]
    public async Task UnaDestinazioneDentroLArchivio_SiRifiuta()
    {
        var errore = await Assert.ThrowsAsync<ArchivioException>(
            () => _backup.CreaBackupAsync(Path.Combine(_a.Radice, "Fatture")));

        Assert.Contains("non può stare dentro l'archivio", errore.Message);
    }

    [Fact]
    public async Task LaRadiceStessa_SiRifiuta_AncheConUnaBarraInFondo() =>
        await Assert.ThrowsAsync<ArchivioException>(() => _backup.CreaBackupAsync(_a.Radice + Path.DirectorySeparatorChar));

    [Fact]
    public async Task UnaCartellaVicinaALaRadice_ConLoStessoInizioDelNome_EValida()
    {
        // "Documentale-backup" non è dentro "Documentale": il confronto deve guardare le cartelle, non le lettere.
        await CreaArchivioAsync();

        var esito = await _backup.CreaBackupAsync(_a.Radice + "-backup");

        Assert.True(File.Exists(esito.PercorsoZip));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UnaDestinazioneVuota_SiRifiuta(string destinazione)
    {
        var errore = await Assert.ThrowsAsync<ArchivioException>(() => _backup.CreaBackupAsync(destinazione));

        Assert.Contains("Scegli la cartella", errore.Message);
    }

    [Fact]
    public async Task UnPercorsoNonValido_SiRifiutaConUnMessaggio() =>
        await Assert.ThrowsAsync<ArchivioException>(() => _backup.CreaBackupAsync("C:\\cartella\0strana"));

    // ---------- Quando qualcosa va storto ----------

    [Fact]
    public async Task UnDocumentoAperto_InUnAltroProgramma_SiCopiaLoStesso()
    {
        await CreaArchivioAsync();
        using var aperto = new FileStream(
            _a.Fisico("Fatture", "Fattura Rossi", "preventivo.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        var esito = await _backup.CreaBackupAsync(_destinazione);

        Assert.Contains("Fatture/Fattura Rossi/preventivo.txt", Voci(esito.PercorsoZip));
    }

    [Fact]
    public async Task UnDocumentoBloccatoInEsclusiva_FaFallireIlBackup_SenzaLasciareNulla()
    {
        await CreaArchivioAsync();
        using var bloccato = new FileStream(
            _a.Fisico("Fatture", "Fattura Rossi", "foto.jpg"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        await Assert.ThrowsAsync<IOException>(() => _backup.CreaBackupAsync(_destinazione));

        Assert.Empty(Directory.GetFiles(_destinazione));
    }

    [Fact]
    public async Task AnnullandoIlBackup_NonRestaNessunFile()
    {
        await CreaArchivioAsync();
        using var annullamento = new CancellationTokenSource();
        await annullamento.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _backup.CreaBackupAsync(_destinazione, null, annullamento.Token));

        Assert.True(!Directory.Exists(_destinazione) || Directory.GetFiles(_destinazione).Length == 0);
    }

    [Fact]
    public async Task ILArchivio_NonCambiaDopoUnBackup()
    {
        await CreaArchivioAsync();
        var prima = Directory.EnumerateFiles(_a.Radice, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("_dati")).Order().ToList();

        await _backup.CreaBackupAsync(_destinazione);

        Assert.Equal(prima, Directory.EnumerateFiles(_a.Radice, "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains("_dati")).Order());
        Assert.Equal(2, (await _a.Servizio.CaricaDocumentiAsync(null)).Count);
    }
}
