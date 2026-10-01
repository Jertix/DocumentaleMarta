using System.IO.Compression;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;

namespace DocumentaleMarta.Tests;

/// <summary>Un servizio di backup che non fa niente: ricorda le richieste e si comporta come il test vuole.</summary>
public class FintoBackup : IBackupService
{
    public List<string> Cartelle { get; } = [];
    public int FilePerBackup { get; set; } = 3;
    public Func<string, IProgress<int>?, Task>? DuranteIlBackup { get; set; }
    public Exception? Errore { get; set; }

    public async Task<EsitoBackup> CreaBackupAsync(string cartellaDestinazione, IProgress<int>? avanzamento = null, CancellationToken annullamento = default)
    {
        Cartelle.Add(cartellaDestinazione);
        if (DuranteIlBackup is not null)
            await DuranteIlBackup(cartellaDestinazione, avanzamento);
        if (Errore is not null)
            throw Errore;
        return new EsitoBackup(Path.Combine(cartellaDestinazione, "backup.zip"), FilePerBackup, 2048, new DateTime(2026, 10, 1, 9, 30, 0));
    }

    // ---------- Ripristino ----------

    /// <summary>Cosa "contiene" il backup letto.</summary>
    public InfoBackup InfoLetta { get; set; } = new("backup.zip", new DateTime(2026, 9, 12, 14, 30, 0), 12, 3L * 1024 * 1024);

    public Exception? ErroreLettura { get; set; }
    public List<string> Lette { get; } = [];

    public Task<InfoBackup> LeggiBackupAsync(string percorsoZip, CancellationToken annullamento = default)
    {
        Lette.Add(percorsoZip);
        if (ErroreLettura is not null)
            throw ErroreLettura;
        return Task.FromResult(InfoLetta with { PercorsoZip = percorsoZip });
    }

    public List<(string Zip, string Destinazione)> Ripristini { get; } = [];
    public Func<string, string, IProgress<int>?, Task>? DuranteIlRipristino { get; set; }
    public Exception? ErroreRipristino { get; set; }
    public int DocumentiMancanti { get; set; }

    public async Task<EsitoRipristino> RipristinaAsync(
        string percorsoZip, string cartellaDestinazione, IProgress<int>? avanzamento = null, CancellationToken annullamento = default)
    {
        Ripristini.Add((percorsoZip, cartellaDestinazione));
        if (DuranteIlRipristino is not null)
            await DuranteIlRipristino(percorsoZip, cartellaDestinazione, avanzamento);
        if (ErroreRipristino is not null)
            throw ErroreRipristino;
        return new EsitoRipristino(cartellaDestinazione, InfoLetta.NumeroFile, DocumentiMancanti);
    }
}

public class BackupViewModelTests : IDisposable
{
    private static readonly DateTime Oggi = new(2026, 10, 1, 9, 0, 0);

    private readonly ArchivioDiProva _a = new();
    private readonly ImpostazioniService _servizio;
    private readonly ImpostazioniApp _impostazioni;
    private readonly FintoBackup _backup = new();

