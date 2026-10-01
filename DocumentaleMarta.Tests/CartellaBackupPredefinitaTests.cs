using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

/// <summary>Se l'utente non ha scelto dove fare i backup si usa C:\Backup\DocumentaleMarta, senza chiedere niente.</summary>
public class CartellaBackupPredefinitaTests : IDisposable
{
    private readonly ArchivioDiProva _a = new();
    private readonly ImpostazioniService _servizio;
    private readonly ImpostazioniApp _impostazioni;
    private readonly FintoBackup _backup = new();

    public CartellaBackupPredefinitaTests()
    {
        _servizio = new ImpostazioniService(_a.Tmp.Combina("impostazioni", "impostazioni.json"));
        _impostazioni = new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false };
    }

    public void Dispose() => _a.Dispose();

    private async Task<MainViewModel> PrincipaleAsync()
    {
        await _a.CreaCartellaAsync("Fatture", "Fattura", "a.pdf");
        var vm = new MainViewModel(
            _a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni,
            new AlertService(_impostazioni, new TempoFisso(new DateTime(2026, 10, 1, 9, 0, 0))),
            servizioImpostazioni: _servizio, servizioBackup: _backup);
        await vm.InizializzaAsync();
        return vm;
    }

    private ImpostazioniApp DalFile() => new ImpostazioniService(_servizio.PercorsoFile).Carica();

    // ---------- Le impostazioni ----------

    [Fact]
    public void LaCartellaPredefinita_EQuellaIndicataDalProgetto()
    {
        Assert.Equal(@"C:\Backup\DocumentaleMarta", ImpostazioniApp.CartellaBackupPredefinita);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SenzaUnaScelta_SiUsaLaCartellaPredefinita(string? scelta)
    {
        var impostazioni = new ImpostazioniApp { PercorsoRadice = @"C:\Documentale", CartellaBackup = scelta };

        Assert.Equal(@"C:\Backup\DocumentaleMarta", impostazioni.CartellaBackupInUso);
    }

    [Fact]
    public void ConUnaScelta_SiUsaQuella_SenzaSpaziAttorno()
    {
        var impostazioni = new ImpostazioniApp { PercorsoRadice = @"C:\Documentale", CartellaBackup = @"  E:\Miei backup  " };

        Assert.Equal(@"E:\Miei backup", impostazioni.CartellaBackupInUso);
    }

    [Theory]
    [InlineData(@"C:\Backup")]                          // la predefinita starebbe dentro l'archivio
    [InlineData(@"C:\Backup\DocumentaleMarta")]         // coincide con l'archivio
    [InlineData(@"c:\backup\documentalemarta\")]        // maiuscole e barra finale non cambiano nulla
    public void SeLaPredefinitaStarebbeDentroLArchivio_NonSiUsa_ESiDovraChiedere(string radice)
    {
        var impostazioni = new ImpostazioniApp { PercorsoRadice = radice };

        Assert.Null(impostazioni.CartellaBackupInUso);
    }

    [Theory]
    [InlineData(@"C:\Backup\DocumentaleMarta\Archivio")] // l'archivio sta DENTRO la cartella dei backup: va bene
    [InlineData(@"C:\Backups")]                           // inizia allo stesso modo ma è un'altra cartella
    [InlineData(@"D:\Backup")]                            // un altro disco
    public void SeLaPredefinitaNonStaDentroLArchivio_SiUsa(string radice)
    {
        var impostazioni = new ImpostazioniApp { PercorsoRadice = radice };

        Assert.Equal(@"C:\Backup\DocumentaleMarta", impostazioni.CartellaBackupInUso);
    }

    [Fact]
    public void UnaScelta_DentroLArchivio_RestaUnErrore_AncheSeLaPredefinitaSarebbeBuona()
    {
        var impostazioni = new ImpostazioniApp { PercorsoRadice = @"C:\Documentale", CartellaBackup = @"C:\Documentale\copie" };

        Assert.Contains(impostazioni.Valida(), e => e.Contains("non può stare dentro l'archivio"));
    }

    [Fact]
    public void SenzaUnaScelta_LeImpostazioniSonoValide_AnchePerUnArchivioNellaCartellaPredefinita()
    {
        // La cartella predefinita non è una scelta dell'utente: se non si può usare, il programma chiede, ma non dà errore.
        var impostazioni = new ImpostazioniApp { PercorsoRadice = @"C:\Backup" };

        Assert.Empty(impostazioni.Valida());
    }

    [Fact]
    public void LaCartellaInUso_NonSiSalvaNelFile()
    {
        var servizio = new ImpostazioniService(_a.Tmp.Combina("file.json"));
        servizio.Salva(new ImpostazioniApp { PercorsoRadice = @"C:\Documentale" });

        Assert.DoesNotContain("CartellaBackupInUso", File.ReadAllText(servizio.PercorsoFile));
    }

    [Fact]
    public void UnFileVecchio_ConLaCartellaNull_SiLegge_EUsaLaPredefinita()
    {
        var percorso = _a.Tmp.CreaFile("vecchio.json", "{ \"NomeRadice\": \"Archivio\", \"CartellaBackup\": null }");

        var impostazioni = new ImpostazioniService(percorso).Carica();

        Assert.Null(impostazioni.CartellaBackup);
        Assert.Equal(@"C:\Backup\DocumentaleMarta", impostazioni.CartellaBackupInUso);
    }

    // ---------- Il primo backup ----------

    [Fact]
    public async Task IlPrimoBackup_UsaLaCartellaPredefinita_SenzaChiedereNiente()
    {
        var vm = await PrincipaleAsync();

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        Assert.Empty(_a.Dialog.SceltaCartellaChiesta);
        Assert.Equal([@"C:\Backup\DocumentaleMarta"], _backup.Cartelle);
        Assert.Empty(_a.Dialog.Errori);
        Assert.Contains(@"C:\Backup\DocumentaleMarta\backup.zip", Assert.Single(_a.Dialog.Messaggi).Messaggio);
    }

    [Fact]
    public async Task DopoIlPrimoBackup_LaCartellaSiRicordaNelleImpostazioni_EOraEUnaScelta()
    {
        var vm = await PrincipaleAsync();

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\Backup\DocumentaleMarta", _impostazioni.CartellaBackup);
        Assert.Equal(@"C:\Backup\DocumentaleMarta", DalFile().CartellaBackup);
        Assert.NotNull(DalFile().UltimoBackup);
    }

    [Fact]
    public async Task IBackupSuccessivi_UsanoSempreLaStessaCartella()
    {
        var vm = await PrincipaleAsync();

        await vm.EseguiBackupCommand.ExecuteAsync(null);
        await vm.EseguiBackupCommand.ExecuteAsync(null);

        Assert.Equal([@"C:\Backup\DocumentaleMarta", @"C:\Backup\DocumentaleMarta"], _backup.Cartelle);
        Assert.Empty(_a.Dialog.SceltaCartellaChiesta);
    }

    [Fact]
    public async Task UnaCartellaScelta_VinceSullaPredefinita()
    {
        _impostazioni.CartellaBackup = @"E:\Miei backup";
        var vm = await PrincipaleAsync();

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        Assert.Equal([@"E:\Miei backup"], _backup.Cartelle);
        Assert.Empty(_a.Dialog.SceltaCartellaChiesta);
    }

    [Fact]
    public async Task SeLaPredefinitaNonSiPuoUsare_ComeAlSolito_SiChiedeDove()
    {
        var vm = await PrincipaleAsync();
        _impostazioni.PercorsoRadice = @"C:\Backup";
        _a.Dialog.RispondiCartella(@"E:\Scelta");

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        Assert.Single(_a.Dialog.SceltaCartellaChiesta);
        Assert.Equal([@"E:\Scelta"], _backup.Cartelle);
    }

    [Fact]
    public async Task SeLaPredefinitaNonSiPuoCreare_IlProgrammaDiceCosaFareESuggerisceLeImpostazioni()
    {
        _backup.Errore = new UnauthorizedAccessException("Accesso negato al percorso «C:\\Backup».");
        var vm = await PrincipaleAsync();

        await vm.EseguiBackupCommand.ExecuteAsync(null);

        var errore = Assert.Single(_a.Dialog.Errori);
        Assert.Contains("Il backup non è riuscito", errore);
        Assert.Contains("dalle Impostazioni", errore);
        Assert.Null(_impostazioni.CartellaBackup);     // un tentativo fallito non resta nelle impostazioni
        Assert.Null(_impostazioni.UltimoBackup);
    }

    // ---------- Ripristino e finestra Impostazioni ----------

    [Fact]
    public async Task IlRipristino_PartePerScegliereIlFileDallaCartellaPredefinita()
    {
        var vm = await PrincipaleAsync();
        _a.Dialog.RispondiFileBackup(null); // l'utente annulla: basta vedere da dove si partiva

        await vm.RipristinaDaBackupCommand.ExecuteAsync(null);

        Assert.Equal([@"C:\Backup\DocumentaleMarta"], _a.Dialog.SceltaFileBackupChiesta);
    }

    [Fact]
    public async Task LaFinestraImpostazioni_MostraLaCartellaPredefinita_ESalvandolaLaRende_UnaScelta()
    {
        var vm = await PrincipaleAsync();
        string? mostrata = null;
        _a.Dialog.RispondiImpostazioni(m => mostrata = m.CartellaBackup);

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\Backup\DocumentaleMarta", mostrata);
        Assert.Equal(@"C:\Backup\DocumentaleMarta", DalFile().CartellaBackup);
    }

    [Fact]
    public async Task SvuotandoLaCartellaNellaFinestra_SiTornaAllaPredefinita()
    {
        _impostazioni.CartellaBackup = @"E:\Miei backup";
        var vm = await PrincipaleAsync();
        _a.Dialog.RispondiImpostazioni(m => m.CartellaBackup = "");

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Null(DalFile().CartellaBackup);
        Assert.Equal(@"C:\Backup\DocumentaleMarta", _impostazioni.CartellaBackupInUso);
    }
}
