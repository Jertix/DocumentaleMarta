using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

/// <summary>Backup e ripristino si fanno dalla finestra Impostazioni: i pulsanti salvano e poi eseguono l'operazione.</summary>
public class ImpostazioniBackupAzioniTests : IDisposable
{
    private static readonly DateTime Oggi = new(2026, 10, 1, 9, 0, 0);

    private readonly ArchivioDiProva _a = new();
    private readonly ImpostazioniService _servizio;
    private readonly ImpostazioniApp _impostazioni;
    private readonly FintoBackup _backup = new();

    public ImpostazioniBackupAzioniTests()
    {
        _servizio = new ImpostazioniService(_a.Tmp.Combina("impostazioni", "impostazioni.json"));
        _impostazioni = new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false };
    }

    public void Dispose() => _a.Dispose();

    private MainViewModel Principale(bool conBackup = true, ImpostazioniService? servizio = null) => new(
        _a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni,
        new AlertService(_impostazioni, new TempoFisso(Oggi)),
        servizioImpostazioni: servizio ?? _servizio,
        servizioBackup: conBackup ? _backup : null);

    private ImpostazioniApp DalFile() => new ImpostazioniService(_servizio.PercorsoFile).Carica();

    // ---------- Il modello della finestra ----------

    [Fact]
    public void IlModello_DiPartenza_NonOffreIPulsantiNeHaAzioniRichieste()
    {
        var modello = new ImpostazioniViewModel(new ImpostazioniApp(), @"C:\x\impostazioni.json");

        Assert.False(modello.BackupDisponibile);
        Assert.Equal(AzioneDaImpostazioni.Nessuna, modello.AzioneRichiesta);
    }

    [Fact]
    public void IlModello_ConIlServizio_OffreIPulsanti()
    {
        var modello = new ImpostazioniViewModel(new ImpostazioniApp(), @"C:\x\impostazioni.json", backupDisponibile: true);

        Assert.True(modello.BackupDisponibile);
    }

    [Fact]
    public async Task LaFinestraRicevePerDefinizione_LaDisponibilitaDelBackup()
    {
        var conBackup = Principale();
        _a.Dialog.RispondiImpostazioni(null);
        await conBackup.ApriImpostazioniCommand.ExecuteAsync(null);
        Assert.True(_a.Dialog.UltimeImpostazioni!.BackupDisponibile);

        var senza = Principale(conBackup: false);
        _a.Dialog.RispondiImpostazioni(null);
        await senza.ApriImpostazioniCommand.ExecuteAsync(null);
        Assert.False(_a.Dialog.UltimeImpostazioni!.BackupDisponibile);
    }

    // ---------- Fai il backup ora ----------

    [Fact]
    public async Task FaiIlBackupOra_SalvaLeImpostazioni_EPoiFaIlBackupNellaCartellaScelta()
    {
        var vm = Principale();
        _a.Dialog.RispondiImpostazioni(m =>
        {
            m.CartellaBackup = @"E:\Nuova cartella";
            m.SogliaArancione = "45";
            m.AzioneRichiesta = AzioneDaImpostazioni.Backup;
        });

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        // Il backup è andato nella cartella appena scelta nella finestra, senza chiederla di nuovo.
        Assert.Equal([@"E:\Nuova cartella"], _backup.Cartelle);
        Assert.Empty(_a.Dialog.SceltaCartellaChiesta);
        // E le impostazioni sono salvate (le altre modifiche comprese) e ricordano il backup appena fatto.
        Assert.Equal(45, DalFile().SogliaArancioneGiorni);
        Assert.Equal(@"E:\Nuova cartella", DalFile().CartellaBackup);
        Assert.NotNull(DalFile().UltimoBackup);
        Assert.Contains("Backup completato", Assert.Single(_a.Dialog.Messaggi).Titolo);
    }

    [Fact]
    public async Task LeImpostazioni_SonoGiaApplicate_QuandoPartePerazione()
    {
        var vm = Principale();
        int? sogliaDuranteIlBackup = null;
        _backup.DuranteIlBackup = (_, _) =>
        {
            sogliaDuranteIlBackup = _impostazioni.SogliaArancioneGiorni;
            return Task.CompletedTask;
        };
        _a.Dialog.RispondiImpostazioni(m =>
        {
            m.CartellaBackup = @"E:\B";
            m.SogliaArancione = "60";
            m.AzioneRichiesta = AzioneDaImpostazioni.Backup;
        });

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Equal(60, sogliaDuranteIlBackup);
    }

    [Fact]
    public async Task FaiIlBackupOra_SenzaCartellaScelta_SeLaPredefinitaNonSiPuoUsare_LaChiedeComeAlSolito()
    {
        var vm = Principale();
        _impostazioni.PercorsoRadice = @"C:\Backup"; // qui la cartella predefinita (C:\Backup\DocumentaleMarta) starebbe dentro l'archivio
        _a.Dialog.RispondiCartella(@"E:\Scelta");
        _a.Dialog.RispondiImpostazioni(m => m.AzioneRichiesta = AzioneDaImpostazioni.Backup);

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Single(_a.Dialog.SceltaCartellaChiesta);
        Assert.Equal([@"E:\Scelta"], _backup.Cartelle);
    }

    // ---------- Ripristina da un backup ----------

    [Fact]
    public async Task Ripristina_SalvaLeImpostazioni_EPoiParteIlRipristino()
    {
        var vm = Principale();
        var madre = _a.Tmp.Combina("dischetto");
        _a.Dialog.RispondiFileBackup(@"E:\backup.zip");
        _a.Dialog.RispondiCartella(madre);
        _a.Dialog.RispondiDomande(true, false); // conferma il ripristino, poi non usa subito il nuovo archivio
        _a.Dialog.RispondiImpostazioni(m =>
        {
            m.PromemoriaBackup = "14";
            m.AzioneRichiesta = AzioneDaImpostazioni.Ripristino;
        });

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Equal(14, DalFile().BackupPromemoriaGiorni); // salvate prima
        var (zip, destinazione) = Assert.Single(_backup.Ripristini);
        Assert.Equal(@"E:\backup.zip", zip);
        Assert.Equal(Path.Combine(madre, "Documentale ripristinato 2026-10-01"), destinazione);
        Assert.Empty(_backup.Cartelle); // non è un backup
    }

    // ---------- Quando non si fa nulla ----------

    [Fact]
    public async Task SoloSalva_NonParteNessunaOperazione()
    {
        var vm = Principale();
        _a.Dialog.RispondiImpostazioni(m => m.SogliaArancione = "50");

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Equal(50, DalFile().SogliaArancioneGiorni);
        Assert.Empty(_backup.Cartelle);
        Assert.Empty(_backup.Ripristini);
        Assert.Empty(_a.Dialog.SceltaFileBackupChiesta);
    }

    [Fact]
    public async Task Annullando_NonSiSalvaNulla_NonParteNessunaOperazione()
    {
        var vm = Principale();
        _a.Dialog.RispondiImpostazioni(null);

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.False(File.Exists(_servizio.PercorsoFile));
        Assert.Empty(_backup.Cartelle);
    }

    [Fact]
    public async Task ConValoriNonValidi_IPulsantiDelBackupNonPartono_ComeSalva()
    {
        var vm = Principale();
        _a.Dialog.RispondiImpostazioni(m =>
        {
            m.SogliaArancione = "abc"; // il pulsante sarebbe disattivato: per il test equivale ad annullare
            m.AzioneRichiesta = AzioneDaImpostazioni.Backup;
        });

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Empty(_backup.Cartelle);
        Assert.False(File.Exists(_servizio.PercorsoFile));
    }

    [Fact]
    public async Task SeIlSalvataggioFallisce_IlBackupNonParte_ENemmenoQuandoLaFinestraSiRiapre()
    {
        var bloccante = _a.Tmp.CreaFile("bloccante", "sono un file");
        var vm = Principale(servizio: new ImpostazioniService(Path.Combine(bloccante, "impostazioni.json")));
        _a.Dialog.RispondiImpostazioni(m => { m.CartellaBackup = @"E:\B"; m.AzioneRichiesta = AzioneDaImpostazioni.Backup; });
        _a.Dialog.RispondiImpostazioni(m => m.SogliaArancione = "40"); // alla riapertura l'utente preme solo Salva (che fallisce ancora)
        _a.Dialog.RispondiImpostazioni(null);

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Equal(2, _a.Dialog.Errori.Count(e => e.Contains("Non è stato possibile salvare le impostazioni")));
        Assert.Empty(_backup.Cartelle); // l'azione chiesta prima del salvataggio fallito non vale per la riapertura
        Assert.Equal(3, _a.Dialog.AperturaImpostazioni);
    }

    [Fact]
    public async Task SeIlBackupFallisce_IlProgrammaNonSiRompe_LeImpostazioniRestanoSalvate()
    {
        _backup.Errore = new IOException("disco non raggiungibile");
        var vm = Principale();
        _a.Dialog.RispondiImpostazioni(m =>
        {
            m.CartellaBackup = @"E:\Spento";
            m.AzioneRichiesta = AzioneDaImpostazioni.Backup;
        });

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Contains("Il backup non è riuscito", Assert.Single(_a.Dialog.Errori));
        Assert.Equal(@"E:\Spento", DalFile().CartellaBackup); // la cartella scelta si è salvata con le altre
        Assert.True(File.Exists(_servizio.PercorsoFile));
    }
}