    public BackupViewModelTests()
    {
        _servizio = new ImpostazioniService(_a.Tmp.Combina("impostazioni", "impostazioni.json"));
        _impostazioni = new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false };
    }

    public void Dispose() => _a.Dispose();

    private MainViewModel Principale(IBackupService? backup = null, ImpostazioniService? servizio = null, bool senzaBackup = false) => new(
        _a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni,
        new AlertService(_impostazioni, new TempoFisso(Oggi)),
        servizioImpostazioni: servizio ?? _servizio,
        servizioBackup: senzaBackup ? null : backup ?? _backup);

    private async Task<MainViewModel> ConDocumentiAsync(IBackupService? backup = null, bool senzaBackup = false)
    {
        await _a.CreaCartellaAsync("Fatture", "Fattura", "a.pdf");
        var vm = Principale(backup, senzaBackup: senzaBackup);
        await vm.InizializzaAsync();
        return vm;
    }

    private ImpostazioniApp DalFile() => new ImpostazioniService(_servizio.PercorsoFile).Carica();

    // ---------- Il comando ----------

    /// <summary>Mette l'archivio dove la cartella predefinita dei backup (C:\Backup\DocumentaleMarta) finirebbe al suo interno: il programma deve chiedere.</summary>
    private void LaCartellaPredefinitaNonSiPuoUsare() => _impostazioni.PercorsoRadice = @"C:\Backup";

    [Fact]
    public async Task IlPrimoBackup_SeLaPredefinitaNonSiPuoUsare_ChiedeLaCartella_LaUsa_ESiRicordaNelleImpostazioni()
    {
        var vm = await ConDocumentiAsync();
        LaCartellaPredefinitaNonSiPuoUsare();
        _a.Dialog.RispondiCartella(@"E:\Backup");

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        Assert.Single(_a.Dialog.SceltaCartellaChiesta);
        Assert.Equal([@"E:\Backup"], _backup.Cartelle);
        Assert.Equal(@"E:\Backup", _impostazioni.CartellaBackup);
        Assert.Equal(new DateTime(2026, 10, 1, 9, 30, 0), _impostazioni.UltimoBackup);
        // E lo ricorda anche per la prossima apertura del programma.
        Assert.Equal(@"E:\Backup", DalFile().CartellaBackup);
        Assert.Equal(new DateTime(2026, 10, 1, 9, 30, 0), DalFile().UltimoBackup);
    }

    [Fact]
    public async Task ILMessaggioFinale_DiceDoveEIlBackupEQuantoEGrande()
    {
        var vm = await ConDocumentiAsync();
        _impostazioni.CartellaBackup = @"E:\Backup";

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        var (titolo, messaggio) = Assert.Single(_a.Dialog.Messaggi);
        Assert.Equal("Backup completato", titolo);
        Assert.Contains(@"E:\Backup\backup.zip", messaggio);
        Assert.Contains("3 file", messaggio);
        Assert.Contains("2 KB", messaggio);
        Assert.Contains("istruzioni", messaggio);
        Assert.Empty(_a.Dialog.Errori);
    }

    [Fact]
    public async Task DalSecondoBackupInPoi_BastaUnClic_NonChiedePiuLaCartella()
    {
        var vm = await ConDocumentiAsync();
        _impostazioni.CartellaBackup = @"E:\Backup";

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        Assert.Empty(_a.Dialog.SceltaCartellaChiesta);
        Assert.Equal([@"E:\Backup"], _backup.Cartelle);
    }

    [Fact]
    public async Task AnnullandoLaScelta_NonSiFaNulla()
    {
        var vm = await ConDocumentiAsync();
        LaCartellaPredefinitaNonSiPuoUsare();
        _a.Dialog.RispondiCartella(null);

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        Assert.Empty(_backup.Cartelle);
        Assert.Empty(_a.Dialog.Messaggi);
        Assert.Empty(_a.Dialog.Errori);
        Assert.Null(_impostazioni.CartellaBackup);
        Assert.Null(_impostazioni.UltimoBackup);
        Assert.False(File.Exists(_servizio.PercorsoFile));
    }

    [Fact]
    public async Task UnaDestinazioneNonValida_MostraIlMotivo_ENonSiRicordaLaScelta()
    {
        _backup.Errore = new ArchivioException("La cartella dei backup non può stare dentro l'archivio.");
        var vm = await ConDocumentiAsync();
        LaCartellaPredefinitaNonSiPuoUsare();
        _a.Dialog.RispondiCartella(Path.Combine(_a.Radice, "Fatture"));

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        Assert.Equal("La cartella dei backup non può stare dentro l'archivio.", Assert.Single(_a.Dialog.Errori));
        Assert.Empty(_a.Dialog.Messaggi);
        Assert.Null(_impostazioni.CartellaBackup);
        Assert.Null(_impostazioni.UltimoBackup);
        Assert.Equal("", vm.TestoBackup);
    }

    [Fact]
    public async Task UnDiscoNonRaggiungibile_DiceCosaControllare_ENonCambiaNulla()
    {
        _backup.Errore = new IOException("Impossibile trovare una parte del percorso.");
        var vm = await ConDocumentiAsync();
        _impostazioni.CartellaBackup = @"E:\Backup";

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        var errore = Assert.Single(_a.Dialog.Errori);
        Assert.Contains("Il backup non è riuscito", errore);
        Assert.Contains("Impossibile trovare una parte del percorso.", errore);
        Assert.Contains("disco esterno", errore);
        Assert.Null(_impostazioni.UltimoBackup);
        Assert.Equal(@"E:\Backup", _impostazioni.CartellaBackup);
        Assert.Equal("", vm.TestoBackup);
    }

    [Fact]
    public async Task MentreLavora_MostraLAvanzamento_EIlPulsanteEDisattivato()
    {
        var vm = await ConDocumentiAsync();
        _impostazioni.CartellaBackup = @"E:\Backup";
        string? testoDurante = null;
        bool? attivoDurante = null;
        _backup.DuranteIlBackup = async (_, avanzamento) =>
        {
            await Task.Yield(); // il comando risulta "in corso" solo dopo il primo vero passaggio asincrono
            avanzamento!.Report(5);
            testoDurante = vm.TestoBackup;
            attivoDurante = vm.EseguiBackupCommand.CanExecute(null);
        };

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        Assert.Equal("Backup in corso… 5 file", testoDurante);
        Assert.False(attivoDurante);
        Assert.Equal("", vm.TestoBackup);
        Assert.True(vm.EseguiBackupCommand.CanExecute(null));
    }

    [Fact]
    public async Task UnaSecondaRichiestaMentreLavora_NonParteInParallelo()
    {
        var vm = await ConDocumentiAsync();
        _impostazioni.CartellaBackup = @"E:\Backup";
        var via = new TaskCompletionSource();
        _backup.DuranteIlBackup = (_, _) => via.Task;

        var primo = vm.EseguiBackupCommand.ExecuteAsync(null);
        Assert.False(vm.EseguiBackupCommand.CanExecute(null));
        vm.EseguiBackupCommand.Execute(null); // un secondo clic: ignorato, il backup è già in corso
        via.SetResult();
        await primo;

        Assert.Single(_backup.Cartelle);
    }

    [Fact]
    public async Task SeLeImpostazioniNonSiSalvano_ILBackupRiescePerSelo_ConUnAvviso()
    {
        var bloccante = _a.Tmp.CreaFile("bloccante", "sono un file");
        var vm = Principale(servizio: new ImpostazioniService(Path.Combine(bloccante, "impostazioni.json")));
        await _a.CreaCartellaAsync("Fatture", "Fattura", "a.pdf");
        await vm.InizializzaAsync();
        _impostazioni.CartellaBackup = @"E:\Backup";

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        var (_, messaggio) = Assert.Single(_a.Dialog.Messaggi);
        Assert.Contains("Il backup è stato creato", messaggio);
        Assert.Contains("non è stato possibile salvare la data del backup", messaggio);
        Assert.Equal(new DateTime(2026, 10, 1, 9, 30, 0), _impostazioni.UltimoBackup); // vale per questa sessione
    }

    [Fact]
    public async Task SenzaIlServizio_IlPulsanteNonCompare_EIlComandoNonFaNulla()
    {
        var vm = await ConDocumentiAsync(senzaBackup: true);

        Assert.False(vm.BackupDisponibile);
        Assert.False(vm.EseguiBackupCommand.CanExecute(null));
        await vm.EseguiBackupCommand.ExecuteAsync(null);
        Assert.Empty(_a.Dialog.SceltaCartellaChiesta);
    }

    // ---------- Il promemoria ----------

    [Fact]
    public async Task SenzaDocumenti_NienteDaProteggere_NienteRicordi()
    {
        var vm = Principale();
        await vm.InizializzaAsync();

        Assert.Equal("", vm.TestoPromemoriaBackup);
        Assert.False(vm.PromemoriaBackupVisibile);
    }

    [Fact]
    public async Task ConDocumentiEMaiUnBackup_Ricorda()
    {
        var vm = await ConDocumentiAsync();

        Assert.Equal("Non hai ancora fatto nessun backup dei tuoi documenti.", vm.TestoPromemoriaBackup);
        Assert.True(vm.PromemoriaBackupVisibile);
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(29, false)]
    [InlineData(30, true)]
    [InlineData(45, true)]
    public async Task ConUnBackupDelPassato_RicordaSoloDopoLaSogliaDelleImpostazioni(int giorniFa, bool visibile)
    {
        _impostazioni.UltimoBackup = Oggi.AddDays(-giorniFa).AddHours(3); // anche se è stato fatto a metà giornata
        _impostazioni.BackupPromemoriaGiorni = 30;
        var vm = await ConDocumentiAsync();

        Assert.Equal(visibile, vm.PromemoriaBackupVisibile);
        Assert.Equal(visibile ? $"L'ultimo backup risale a {giorniFa} giorni fa." : "", vm.TestoPromemoriaBackup);
    }

    [Fact]
    public async Task LaSogliaDelPromemoria_SiLeggeDalleImpostazioni()
    {
        _impostazioni.UltimoBackup = Oggi.AddDays(-8);
        _impostazioni.BackupPromemoriaGiorni = 7;
        var vm = await ConDocumentiAsync();

        Assert.True(vm.PromemoriaBackupVisibile);
    }

    [Fact]
    public async Task RimandandoIlPromemoria_SparisceFinoAllaProssimaApertura()
    {
        var vm = await ConDocumentiAsync();
        var cambiate = new List<string?>();
        vm.PropertyChanged += (_, e) => cambiate.Add(e.PropertyName);

        vm.RimandaPromemoriaBackupCommand.Execute(null);

        Assert.False(vm.PromemoriaBackupVisibile);
        Assert.Contains(nameof(MainViewModel.PromemoriaBackupVisibile), cambiate);

        await vm.AggiornaCommand.ExecuteAsync(null); // una rilettura dell'archivio non lo fa tornare
        Assert.False(vm.PromemoriaBackupVisibile);

        // All'apertura successiva (un nuovo MainViewModel) compare di nuovo.
        var riaperto = Principale();
        await riaperto.InizializzaAsync();
        Assert.True(riaperto.PromemoriaBackupVisibile);
    }

    [Fact]
    public async Task DopoUnBackupRiuscito_IlPromemoriaSparisce()
    {
        var vm = await ConDocumentiAsync();
        Assert.True(vm.PromemoriaBackupVisibile);
        _impostazioni.CartellaBackup = @"E:\Backup";

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        Assert.False(vm.PromemoriaBackupVisibile);
        Assert.Equal("", vm.TestoPromemoriaBackup);
    }

    [Fact]
    public async Task DopoUnBackupFallito_IlPromemoriaResta()
    {
        _backup.Errore = new IOException("disco pieno");
        var vm = await ConDocumentiAsync();
        _impostazioni.CartellaBackup = @"E:\Backup";

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        Assert.True(vm.PromemoriaBackupVisibile);
    }

    [Fact]
    public async Task SenzaIlServizio_ILPromemoriaNonCompareMai()
    {
        var vm = await ConDocumentiAsync(senzaBackup: true);

        Assert.False(vm.PromemoriaBackupVisibile);
        Assert.Equal("", vm.TestoPromemoriaBackup);
    }

    [Fact]
    public async Task CambiandoLaSogliaNelleImpostazioni_IlPromemoriaSiAggiornaSubito()
    {
        _impostazioni.UltimoBackup = Oggi.AddDays(-10);
        var vm = await ConDocumentiAsync();
        Assert.False(vm.PromemoriaBackupVisibile); // 10 giorni su 30

        _a.Dialog.RispondiImpostazioni(m => m.PromemoriaBackup = "5");
        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.True(vm.PromemoriaBackupVisibile);
        Assert.Equal(5, DalFile().BackupPromemoriaGiorni);
    }

    // ---------- Con il servizio vero ----------

    [Fact]
    public async Task ConIlServizioVero_IlClicCreaLoZip_ELoRicordaNelFile()
    {
        await _a.CreaCartellaAsync("Fatture", "Fattura", "a.pdf", "b.pdf");
        var destinazione = _a.Tmp.Combina("dischetto");
        var vm = Principale(new BackupService(_a.Factory, _a.Files));
        await vm.InizializzaAsync();
        _impostazioni.CartellaBackup = destinazione; // senza una scelta si userebbe C:\Backup, che in una prova non va toccata

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        var zip = Assert.Single(Directory.GetFiles(destinazione, "*.zip"));
        using (var archivio = ZipFile.OpenRead(zip))
        {
            Assert.Contains(archivio.Entries, e => e.FullName == "Fatture/Fattura/a.pdf");
            Assert.Contains(archivio.Entries, e => e.FullName == "_dati/documentale.db");
        }
        Assert.Empty(_a.Dialog.Errori);
        Assert.Contains(Path.GetFileName(zip), Assert.Single(_a.Dialog.Messaggi).Messaggio);
        Assert.Equal(destinazione, DalFile().CartellaBackup);
        Assert.NotNull(DalFile().UltimoBackup);
    }

    [Fact]
    public async Task ConIlServizioVero_UnaCartellaDentroLArchivio_VieneRifiutata()
    {
        await _a.CreaCartellaAsync("Fatture", "Fattura", "a.pdf");
        var vm = Principale(new BackupService(_a.Factory, _a.Files));
        await vm.InizializzaAsync();
        _impostazioni.CartellaBackup = Path.Combine(_a.Radice, "Fatture"); // (le Impostazioni non lo lascerebbero salvare: qui si prova il servizio)

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        Assert.Contains("non può stare dentro l'archivio", Assert.Single(_a.Dialog.Errori));
        Assert.Null(_impostazioni.UltimoBackup);
        Assert.False(Directory.Exists(Path.Combine(_a.Radice, "Fatture", "Documentale-backup")));
        Assert.Empty(Directory.GetFiles(_a.Fisico("Fatture", "Fattura"), "*.zip"));
    }
}

