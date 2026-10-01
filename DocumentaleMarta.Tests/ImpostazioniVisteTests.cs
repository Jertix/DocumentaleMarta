using System.Windows;
using System.Windows.Controls;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

/// <summary>Prove di fumo sulle viste della fase 7: finestre Impostazioni e Informazioni, pannello "Ricerca avanzata", barra con i nuovi pulsanti.</summary>
public class ImpostazioniVisteTests
{
    private static ImpostazioniViewModel NuovoModello(ImpostazioniApp? impostazioni = null) =>
        new(impostazioni ?? new ImpostazioniApp(), @"C:\Users\Marta\AppData\Roaming\DocumentaleMarta\impostazioni.json");

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

    private static IEnumerable<UIElement> Antenati(DependencyObject elemento)
    {
        for (var o = System.Windows.Media.VisualTreeHelper.GetParent(elemento); o is not null; o = System.Windows.Media.VisualTreeHelper.GetParent(o))
            if (o is UIElement ui)
                yield return ui;
    }

    // ---------- Impostazioni ----------

    [Fact]
    public void ImpostazioniDialog_SiCostruisceESiDisegna_SenzaErroriDiBinding()
    {
        var modello = NuovoModello();

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new ImpostazioniDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 580, 760, "impostazioni");

            // I campi principali ci sono e mostrano i valori di partenza.
            var caselle = Tutti<TextBox>(contenuto).Select(t => t.Text).ToList();
            Assert.Contains("METAL PROJET DI TEDDE PAOLO E SORRENTINO LUCA SNC", caselle);
            Assert.Contains("30", caselle);
            Assert.Contains("7", caselle);
            Assert.Contains("Tutti i documenti", caselle);
            Assert.Contains(caselle, t => t.Contains("impostazioni.json") || t.Contains(@"C:\Documentale"));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public void ImpostazioniDialog_ConValoriNonValidi_MostraLErrore_ESalvaEDisattivato()
    {
        var modello = NuovoModello();
        modello.SogliaArancione = "abc";

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new ImpostazioniDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 580, 760, "impostazioni-errore");

            var testi = Tutti<TextBlock>(contenuto).Where(t => t.Visibility == Visibility.Visible).Select(t => t.Text).ToList();
            Assert.Contains(testi, t => t.Contains("numero intero"));

            var salva = Tutti<Button>(contenuto).Single(b => b.IsDefault);
            Assert.False(salva.IsEnabled);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public void ImpostazioniDialog_ConValoriValidi_SalvaEAttivo()
    {
        var modello = NuovoModello();

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new ImpostazioniDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 580, 760, "impostazioni-valide");

            var salva = Tutti<Button>(contenuto).Single(b => b.IsDefault);
            Assert.True(salva.IsEnabled);
            Assert.Single(Tutti<Button>(contenuto), b => b.IsCancel);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public void ImpostazioniDialog_ModificandoUnaSoglia_ILPulsanteSegueLaValidita()
    {
        var modello = NuovoModello();

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new ImpostazioniDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 580, 760, "impostazioni-modifica");
            var salva = Tutti<Button>(contenuto).Single(b => b.IsDefault);

            modello.SogliaRossa = "40"; // maggiore dell'arancione (30)
            Assert.False(salva.IsEnabled);