[Collection("WPF")]
public class ImpostazioniDialogBackupVisteTests
{
    private static IEnumerable<T> Tutti<T>(DependencyObject? radice) where T : DependencyObject
    {
        if (radice is null)
            yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(radice); i++)
        {
            var figlio = VisualTreeHelper.GetChild(radice, i);
            if (figlio is T trovato)
                yield return trovato;
            foreach (var discendente in Tutti<T>(figlio))
                yield return discendente;
        }
    }

    private static bool Visibile(UIElement elemento)
    {
        for (DependencyObject? o = elemento; o is not null; o = VisualTreeHelper.GetParent(o))
            if (o is UIElement u && u.Visibility != Visibility.Visible)
                return false;
        return true;
    }

    private static ImpostazioniViewModel Modello(bool backupDisponibile) =>
        new(new ImpostazioniApp { PercorsoRadice = @"C:\Documentale", CartellaBackup = @"E:\Backup" },
            @"C:\Users\Marta\AppData\Roaming\DocumentaleMarta\impostazioni.json", backupDisponibile);

    [Fact]
    public void ConIlServizio_LaSezioneBackupHaIDuePulsanti_AttiviSeIValoriSonoValidi()
    {
        var modello = Modello(backupDisponibile: true);

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new ImpostazioniDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 580, 900, "impostazioni-pulsanti-backup");

            var fai = Tutti<Button>(contenuto).Single(b => b.Content is "Fai il backup ora");
            var ripristina = Tutti<Button>(contenuto).Single(b => b.Content is "Ripristina da un backup…");
            Assert.True(Visibile(fai) && Visibile(ripristina));
            Assert.True(fai.IsEnabled && ripristina.IsEnabled);
            Assert.Contains("Salva le impostazioni e poi", (string)fai.ToolTip);
            Assert.Contains("non tocca l'archivio attuale", (string)ripristina.ToolTip);
            Assert.Contains(Tutti<TextBlock>(contenuto).Where(Visibile).Select(t => t.Text),
                t => t.Contains("salvano prima le impostazioni"));

            // Con un valore non valido i pulsanti si spengono, come "Salva".
            modello.SogliaArancione = "abc";
            Assert.False(fai.IsEnabled);
            Assert.False(ripristina.IsEnabled);
            modello.SogliaArancione = "30";
            Assert.True(fai.IsEnabled);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public void SenzaIlServizio_IPulsantiNonCompaiono_MaCartellaEGiorniSi()
    {
        var modello = Modello(backupDisponibile: false);

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new ImpostazioniDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 580, 900, "impostazioni-senza-pulsanti-backup");

            Assert.DoesNotContain(Tutti<Button>(contenuto).Where(Visibile), b => b.Content is "Fai il backup ora" or "Ripristina da un backup…");
            Assert.Contains(Tutti<TextBox>(contenuto).Select(t => t.Text), t => t == @"E:\Backup");
        });

        Assert.Empty(errori);
    }

    [Fact]
    public void PremendoUnPulsante_LAzioneSiRicorda_ESeInvalidoNonSuccedeNulla()
    {
        var modello = Modello(backupDisponibile: true);

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new ImpostazioniDialog(modello);
            VisteTests.Disegna((FrameworkElement)finestra.Content, 580, 900, "impostazioni-clic");
            var contenuto = (FrameworkElement)finestra.Content;
            var fai = Tutti<Button>(contenuto).Single(b => b.Content is "Fai il backup ora");
            var ripristina = Tutti<Button>(contenuto).Single(b => b.Content is "Ripristina da un backup…");

            // A finestra non mostrata non si può impostare l'esito: l'azione però è già stata registrata (viene prima).
            Assert.Throws<InvalidOperationException>(() =>
                fai.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)));
            Assert.Equal(AzioneDaImpostazioni.Backup, modello.AzioneRichiesta);

            modello.AzioneRichiesta = AzioneDaImpostazioni.Nessuna;
            Assert.Throws<InvalidOperationException>(() =>
                ripristina.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)));
            Assert.Equal(AzioneDaImpostazioni.Ripristino, modello.AzioneRichiesta);

            // Con un valore non valido il clic non fa niente (il pulsante è comunque spento).
            modello.AzioneRichiesta = AzioneDaImpostazioni.Nessuna;
            modello.SogliaRossa = "x";
            fai.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(AzioneDaImpostazioni.Nessuna, modello.AzioneRichiesta);
        });

        Assert.Empty(errori);
    }
}