public class ImpostazioniBackupTests
{
    private static ImpostazioniViewModel Nuovo(Action<ImpostazioniApp>? prepara = null)
    {
        var impostazioni = new ImpostazioniApp { PercorsoRadice = @"C:\Documentale" };
        prepara?.Invoke(impostazioni);
        return new ImpostazioniViewModel(impostazioni, @"C:\x\impostazioni.json");
    }

    [Fact]
    public void LaFinestra_MostraLeImpostazioniDelBackup()
    {
        var vm = Nuovo(i =>
        {
            i.CartellaBackup = @"E:\Backup";
            i.BackupPromemoriaGiorni = 14;
            i.UltimoBackup = new DateTime(2026, 9, 12, 14, 5, 0);
        });

        Assert.Equal(@"E:\Backup", vm.CartellaBackup);
        Assert.Equal("14", vm.PromemoriaBackup);
        Assert.Equal("Ultimo backup: 12/09/2026 alle 14:05", vm.UltimoBackupTesto);
        Assert.True(vm.PuoSalvare);
    }

    [Fact]
    public void SenzaBackup_LaFinestraLoDice_ELaCartellaEQuellaPredefinita()
    {
        var vm = Nuovo();

        Assert.Equal(@"C:\Backup\DocumentaleMarta", vm.CartellaBackup);
        Assert.Equal("30", vm.PromemoriaBackup);
        Assert.Equal("Non hai ancora fatto nessun backup.", vm.UltimoBackupTesto);
        Assert.True(vm.PuoSalvare);
    }

