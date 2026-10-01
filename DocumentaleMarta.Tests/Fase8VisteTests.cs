using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

/// <summary>Prove di fumo sulle schermate della fase 8: duplicati, ricorrenze, backup e anteprima.</summary>
[Collection("WPF")]
public class Fase8VisteTests
{
    private static IEnumerable<T> Tutti<T>(DependencyObject? radice) where T : DependencyObject
    {
        if (radice is null)
            yield break;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(radice); i++)
        {
            var figlio = System.Windows.Media.VisualTreeHelper.GetChild(radice, i);
            if (figlio is T trovato)
                yield return trovato;
            foreach (var discendente in Tutti<T>(figlio))
                yield return discendente;
        }
    }

    private static DuplicatoTrovato Dup(string nome, params (string Area, string Cartella)[] dove) =>
        new(@"C:\x\" + nome, nome,
            dove.Select((d, i) => new DocumentoGiaArchiviato(i + 1, nome, i + 1, d.Cartella, d.Area)).ToList());

    /// <summary>Visibile davvero: lo è l'elemento e lo sono tutti i suoi genitori (senza finestra mostrata <c>IsVisible</c> è sempre falso).</summary>
    private static bool Visibile(UIElement elemento)
    {
        for (DependencyObject? o = elemento; o is not null; o = System.Windows.Media.VisualTreeHelper.GetParent(o))
            if (o is UIElement u && u.Visibility != Visibility.Visible)
                return false;
        return true;
    }

    private static IEnumerable<string> TestiVisibili(DependencyObject radice) =>
        Tutti<TextBlock>(radice).Where(Visibile).Select(t => t.Text);

    private static IEnumerable<Button> PulsantiVisibili(DependencyObject radice) => Tutti<Button>(radice).Where(Visibile);

    // ---------- Duplicati ----------

    [Fact]
    public void DuplicatiDialog_ConAlcuniDuplicati_HaTreScelte_ESaltaEIlPulsantePredefinito()
    {
        var modello = new DuplicatiViewModel(
            [Dup("fattura.pdf", ("Fatture", "Fattura 123")), Dup("contratto.pdf", ("Contratti", "Affitto"), ("Archivio", "Vecchi"))], totaleFile: 5);

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new DuplicatiDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 560, 360, "duplicati-tre-scelte");

            var pulsanti = PulsantiVisibili(contenuto).Select(b => (string)b.Content).ToList();
            Assert.Equal(["Allega comunque", "Salta i duplicati", "Annulla"], pulsanti);
            Assert.True(Tutti<Button>(contenuto).Single(b => (string)b.Content == "Salta i duplicati").IsDefault);
            Assert.True(Tutti<Button>(contenuto).Single(b => (string)b.Content == "Annulla").IsCancel);

            var testi = TestiVisibili(contenuto).ToList();
            Assert.Contains("2 dei 5 file scelti sono già nell'archivio.", testi);
            Assert.Contains("Vuoi allegarli comunque?", testi);
            Assert.Contains("Già in: Fatture › Fattura 123", testi);
            Assert.Contains("Già in: Contratti › Affitto; Archivio › Vecchi", testi);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public void DuplicatiDialog_ConTuttiDuplicati_NonOffreDiSaltare()
    {
        var modello = new DuplicatiViewModel([Dup("fattura.pdf", ("Fatture", "Fattura 123"))], totaleFile: 1);

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new DuplicatiDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 560, 320, "duplicati-due-scelte");

            var pulsanti = PulsantiVisibili(contenuto).Select(b => (string)b.Content).ToList();
            Assert.Equal(["Allega comunque", "Non allegare"], pulsanti);
            var rifiuto = Tutti<Button>(contenuto).Single(b => (string)b.Content == "Non allegare");
            Assert.True(rifiuto.IsCancel);
            Assert.True(rifiuto.IsDefault); // Invio sceglie l'opzione prudente
            Assert.Contains("Il file «fattura.pdf» è già nell'archivio.", TestiVisibili(contenuto));
        });

        Assert.Empty(errori);
    }

    // ---------- Ricorrenze ----------

    [Fact]
    public void NuovaCartellaDialog_IlMenuSiRipete_SiAccendeConLaScadenza()
    {
        using var a = new ArchivioDiProva();
        var modello = new NuovaCartellaViewModel(a.Dialog, "Fiscale") { Titolo = "F24 mensile" };

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new NuovaCartellaDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 600, 560, "nuova-cartella-senza-scadenza");

            var menu = Tutti<ComboBox>(contenuto).Single();
            Assert.Equal(["Mai", "Ogni mese", "Ogni 3 mesi", "Ogni anno"], menu.Items.OfType<RicorrenzaOpzione>().Select(o => o.Testo));
            Assert.False(menu.IsEnabled); // senza scadenza non si può ripetere

            modello.DataScadenza = new DateTime(2026, 10, 16);
            modello.Ricorrenza = Ricorrenza.Trimestrale;
            VisteTests.Disegna(contenuto, 600, 560, "nuova-cartella-con-ricorrenza");

            Assert.True(menu.IsEnabled);
            Assert.Equal("Ogni 3 mesi", ((RicorrenzaOpzione)menu.SelectedItem).Testo);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task CartellaView_ConRicorrenza_MostraMenuESuggerimento()
    {
        using var a = new ArchivioDiProva();
        var areaId = await a.Servizio.CreaAreaAsync("Fiscale");
        var dettaglio = await a.Servizio.CreaCartellaConDatiAsync(
            areaId, new DatiCartella("F24", "Versamenti", new DateOnly(2026, 10, 16), false, null, Ricorrenza.Mensile), []);
        var form = new CartellaFormViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, dettaglio);

        var errori = VisteTests.InSta(() =>
        {
            var vista = new CartellaView { DataContext = form };
            VisteTests.Disegna(vista, 780, 460, "cartella-con-ricorrenza");

            var menu = Tutti<ComboBox>(vista).Single(c => c.Items.OfType<RicorrenzaOpzione>().Any());
            Assert.True(menu.IsEnabled);
            Assert.Equal("Ogni mese", ((RicorrenzaOpzione)menu.SelectedItem).Testo);
            Assert.Contains("Completandola ti propone la cartella successiva.", TestiVisibili(vista));

            // Togliendo la scadenza il menu si spegne e torna a "Mai": il suggerimento sparisce.
            form.CancellaScadenzaCommand.Execute(null);
            VisteTests.Disegna(vista, 780, 460, "cartella-senza-scadenza");
            Assert.False(menu.IsEnabled);
            Assert.Equal("Mai", ((RicorrenzaOpzione)menu.SelectedItem).Testo);
            Assert.DoesNotContain("Completandola ti propone la cartella successiva.", TestiVisibili(vista));
        });

        Assert.Empty(errori);
    }

    // ---------- Impostazioni: sezione Backup ----------

    [Fact]
    public void ImpostazioniDialog_MostraLaSezioneBackup()
    {
        var modello = new ImpostazioniViewModel(
            new ImpostazioniApp
            {
                PercorsoRadice = @"C:\Documentale", CartellaBackup = @"E:\Backup", BackupPromemoriaGiorni = 14,
                UltimoBackup = new DateTime(2026, 9, 12, 14, 5, 0)
            },
            @"C:\Users\Marta\AppData\Roaming\DocumentaleMarta\impostazioni.json");

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new ImpostazioniDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 580, 900, "impostazioni-backup");

            var caselle = Tutti<TextBox>(contenuto).Select(t => t.Text).ToList();
            Assert.Contains(@"E:\Backup", caselle);
            Assert.Contains("14", caselle);
            Assert.Contains("Ultimo backup: 12/09/2026 alle 14:05", TestiVisibili(contenuto));
            Assert.Contains(Tutti<Button>(contenuto), b => b.Content is "Scegli…");
            Assert.True(Tutti<Button>(contenuto).Single(b => b.IsDefault).IsEnabled);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public void ImpostazioniDialog_ConUnaCartellaDentroLArchivio_MostraLErrore_ENonSiPuoSalvare()
    {
        var modello = new ImpostazioniViewModel(new ImpostazioniApp { PercorsoRadice = @"C:\Documentale" }, @"C:\x\impostazioni.json")
        {
            CartellaBackup = @"C:\Documentale\copie"
        };

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new ImpostazioniDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 580, 900, "impostazioni-backup-errore");

            Assert.Contains(TestiVisibili(contenuto), t => t.Contains("non può stare dentro l'archivio"));
            Assert.False(Tutti<Button>(contenuto).Single(b => b.IsDefault).IsEnabled);
        });

        Assert.Empty(errori);
    }

    // ---------- Finestra principale: backup e anteprima ----------

    private static async Task<(MainViewModel Vm, ArchivioDiProva Archivio)> PrincipaleAsync(
        bool backup = true, IGeneratoreAnteprima? generatore = null, string[]? file = null)
    {
        var a = new ArchivioDiProva();
        Directory.CreateDirectory(a.Tmp.Combina("src"));
        var tmp = file ?? ["fattura.pdf"];
        var area = await a.Servizio.CreaAreaAsync("Fatture");
        var sorgenti = tmp.Select(n => n.EndsWith(".pdf")
            ? FileDiProva.Pdf(a.Tmp.Combina(Path.Combine("src", n)), "Fattura numero 123 del 1 ottobre", "Seconda pagina") : a.Tmp.CreaFile(Path.Combine("src", n), "x")).ToList();
        await a.Servizio.CreaCartellaConDatiAsync(area, new DatiCartella("Fattura 123", null, new DateOnly(2026, 10, 20), false, null), sorgenti);

        var impostazioni = new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false };
        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, impostazioni,
            new AlertService(30, 7, true, new TempoFisso(new DateTime(2026, 10, 1))),
            servizioImpostazioni: new ImpostazioniService(a.Tmp.Combina("imp", "impostazioni.json")),
            servizioBackup: backup ? new FintoBackup() : null,
            generatoreAnteprima: generatore);
        vm.Anteprima?.GetType(); // (solo per chiarezza: l'anteprima c'è se c'è il generatore)
        if (vm.Anteprima is not null)
            vm.Anteprima.Ritardo = TimeSpan.Zero;
        await vm.InizializzaAsync();
        return (vm, a);
    }

    [Fact]
    public async Task FinestraPrincipale_ConBackup_MostraPulsanteEPromemoria_ERimandandoSparisce()
    {
        var (vm, a) = await PrincipaleAsync();
        using var _ = a;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1180, 680, "finestra-promemoria-backup");

            var backup = Tutti<Button>(contenuto).First(b => b.Content is "Backup");
            Assert.Equal(Visibility.Visible, backup.Visibility);
            Assert.Same(vm.EseguiBackupCommand, backup.Command);
            Assert.Contains("Non hai ancora fatto nessun backup dei tuoi documenti.", TestiVisibili(contenuto));
            Assert.Contains(PulsantiVisibili(contenuto), b => b.Content is "Fai il backup ora");

            vm.RimandaPromemoriaBackupCommand.Execute(null);
            VisteTests.Disegna(contenuto, 1180, 680, "finestra-promemoria-rimandato");
            Assert.DoesNotContain("Non hai ancora fatto nessun backup dei tuoi documenti.", TestiVisibili(contenuto));
            Assert.Equal(Visibility.Visible, backup.Visibility); // il pulsante resta
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_DuranteIlBackup_MostraLAvanzamentoInFondo()
    {
        var (vm, a) = await PrincipaleAsync();
        using var _ = a;
        vm.TestoBackup = "Backup in corso… 12 file";

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1180, 680, "finestra-backup-in-corso");

            Assert.Contains("Backup in corso… 12 file", TestiVisibili(contenuto));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_SenzaServizioDiBackup_NonMostraNePulsanteNePromemoria()
    {
        var (vm, a) = await PrincipaleAsync(backup: false);
        using var _ = a;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1180, 680, "finestra-senza-backup");

            Assert.Equal(Visibility.Collapsed, Tutti<Button>(contenuto).First(b => b.Content is "Backup").Visibility);
            Assert.DoesNotContain("Non hai ancora fatto nessun backup dei tuoi documenti.", TestiVisibili(contenuto));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_ConAnteprima_MostraLaPaginaDelPdfSelezionato()
    {
        var (vm, a) = await PrincipaleAsync(generatore: new GeneratoreAnteprima());
        using var _ = a;
        vm.Radici.Single().Aree.Single().Figli.Single().IsSelected = true;
        await vm.CaricamentoFormCompletato;
        vm.FormCartella!.DocumentoSelezionato = vm.FormCartella.Documenti.Single();
        await vm.Anteprima!.Completamento;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1180, 680, "finestra-anteprima-pdf");

            var pannello = Tutti<AnteprimaView>(contenuto).Single();
            Assert.Equal(Visibility.Visible, pannello.Visibility);
            Assert.NotNull(Tutti<Image>(pannello).Single().Source);
            Assert.Contains("fattura.pdf", TestiVisibili(pannello));
            Assert.Contains("Pagina 1 di 2", TestiVisibili(pannello)); // il PDF di prova ha due pagine
            Assert.Equal(3, PulsantiVisibili(pannello).Count()); // le due frecce delle pagine e "Ingrandisci"

            var interruttore = Tutti<ToggleButton>(contenuto).Single(t => t.Content is "Anteprima");
            Assert.True(interruttore.IsChecked);

            // Chiudendo il pannello la colonna sparisce.
            vm.Anteprima.Visibile = false;
            VisteTests.Disegna(contenuto, 1180, 680, "finestra-anteprima-chiusa");
            Assert.Equal(Visibility.Collapsed, pannello.Visibility);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_ConAnteprima_SenzaSelezione_DiceDiScegliereUnDocumento()
    {
        var (vm, a) = await PrincipaleAsync(generatore: new GeneratoreAnteprima());
        using var _ = a;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1180, 680, "finestra-anteprima-vuota");

            var pannello = Tutti<AnteprimaView>(contenuto).Single();
            Assert.Equal(Visibility.Visible, pannello.Visibility);
            Assert.Contains(AnteprimaViewModel.TestoNessunDocumento, TestiVisibili(pannello));
            Assert.Equal(Visibility.Collapsed, Tutti<Border>(pannello).First(b => b.Child is Image).Visibility);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_SenzaGeneratore_NonHaIlPannelloNeLInterruttore()
    {
        var (vm, a) = await PrincipaleAsync(generatore: null);
        using var _ = a;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1180, 680, "finestra-senza-anteprima");

            Assert.Equal(Visibility.Collapsed, Tutti<AnteprimaView>(contenuto).Single().Visibility);
            Assert.Equal(Visibility.Collapsed, Tutti<ToggleButton>(contenuto).Single(t => t.Content is "Anteprima").Visibility);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_FormatoSenzaAnteprima_DiceDiUsareApri()
    {
        var (vm, a) = await PrincipaleAsync(generatore: new GeneratoreAnteprima(), file: ["lettera.docx"]);
        using var _ = a;
        vm.Radici.Single().Aree.Single().Figli.Single().IsSelected = true;
        await vm.CaricamentoFormCompletato;
        vm.FormCartella!.DocumentoSelezionato = vm.FormCartella.Documenti.Single();
        await vm.Anteprima!.Completamento;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1180, 680, "finestra-anteprima-non-disponibile");

            var pannello = Tutti<AnteprimaView>(contenuto).Single();
            Assert.Contains(TestiVisibili(pannello), t => t.Contains("DOCX") && t.Contains("Apri"));
            Assert.Empty(PulsantiVisibili(pannello));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_ConUnaGrigliaDiArea_LaSelezioneAggiornaLAnteprima()
    {
        var (vm, a) = await PrincipaleAsync(generatore: new GeneratoreAnteprima());
        using var _ = a;
        vm.Radici.Single().Aree.Single().IsSelected = true;
        await vm.CaricamentoElencoCompletato;
        // Il disegno parte dopo mezzo secondo: il tempo di scollegare la finestra, che in questo test vive su un thread
        // a parte (nel programma vero tutto passa dal thread dell'interfaccia).
        vm.Anteprima!.Ritardo = TimeSpan.FromMilliseconds(500);

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1180, 680, "finestra-area-senza-selezione");

            // Si seleziona una riga come farebbe il clic: la griglia riscrive la selezione nel modello.
            var griglia = VisteTests.FindDataGrid(contenuto, "Nome file")!;
            griglia.SelectedIndex = 0;
            Assert.Same(vm.ElencoDocumenti!.Documenti[0], vm.ElencoDocumenti.DocumentoSelezionato);
            finestra.DataContext = null;
        });

        Assert.Empty(errori);
        await vm.Anteprima!.Completamento;
        Assert.Equal("fattura.pdf", vm.Anteprima.Titolo);
    }
}