            modello.SogliaRossa = "5";
            Assert.True(salva.IsEnabled);
        });

        Assert.Empty(errori);
    }

    // ---------- Informazioni ----------

    [Fact]
    public async Task InformazioniDialog_SiCostruisceESiDisegna_SenzaErroriDiBinding()
    {
        using var a = new ArchivioDiProva();
        var area = await a.Servizio.CreaAreaAsync("Fatture");
        await a.CreaCartellaInAreaAsync(area, "Fattura Rossi", ["rossi.pdf", "note.txt"]);
        var impostazioni = new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false };
        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, impostazioni,
            null, a.Ricerca, null, new FintoOcr(), new ImpostazioniService(a.Tmp.Combina("imp", "impostazioni.json")));
        await vm.InizializzaAsync();
        vm.ApriInformazioniCommand.Execute(null);
        var info = a.Dialog.UltimeInformazioni!;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new InformazioniDialog(info);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 520, 560, "informazioni");

            var testi = Tutti<TextBlock>(contenuto).Select(t => t.Text)
                .Concat(Tutti<TextBox>(contenuto).Select(t => t.Text)).ToList();
            Assert.Contains("METAL PROJET DI TEDDE PAOLO E SORRENTINO LUCA SNC", testi);
            Assert.Contains(testi, t => t.Contains("1 area") && t.Contains("2 documenti"));
            Assert.Contains(testi, t => t.Contains("documentale.db"));
            Assert.Contains(testi, t => t.Contains("Disponibile"));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public void InformazioniDialog_ConOcrNonDisponibile_MostraIlMotivo()
    {
        var info = new InformazioniViewModel(
            "Documentale", "1.0.0", "DITTA", "123", "123 (IT)", "VIA ROMA 1", "Fabbri",
            @"C:\Documentale", @"C:\Documentale\_dati\documentale.db", @"C:\x\impostazioni.json",
            "0 aree  ·  0 cartelle  ·  0 documenti",
            "Manca il pacchetto della lingua italiana per il riconoscimento del testo.", ".NET 10 su Windows 11");

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new InformazioniDialog(info);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 520, 560, "informazioni-senza-ocr");
            var testi = Tutti<TextBlock>(contenuto).Select(t => t.Text).ToList();
            Assert.Contains(testi, t => t.Contains("lingua italiana"));
        });

        Assert.Empty(errori);
    }

    // ---------- Finestra principale ----------

    private static async Task<(MainViewModel Vm, ArchivioDiProva Archivio)> ArchivioAsync(bool conImpostazioni)
    {
        var a = new ArchivioDiProva();
        var fatture = await a.Servizio.CreaAreaAsync("Fatture");
        var riunioni = await a.Servizio.CreaAreaAsync("Riunioni");
        await a.CreaCartellaInAreaAsync(fatture, "Fattura Rossi", ["rossi.pdf"], new DateOnly(2026, 10, 12));
        await a.CreaCartellaInAreaAsync(riunioni, "Verbali 2026", ["verbale.txt"]);

        var impostazioni = new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false };
        var servizio = conImpostazioni ? new ImpostazioniService(a.Tmp.Combina("imp", "impostazioni.json")) : null;
        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, impostazioni,
            new AlertService(30, 7, true, new TempoFisso(new DateTime(2026, 10, 1))),
            a.Ricerca, null, new FintoOcr(), servizio);
        await vm.InizializzaAsync();
        return (vm, a);
    }

    [Fact]
    public async Task FinestraPrincipale_PannelloRicercaAvanzataAperto_SiDisegna_SenzaErroriDiBinding()
    {
        var (vm, a) = await ArchivioAsync(conImpostazioni: true);
        using var _ = a;
        vm.PannelloFiltriAperto = true;
        vm.Filtri.Pdf = true;
        vm.Filtri.Stato = StatoOpzione.Tutte.Single(s => s.Valore == StatoRicerca.InScadenza);
        await vm.RicercaCompletata;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1100, 680, "ricerca-avanzata-aperta");

            // Il pannello è visibile e il pulsante dice quanti filtri sono attivi.
            var pulsante = Tutti<System.Windows.Controls.Primitives.ToggleButton>(contenuto)
                .Single(t => t.Content is string s && s.StartsWith("Ricerca avanzata"));
            Assert.Equal("Ricerca avanzata (2)", pulsante.Content);
            Assert.True(pulsante.IsChecked);

            var pdf = Tutti<CheckBox>(contenuto).Single(c => c.Content is string s && s == "PDF");
            Assert.True(pdf.IsChecked);
            Assert.All(new UIElement[] { pdf }.Concat(Antenati(pdf)), e => Assert.Equal(Visibility.Visible, e.Visibility));
            Assert.Contains(Tutti<ComboBox>(contenuto), c => c.Items.OfType<AreaOpzione>().Any(o => o.Nome == "Fatture"));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_PannelloChiuso_NonMostraIFiltri()
    {
        var (vm, a) = await ArchivioAsync(conImpostazioni: true);
        using var _ = a;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1100, 680, "ricerca-avanzata-chiusa");

            // Senza una finestra mostrata IsVisible è sempre falso: si guarda la Visibility di ogni elemento da lì in su.
            var casella = Tutti<CheckBox>(contenuto).Single(c => c.Content is string s && s == "PDF");
            Assert.Contains(Antenati(casella), e => e.Visibility != Visibility.Visible);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_ConImpostazioni_HaIPulsantiImpostazioniEInformazioni()
    {
        var (vm, a) = await ArchivioAsync(conImpostazioni: true);
        using var _ = a;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1100, 680, "barra-con-impostazioni");

            var impostazioni = Tutti<Button>(contenuto).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Impostazioni");
            var informazioni = Tutti<Button>(contenuto).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Informazioni");
            Assert.Equal(Visibility.Visible, impostazioni.Visibility);
            Assert.Equal(Visibility.Visible, informazioni.Visibility);
            Assert.True(impostazioni.IsEnabled);
            Assert.Same(vm.ApriImpostazioniCommand, impostazioni.Command);
            Assert.Same(vm.ApriInformazioniCommand, informazioni.Command);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_SenzaServizioDelleImpostazioni_NascondeIlPulsanteImpostazioni()
    {
        var (vm, a) = await ArchivioAsync(conImpostazioni: false);
        using var _ = a;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1100, 680, "barra-senza-impostazioni");

            var impostazioni = Tutti<Button>(contenuto).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Impostazioni");
            Assert.Equal(Visibility.Collapsed, impostazioni.Visibility);
            Assert.Equal(Visibility.Visible,
                Tutti<Button>(contenuto).Single(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Informazioni").Visibility);
        });

        Assert.Empty(errori);
    }
}