    [Fact]
    public void SeLaPredefinitaNonSiPuoUsare_LaCartellaNellaFinestraEVuota_EIlBackupLaChiedera()
    {
        var vm = new ImpostazioniViewModel(new ImpostazioniApp { PercorsoRadice = @"C:\Backup" }, @"C:\x\impostazioni.json");

        Assert.Equal("", vm.CartellaBackup);
        Assert.True(vm.PuoSalvare);
    }

    [Fact]
    public void LaSpiegazione_DiceQualeELaCartellaPredefinita_EPerchePuoStareVuota()
    {
        var vm = Nuovo();

        Assert.Contains(@"C:\Backup\DocumentaleMarta", vm.SuggerimentoCartellaBackup);
        Assert.Contains("Se la lasci vuota", vm.SuggerimentoCartellaBackup);
        Assert.Contains("Non può stare dentro la cartella dell'archivio", vm.SuggerimentoCartellaBackup);
    }

    [Fact]
    public void Costruisci_PortaLaCartella_SenzaSpazi_EVuotaDiventaNull()
    {
        var vm = Nuovo();

        vm.CartellaBackup = @"  E:\Backup  ";
        vm.PromemoriaBackup = " 21 ";
        var risultato = vm.Costruisci();
        Assert.Equal(@"E:\Backup", risultato.CartellaBackup);
        Assert.Equal(21, risultato.BackupPromemoriaGiorni);

        vm.CartellaBackup = "   ";
        Assert.Null(vm.Costruisci().CartellaBackup);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("2,5")]
    public void UnPromemoriaNonNumerico_Errore(string testo)
    {
        var vm = Nuovo();

        vm.PromemoriaBackup = testo;

        Assert.Equal("I giorni del promemoria del backup devono essere un numero intero.", vm.Errore);
        Assert.False(vm.PuoSalvare);
    }

