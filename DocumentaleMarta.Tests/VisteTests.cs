using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;

namespace DocumentaleMarta.Tests;

/// <summary>
/// Prove di fumo sulle viste XAML: costruite e disegnate in memoria (senza mostrare finestre).
/// Un nome sbagliato in un binding non dà errori di compilazione: qui lo si scopre dai messaggi di WPF.
/// Se la variabile d'ambiente DOCUMENTALE_TEST_IMMAGINI indica una cartella, salva anche le immagini disegnate.
/// </summary>
public class VisteTests
{
    private sealed class RaccoltaErroriBinding : TraceListener
    {
        public List<string> Messaggi { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message) { if (message is not null) Messaggi.Add(message); }
    }

    /// <summary>Esegue su un thread STA (richiesto da WPF) raccogliendo gli errori di binding.</summary>
    private static List<string> InSta(Action azione)
    {
        var raccolta = new RaccoltaErroriBinding();
        Exception? errore = null;

        var thread = new Thread(() =>
        {
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(raccolta);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            try { azione(); }
            catch (Exception ex) { errore = ex; }
            finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(raccolta); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (errore is not null)
            ExceptionDispatchInfo.Capture(errore).Throw();
        return raccolta.Messaggi;
    }

    private static void Disegna(FrameworkElement elemento, double larghezza, double altezza, string nome)
    {
        // Come in una finestra vera: si lascia finire il lavoro in coda (es. il calcolo delle larghezze delle colonne)
        // e poi si ripete il layout.
        for (var passata = 0; passata < 2; passata++)
        {
            elemento.Measure(new Size(larghezza, altezza));
            elemento.Arrange(new Rect(0, 0, larghezza, altezza));
            elemento.UpdateLayout();
            elemento.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }

        if (Environment.GetEnvironmentVariable("DOCUMENTALE_TEST_IMMAGINI") is not { Length: > 0 } cartella)
            return;

        var bitmap = new RenderTargetBitmap((int)larghezza, (int)altezza, 96, 96, PixelFormats.Pbgra32);
        var sfondo = new DrawingVisual();
        using (var dc = sfondo.RenderOpen())
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, larghezza, altezza));
        bitmap.Render(sfondo);
        bitmap.Render(elemento);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(cartella);
        using var file = File.Create(Path.Combine(cartella, nome + ".png"));
        encoder.Save(file);
    }

    [Fact]
    public void IlRilevatoreDiBindingRotti_FunzionaDavvero()
    {
        // Senza questa prova gli altri test potrebbero passare anche se il rilevatore non vedesse nulla.
        var errori = InSta(() =>
        {
            var testo = new TextBlock { DataContext = new object() };
            testo.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("ProprietaInesistente"));
            testo.Measure(new Size(100, 20));
        });

        Assert.Contains(errori, m => m.Contains("ProprietaInesistente"));
    }

    [Fact]
    public void NuovaCartellaDialog_SiCostruisceESiDisegna_SenzaErroriDiBinding()
    {
        using var a = new ArchivioDiProva();
        var vm = new NuovaCartellaViewModel(a.Dialog, "Fatture")
        {
            Descrizione = "Fatture di settembre",
            DataScadenza = new DateTime(2026, 10, 31)
        };
        a.Dialog.RispondiFile(
            a.Tmp.CreaFile("s/Fattura 123.pdf", "abc"), a.Tmp.CreaFile("s/Fattura 124.pdf", "abcdef"));
        vm.AllegaCommand.Execute(null);

        var errori = InSta(() =>
        {
            var finestra = new NuovaCartellaDialog(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            Disegna(contenuto, 560, 520, "nuova-cartella");
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task CartellaView_ConDocumenti_SiDisegna_SenzaErroriDiBinding()
    {
        using var a = new ArchivioDiProva();
        var dettaglio = await a.CreaCartellaAsync("Fatture", "Fattura 123", "Fattura 123.pdf", "scansione.jpg", "relazione.docx");
        File.Delete(a.Fisico("Fatture", "Fattura 123", "scansione.jpg")); // una riga col file mancante
        var form = new CartellaFormViewModel(a.Servizio, a.Files, a.Dialog, a.Shell,
            await a.Servizio.AggiornaCartellaAsync(dettaglio.Id,
                new DatiCartella("Fattura 123", "Fatture di settembre", new DateOnly(2026, 10, 31), true, new DateOnly(2026, 10, 1))));

        var errori = InSta(() =>
        {
            var vista = new CartellaView { DataContext = form };
            Disegna(vista, 780, 560, "cartella-con-documenti");

            Assert.Equal(3, FindDataGrid(vista)!.Items.Count);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task CartellaView_SenzaDocumenti_SiDisegna_SenzaErroriDiBinding()
    {
        using var a = new ArchivioDiProva();
        var dettaglio = await a.CreaCartellaAsync("Fatture", "Vuota");
        var form = new CartellaFormViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, dettaglio);

        var errori = InSta(() => Disegna(new CartellaView { DataContext = form }, 780, 460, "cartella-vuota"));

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_ConAlberoECartellaSelezionata_SiDisegna_SenzaErroriDiBinding()
    {
        using var a = new ArchivioDiProva();
        await a.CreaCartellaAsync("Fatture", "Fattura 123", "Fattura 123.pdf");
        await a.Servizio.CreaAreaAsync("INPS");
        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, new ImpostazioniApp { PercorsoRadice = a.Radice });
        await vm.InizializzaAsync();
        vm.Radici[0].Figli.Single(n => n.Nome == "Fatture").Figli.Single().IsSelected = true;
        await vm.CaricamentoFormCompletato;

        var errori = InSta(() =>
        {
            var finestra = new MainWindow(vm);
            Disegna((FrameworkElement)finestra.Content, 1100, 680, "finestra-cartella");

            vm.Radici[0].Figli.Single(n => n.Nome == "INPS").IsSelected = true;
            Disegna((FrameworkElement)finestra.Content, 1100, 680, "finestra-area");
        });

        Assert.Empty(errori);
    }

    private static DataGrid? FindDataGrid(DependencyObject radice)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(radice); i++)
        {
            var figlio = VisualTreeHelper.GetChild(radice, i);
            if (figlio is DataGrid griglia)
                return griglia;
            if (FindDataGrid(figlio) is { } trovata)
                return trovata;
        }
        return null;
    }
}
