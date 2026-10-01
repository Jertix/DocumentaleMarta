using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;

namespace DocumentaleMarta.Tests;

/// <summary>Il pulsante "Ripristina…": scelta del backup, della cartella, conferma, ripristino e passaggio al nuovo archivio.</summary>
public class RipristinoViewModelTests : IDisposable
{
    private static readonly DateTime Oggi = new(2026, 10, 1, 9, 0, 0);

    private readonly ArchivioDiProva _a = new();
    private readonly ImpostazioniService _servizio;
    private readonly ImpostazioniApp _impostazioni;
    private readonly FintoBackup _backup = new();
    private readonly string _madre;
    private const string NomeCartella = "Documentale ripristinato 2026-10-01";

    public RipristinoViewModelTests()
    {
        _servizio = new ImpostazioniService(_a.Tmp.Combina("impostazioni", "impostazioni.json"));
        _impostazioni = new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false };
        _madre = _a.Tmp.Combina("dischetto");
    }

    public void Dispose() => _a.Dispose();

    private string Destinazione => Path.Combine(_madre, NomeCartella);

    private MainViewModel Principale(IBackupService? backup = null, ImpostazioniService? servizio = null, bool senzaBackup = false) => new(
        _a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni,
        new AlertService(_impostazioni, new TempoFisso(Oggi)),
        servizioImpostazioni: servizio ?? _servizio,
        servizioBackup: senzaBackup ? null : backup ?? _backup);

    private ImpostazioniApp DalFile() => new ImpostazioniService(_servizio.PercorsoFile).Carica();

    /// <summary>L'utente sceglie il file e la cartella, e risponde "sì" alle domande (a meno che indicato).</summary>
    private void Scelte(string zip = @"E:\Backup\backup.zip", bool conferma = true, bool usaSubito = true)
    {
        _a.Dialog.RispondiFileBackup(zip);
        _a.Dialog.RispondiCartella(_madre);
        _a.Dialog.RispondiDomande(conferma, usaSubito);
    }

    // ---------- Il percorso normale ----------

    [Fact]
    public async Task ScegliendoBackupECartella_SiRipristinaInUnaCartellaNuova_EsiPassaAlNuovoArchivio()
    {
        var vm = Principale();
        _impostazioni.CartellaBackup = @"E:\Backup";
        Scelte();

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        Assert.Equal([(@"E:\Backup\backup.zip", Destinazione)], _backup.Ripristini);
        // Si parte dalla cartella dei backup per scegliere il file, e dalla cartella che contiene l'archivio per scegliere dove.
        Assert.Equal([@"E:\Backup"], _a.Dialog.SceltaFileBackupChiesta);
        Assert.Equal(Path.GetDirectoryName(_a.Radice), _a.Dialog.CartellaInizialeChiesta);
        // Le impostazioni si salvano nel file con la nuova radice; quelle in uso cambiano solo al riavvio.
        Assert.Equal(Destinazione, DalFile().PercorsoRadice);
        Assert.Equal(_a.Radice, _impostazioni.PercorsoRadice);
        Assert.Equal(1, _a.Shell.RiavviiRichiesti);
        Assert.Empty(_a.Dialog.Errori);
        Assert.Empty(_a.Dialog.Messaggi);
    }

    [Fact]
    public async Task LaConferma_DiceCheBackupEEDoveVa_EChel_ArchivioAttualeNonSiTocca()
    {
        _backup.InfoLetta = new("x.zip", new DateTime(2026, 9, 12, 14, 30, 0), 12, 3L * 1024 * 1024);
        var vm = Principale();
        Scelte();

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        var conferma = _a.Dialog.Domande[0];
        Assert.Contains("del 12/09/2026 alle 14:30", conferma);
        Assert.Contains("12 file", conferma);
        Assert.Contains("3 MB", conferma);
        Assert.Contains(Destinazione, conferma);
        Assert.Contains($"L'archivio attuale ({_a.Radice}) non viene toccato", conferma);
    }

    [Fact]
    public async Task UnBackupSenzaData_NonLaScrive()
    {
        _backup.InfoLetta = new("x.zip", null, 3, 2048);
        var vm = Principale();
        Scelte();

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        Assert.DoesNotContain("del ", _a.Dialog.Domande[0].Split('\n')[0]);
        Assert.Contains("3 file", _a.Dialog.Domande[0]);
    }

    [Fact]
    public async Task LaDomandaFinale_DiceCheIlProgrammaSiRiavvia_EChelArchivioAttualeResta()
    {
        var vm = Principale();
        Scelte();

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        var domanda = _a.Dialog.Domande[1];
        Assert.Contains($"ripristinato in:\n{Destinazione}", domanda);
        Assert.Contains("Documentale si riavvia", domanda);
        Assert.Contains("L'archivio attuale resta dov'è", domanda);
    }

    [Fact]
    public async Task SeLaCartellaCeGia_SiAggiungeUnNumero_NienteSiMescola()
    {
        Directory.CreateDirectory(Destinazione);
        var vm = Principale();
        Scelte();

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        Assert.Equal(Path.Combine(_madre, NomeCartella + " (1)"), _backup.Ripristini.Single().Destinazione);
    }

    // ---------- Quando l'utente non usa subito il nuovo archivio ----------

    [Fact]
    public async Task RispondendoNoAllaDomandaFinale_NonSiCambiaNulla_ESiSpiegaCosaFare()
    {
        var vm = Principale();
        Scelte(usaSubito: false);

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        Assert.Equal(0, _a.Shell.RiavviiRichiesti);
        Assert.False(File.Exists(_servizio.PercorsoFile));
        var (titolo, messaggio) = Assert.Single(_a.Dialog.Messaggi);
        Assert.Equal("Ripristino completato", titolo);
        Assert.Contains(Destinazione, messaggio);
        Assert.Contains("PercorsoRadice", messaggio);
        Assert.Contains(_servizio.PercorsoFile, messaggio);
    }

    [Fact]
    public async Task SenzaIlServizioDelleImpostazioni_NonSiPuoCambiareArchivio_ESiSpiegaComeFare()
    {
        var vm = new MainViewModel(
            _a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni, new AlertService(_impostazioni, new TempoFisso(Oggi)),
            servizioBackup: _backup);
        _a.Dialog.RispondiFileBackup(@"E:\b.zip");
        _a.Dialog.RispondiCartella(_madre);
        _a.Dialog.RispondiDomande(true);

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        Assert.Single(_a.Dialog.Domande); // solo la conferma iniziale: non c'è niente da proporre dopo
        Assert.Equal(0, _a.Shell.RiavviiRichiesti);
        Assert.Contains("Ripristino completato", Assert.Single(_a.Dialog.Messaggi).Titolo);
    }

    // ---------- Avvisi sui file mancanti ----------

    [Theory]
    [InlineData(1, "Attenzione: 1 documento elencato nel database non ha il suo file nel backup.")]
    [InlineData(4, "Attenzione: 4 documenti elencati nel database non hanno il loro file nel backup.")]
    public async Task IDocumentiMancanti_SiDicono_AlSingolareEAlPlurale(int mancanti, string atteso)
    {
        _backup.DocumentiMancanti = mancanti;
        var vm = Principale();
        Scelte();

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        Assert.Contains(atteso, _a.Dialog.Domande[1]);
    }

    [Fact]
    public async Task SeNonManca_NienteAttenzione()
    {
        var vm = Principale();
        Scelte();

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        Assert.DoesNotContain("Attenzione", _a.Dialog.Domande[1]);
    }

    // ---------- Rinunce ----------

    [Fact]
    public async Task AnnullandoLaScelta_DelFile_NonSiFaNulla()
    {
        var vm = Principale();
        _a.Dialog.RispondiFileBackup(null);

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        Assert.Empty(_backup.Lette);
        Assert.Empty(_a.Dialog.SceltaCartellaChiesta);
        Assert.Empty(_backup.Ripristini);
    }

    [Fact]
    public async Task AnnullandoLaScelta_DellaCartella_NonSiFaNulla()
    {
        var vm = Principale();
        _a.Dialog.RispondiFileBackup(@"E:\b.zip");
        _a.Dialog.RispondiCartella(null);

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        Assert.Empty(_a.Dialog.Domande);
        Assert.Empty(_backup.Ripristini);
    }

    [Fact]
    public async Task RispondendoNoAllaConferma_NonSiRipristinaNulla()
    {
        var vm = Principale();
        Scelte(conferma: false);

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        Assert.Single(_a.Dialog.Domande);
        Assert.Empty(_backup.Ripristini);
        Assert.Equal(0, _a.Shell.RiavviiRichiesti);
        Assert.False(Directory.Exists(Destinazione));
    }

    // ---------- Errori ----------

    [Fact]
    public async Task UnFileCheNonEUnBackup_DiceloSubito_SenzaChiedereDove()
    {
        _backup.ErroreLettura = new ArchivioException("Questo file non è un backup di Documentale.");
        var vm = Principale();
        _a.Dialog.RispondiFileBackup(@"E:\foto.zip");

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        Assert.Equal("Questo file non è un backup di Documentale.", Assert.Single(_a.Dialog.Errori));
        Assert.Empty(_a.Dialog.SceltaCartellaChiesta);
        Assert.Empty(_backup.Ripristini);
    }

    [Fact]
    public async Task UnFileIllegibile_DaUnMessaggio()
    {
        _backup.ErroreLettura = new IOException("Accesso negato.");
        var vm = Principale();
        _a.Dialog.RispondiFileBackup(@"E:\b.zip");

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        Assert.Contains("Non è stato possibile leggere il file: Accesso negato.", Assert.Single(_a.Dialog.Errori));
    }

    [Fact]
    public async Task UnRipristinoRifiutato_MostraIlMotivo_ENonProponeDiUsarlo()
    {
        _backup.ErroreRipristino = new ArchivioException("La cartella non è vuota.");
        var vm = Principale();
        Scelte();

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        Assert.Equal("La cartella non è vuota.", Assert.Single(_a.Dialog.Errori));
        Assert.Single(_a.Dialog.Domande); // solo la conferma: la domanda finale non c'è
        Assert.Equal(0, _a.Shell.RiavviiRichiesti);
        Assert.False(File.Exists(_servizio.PercorsoFile));
        Assert.Equal("", vm.TestoBackup);
    }

    [Fact]
    public async Task UnDiscoPieno_DiceCheNonEStatoCreatoNulla()
    {
        _backup.ErroreRipristino = new IOException("Spazio su disco insufficiente.");
        var vm = Principale();
        Scelte();

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        var errore = Assert.Single(_a.Dialog.Errori);
        Assert.Contains("Il ripristino non è riuscito: Spazio su disco insufficiente.", errore);
        Assert.Contains("Non è stato creato nulla", errore);
        Assert.Equal(0, _a.Shell.RiavviiRichiesti);
    }

    [Fact]
    public async Task SeLeImpostazioniNonSiSalvano_NonSiRiavvia_ESiDiceCosaFareAMano()
    {
        var bloccante = _a.Tmp.CreaFile("bloccante", "sono un file");
        var servizio = new ImpostazioniService(Path.Combine(bloccante, "impostazioni.json"));
        var vm = Principale(servizio: servizio);
        Scelte();

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        var errore = Assert.Single(_a.Dialog.Errori);
        Assert.Contains($"ripristinato in:\n{Destinazione}", errore);
        Assert.Contains("non è stato possibile salvare le impostazioni", errore);
        Assert.Contains("PercorsoRadice", errore);
        Assert.Equal(0, _a.Shell.RiavviiRichiesti);
    }

    [Fact]
    public async Task SeLeImpostazioniDiventerebberoNonValide_NonSiCambiaArchivio()
    {
        // La cartella dei backup starebbe dentro il nuovo archivio: all'avvio il programma non partirebbe.
        _impostazioni.CartellaBackup = Path.Combine(Destinazione, "copie");
        var vm = Principale();
        Scelte();

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        var errore = Assert.Single(_a.Dialog.Errori);
        Assert.Contains("non si può usare subito", errore);
        Assert.Contains("non può stare dentro l'archivio", errore);
        Assert.Equal(0, _a.Shell.RiavviiRichiesti);
        Assert.False(File.Exists(_servizio.PercorsoFile)); // il file non è stato toccato
    }

    // ---------- Durante il ripristino ----------

    [Fact]
    public async Task MentreLavora_MostraLAvanzamento_BloccaIlBackup_ELoStatoTornaPulito()
    {
        var vm = Principale();
        Scelte();
        string? testoDurante = null;
        _backup.DuranteIlRipristino = async (_, _, avanzamento) =>
        {
            await Task.Yield();
            avanzamento!.Report(7);
            // L'aggiornamento torna sul contesto dell'interfaccia: si lascia il tempo di arrivare.
            for (var i = 0; i < 200 && !vm.TestoBackup.EndsWith("7 file"); i++)
                await Task.Delay(10);
            testoDurante = vm.TestoBackup;
            await vm.EseguiBackupCommand.ExecuteAsync(null); // un backup durante un ripristino non parte
        };

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        Assert.Equal("Ripristino in corso… 7 file", testoDurante);
        Assert.Empty(_backup.Cartelle); // il backup non è partito
        Assert.Equal("", vm.TestoBackup);
    }

    [Fact]
    public async Task UnRipristinoNonParteSeUnBackupELavora()
    {
        var vm = Principale();
        _impostazioni.CartellaBackup = @"E:\Backup";
        var via = new TaskCompletionSource();
        _backup.DuranteIlBackup = (_, _) => via.Task;

        var inCorso = vm.EseguiBackupCommand.ExecuteAsync(null);
        Scelte();
        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);
        via.SetResult();
        await inCorso;

        Assert.Empty(_backup.Ripristini);
        Assert.Empty(_a.Dialog.SceltaFileBackupChiesta); // nemmeno la scelta del file
    }

    // ---------- Quando il pulsante c'è ----------

    [Fact]
    public async Task SenzaIlServizioDiBackup_IlComandoNonFunziona()
    {
        var vm = Principale(senzaBackup: true);

        Assert.False(vm.RipristinaDaBackupCommand.CanExecute(null));
        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);
        Assert.Empty(_a.Dialog.SceltaFileBackupChiesta);
    }

    // ---------- Con il servizio vero, di punta a punta ----------

    [Fact]
    public async Task ConIlServizioVero_BackupERipristinoDalProgramma_PortanoAUnArchivioIdentico_ESalvanoLeImpostazioni()
    {
        // Un archivio con qualcosa dentro e il suo backup, fatti con il servizio vero.
        var area = await _a.Servizio.CreaAreaAsync("Fatture");
        var c = await _a.CreaCartellaInAreaAsync(area, "Fattura 123", ["uno.pdf", "due.pdf"], new DateOnly(2026, 11, 5), "Nota");
        await _a.Servizio.AggiornaCartellaAsync(c.Id, c.Dati with { Completato = true, DataCompletamento = new DateOnly(2026, 10, 1) });
        await _a.Servizio.ArchiviaCartellaAsync(c.Id);
        var servizioVero = new BackupService(_a.Factory, _a.Files);
        var zip = (await servizioVero.CreaBackupAsync(_a.Tmp.Combina("backup"))).PercorsoZip;

        var vm = Principale(servizioVero);
        Scelte(zip);
        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        Assert.Empty(_a.Dialog.Errori);
        Assert.Equal(1, _a.Shell.RiavviiRichiesti);

        // La nuova radice si apre come si aprirebbe all'avvio, e ha lo stesso contenuto.
        var nuovaRadice = DalFile().PercorsoRadice;
        Assert.Equal(Destinazione, nuovaRadice);
        var percorsoDatabase = ArchivioDatabase.Inizializza(nuovaRadice);
        var nuovo = new ArchivioService(
            new AppDbContextFactory(ArchivioDatabase.CreaOpzioni(percorsoDatabase)), new ArchivioFileService(nuovaRadice, usaCestino: false));
        var cartella = (await nuovo.CaricaAlberoAsync()).Single().Cartelle.Single();
        Assert.Equal(("Fattura 123", 2, true, true), (cartella.Titolo, cartella.NumeroDocumenti, cartella.Completato, cartella.Archiviata));
        Assert.True(File.Exists(Path.Combine(nuovaRadice, "Fatture", "Fattura 123", "uno.pdf")));
        // E quello vecchio è rimasto com'era.
        Assert.Single((await _a.Servizio.CaricaAlberoAsync()).Single().Cartelle);
        Assert.True(File.Exists(_a.Fisico("Fatture", "Fattura 123", "uno.pdf")));
    }
}