    [Fact]
    public void UnPromemoriaDiZeroGiorni_Errore()
    {
        var vm = Nuovo();

        vm.PromemoriaBackup = "0";

        Assert.Equal("Il promemoria del backup deve essere di almeno 1 giorno.", vm.Errore);
        Assert.False(vm.PuoSalvare);
    }

    [Theory]
    [InlineData(@"C:\Documentale")]
    [InlineData(@"C:\Documentale\")]
    [InlineData(@"c:\documentale\Fatture")]
    [InlineData(@"C:\Documentale\_backup\2026")]
    public void UnaCartellaDentroLArchivio_Errore(string cartella)
    {
        var vm = Nuovo();

        vm.CartellaBackup = cartella;

        Assert.Contains("non può stare dentro l'archivio", vm.Errore);
        Assert.False(vm.PuoSalvare);
    }

    [Theory]
    [InlineData(@"E:\Backup")]
    [InlineData(@"C:\Documentale-backup")] // inizia come la radice ma è un'altra cartella
    [InlineData(@"C:\Altro\Documentale")]
    public void UnaCartellaFuoriDallArchivio_Valida(string cartella)
    {
        var vm = Nuovo();

        vm.CartellaBackup = cartella;

        Assert.Equal("", vm.Errore);
        Assert.True(vm.PuoSalvare);
    }

    [Fact]
    public void UnPercorsoNonValido_Errore()
    {
        var vm = Nuovo();

        vm.CartellaBackup = "E:\\Backup\0strano";

        Assert.Equal("La cartella dei backup non è un percorso valido.", vm.Errore);
    }

    [Fact]
    public void ModificandoLaCartella_ErroreEPuoSalvareSiAggiornano()
    {
        var vm = Nuovo();
        var cambiate = new List<string?>();
        vm.PropertyChanged += (_, e) => cambiate.Add(e.PropertyName);

        vm.CartellaBackup = @"C:\Documentale\x";

        Assert.Contains(nameof(ImpostazioniViewModel.Errore), cambiate);
        Assert.Contains(nameof(ImpostazioniViewModel.PuoSalvare), cambiate);
    }

    [Fact]
    public void LeImpostazioniDelFile_ConUnaCartellaDentroLArchivio_NonSonoValide()
    {
        var impostazioni = new ImpostazioniApp { PercorsoRadice = @"C:\Documentale", CartellaBackup = @"C:\Documentale\backup" };

        Assert.Contains(impostazioni.Valida(), e => e.Contains("non può stare dentro l'archivio"));
    }
}
