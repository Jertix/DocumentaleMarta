using System.Diagnostics;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

/// <summary>
/// Prove di fumo sulle viste XAML: costruite e disegnate in memoria (senza mostrare finestre).
/// Un nome sbagliato in un binding non dà errori di compilazione: qui lo si scopre dai messaggi di WPF.
/// Se la variabile d'ambiente DOCUMENTALE_TEST_IMMAGINI indica una cartella, salva anche le immagini disegnate.
/// </summary>
[Collection("WPF")]
public class VisteTests
{
    private sealed class RaccoltaErroriBinding : TraceListener
    {
        public List<string> Messaggi { get; } = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        {
            // Lo stile di Windows 11 per il DatePicker ha un legame che al primo disegno non trova ancora il suo DatePicker e lo risolve
            // subito dopo (succede in qualunque programma WPF con quel tema): non è un errore dei nostri file XAML.
            if (message is null || message.Contains("target element is 'DatePickerTextBox'"))
                return;

            Messaggi.Add(message);
        }
    }

    /// <summary>
    /// Il thread WPF delle prove: uno solo per tutte, con un'applicazione vera (come nel programma, dove lo stile di Windows 11 sta
    /// nelle risorse dell'applicazione e c'è già quando nascono le finestre). Le risorse dell'applicazione appartengono al thread
    /// che le ha create: per questo non se ne usa uno nuovo a ogni prova.
    /// </summary>
    private sealed class ThreadWpf
    {
        private readonly System.Collections.Concurrent.BlockingCollection<Action> _coda = [];

        public Thread Thread { get; }

        public ThreadWpf()
        {
            using var pronto = new ManualResetEventSlim();
            Thread = new Thread(() =>
            {
                // Chiudere l'ultima finestra non deve spegnere l'applicazione delle prove.
                _ = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                AspettoDiProva.Applica(AspettoDiProva.Base);
                pronto.Set();
                foreach (var lavoro in _coda.GetConsumingEnumerable())
                    lavoro();
            })
            {
                IsBackground = true,
                Name = "Thread WPF delle prove"
            };
            Thread.SetApartmentState(ApartmentState.STA);
            Thread.Start();
            pronto.Wait();
        }

        public void Esegui(Action azione)
        {
            using var fine = new ManualResetEventSlim();
            _coda.Add(() =>
            {
                try { azione(); }
                finally { fine.Set(); }
            });
            fine.Wait();
        }
    }

    private static readonly Lazy<ThreadWpf> Wpf = new(() => new ThreadWpf());

    /// <summary>Esegue sul thread WPF (STA, come richiede WPF) raccogliendo gli errori di binding.</summary>
    internal static List<string> InSta(Action azione)
    {
        var raccolta = new RaccoltaErroriBinding();
        Exception? errore = null;

        void Lavoro()
        {
            // Il thread è lo stesso per tutte le prove: quel che WPF aveva ancora in coda per le prove precedenti
            // si esaurisce prima di cominciare (senza ascoltare), così i suoi avvisi non finiscono nella prova di turno.
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(raccolta);
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
            try
            {
                azione();
                // Quel che la prova ha messo in coda si esaurisce qui, non dentro la prova successiva.
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex) { errore = ex; }
            finally
            {
                try { AspettoDiProva.Ripristina(); }
                catch (Exception ex) { errore ??= ex; }
                finally { PresentationTraceSources.DataBindingSource.Listeners.Remove(raccolta); }
            }
        }

        if (Thread.CurrentThread == Wpf.Value.Thread)
            Lavoro(); // già sul thread WPF (una prova dentro l'altra): non si può aspettare se stessi
        else
            Wpf.Value.Esegui(Lavoro);

        if (errore is not null)
            ExceptionDispatchInfo.Capture(errore).Throw();
        return raccolta.Messaggi;
    }

    internal static void Disegna(FrameworkElement elemento, double larghezza, double altezza, string nome)
    {
        // Disegnato da solo, senza il resto della finestra, l'elemento perde il colore del testo che la finestra vera gli darebbe
        // (con il tema scuro: bianco): lo si ricollega alla stessa risorsa, così cambia anch'esso con il tema.
        if (elemento.Parent is Window && elemento.ReadLocalValue(System.Windows.Documents.TextElement.ForegroundProperty) == DependencyProperty.UnsetValue)
            elemento.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "WindowForeground");

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
        // Lo sfondo della finestra: quello che la finestra dichiara (con il tema, e con l'eventuale tinta, non è bianco);
        // se l'elemento non sta in una finestra, quello del tema o il bianco.
        var sfondoFinestra = Window.GetWindow(elemento)?.Background;
        var sfondo = new DrawingVisual();
        using (var dc = sfondo.RenderOpen())
            dc.DrawRectangle(
                sfondoFinestra is { } b && b != Brushes.Transparent ? b : elemento.TryFindResource("WindowBackground") as Brush ?? Brushes.White,
                null, new Rect(0, 0, larghezza, altezza));
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
        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false });
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

    private static async Task<ElencoDocumentiViewModel> ElencoConDocumentiAsync(ArchivioDiProva a, int? areaId = null)
    {
        var fatture = await a.Servizio.CreaAreaAsync("Fatture");
        var inps = await a.Servizio.CreaAreaAsync("Previdenza sociale");
        await a.CreaCartellaInAreaAsync(fatture, "Fattura 123", ["Fattura 123.pdf", "scansione.jpg"], new DateOnly(2026, 10, 31));
        await a.CreaCartellaInAreaAsync(inps, "Contributi 2026", ["F24 settembre.pdf", "relazione con un nome davvero molto lungo.docx"]);
        File.Delete(a.Fisico("Fatture", "Fattura 123", "scansione.jpg")); // una riga col file mancante

        var elenco = new ElencoDocumentiViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, areaId is null ? null : fatture);
        await elenco.CaricaAsync();
        return elenco;
    }

    [Fact]
    public async Task ElencoView_TuttiIDocumenti_SiDisegna_ConLaColonnaArea_SenzaErroriDiBinding()
    {
        using var a = new ArchivioDiProva();
        var elenco = await ElencoConDocumentiAsync(a);

        var errori = InSta(() =>
        {
            var vista = new ElencoView { DataContext = elenco };
            Disegna(vista, 780, 360, "elenco-tutti");

            var griglia = FindDataGrid(vista)!;
            Assert.Equal(4, griglia.Items.Count);
            Assert.Equal(Visibility.Visible, griglia.Columns.Single(c => (string)c.Header == "Area").Visibility);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task ElencoView_DiUnArea_NascondeLaColonnaArea()
    {
        using var a = new ArchivioDiProva();
        var elenco = await ElencoConDocumentiAsync(a, areaId: 1);

        var errori = InSta(() =>
        {
            var vista = new ElencoView { DataContext = elenco };
            Disegna(vista, 780, 360, "elenco-area");

            var griglia = FindDataGrid(vista)!;
            Assert.Equal(2, griglia.Items.Count);
            Assert.Equal(Visibility.Collapsed, griglia.Columns.Single(c => (string)c.Header == "Area").Visibility);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task ElencoView_Vuoto_SiDisegna_SenzaErroriDiBinding()
    {
        using var a = new ArchivioDiProva();
        var elenco = new ElencoDocumentiViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, null);
        await elenco.CaricaAsync();

        var errori = InSta(() => Disegna(new ElencoView { DataContext = elenco }, 780, 300, "elenco-vuoto"));

        Assert.Empty(errori);
    }

    [Fact]
    public async Task ElencoView_DoppioClicSuUnaRiga_VaAllaCartella_MaNonSeSiClicSuUnPulsante()
    {
        using var a = new ArchivioDiProva();
        var elenco = await ElencoConDocumentiAsync(a);
        var richieste = new List<int>();
        elenco.VaiAllaCartellaRichiesto += richieste.Add;

        InSta(() =>
        {
            var vista = new ElencoView { DataContext = elenco };
            Disegna(vista, 780, 360, "elenco-doppio-clic");
            var griglia = FindDataGrid(vista)!;
            var riga = (DataGridRow)griglia.ItemContainerGenerator.ContainerFromIndex(0);
            var cartellaDellaRiga = ((DocumentoElencoViewModel)riga.DataContext).CartellaId;

            // Doppio clic sulla riga (sul testo): si va alla cartella.
            var sulTesto = FindFirst<TextBlock>(riga)!;
            sulTesto.RaiseEvent(NuovoDoppioClic());
            Assert.Equal([cartellaDellaRiga], richieste);

            // Doppio clic su un pulsante della riga: nessun cambio di cartella.
            FindFirst<Button>(riga)!.RaiseEvent(NuovoDoppioClic());
            Assert.Single(richieste);

            // Doppio clic sull'intestazione delle colonne: nessun cambio di cartella.
            FindFirst<System.Windows.Controls.Primitives.DataGridColumnHeader>(griglia)!.RaiseEvent(NuovoDoppioClic());
            Assert.Single(richieste);

            // Un solo clic sulla riga non basta.
            var singolo = NuovoDoppioClic();
            typeof(System.Windows.Input.MouseButtonEventArgs).GetProperty("ClickCount")!.GetSetMethod(true)!.Invoke(singolo, [1]);
            sulTesto.RaiseEvent(singolo);
            Assert.Single(richieste);
        });
    }

    /// <summary>Un clic sinistro con ClickCount = 2, come lo genera Windows per il secondo clic di un doppio clic.</summary>
    private static System.Windows.Input.MouseButtonEventArgs NuovoDoppioClic() => NuovoClic(2);

    /// <summary>Un clic sinistro con il numero di clic indicato (2 = doppio clic), generato come evento <paramref name="evento"/> (MouseDown se non indicato).</summary>
    internal static System.Windows.Input.MouseButtonEventArgs NuovoClic(int numeroClic, RoutedEvent? evento = null)
    {
        var clic = new System.Windows.Input.MouseButtonEventArgs(
            System.Windows.Input.Mouse.PrimaryDevice, Environment.TickCount, System.Windows.Input.MouseButton.Left)
        {
            RoutedEvent = evento ?? UIElement.MouseDownEvent
        };
        // ClickCount si può impostare solo dall'interno di WPF.
        typeof(System.Windows.Input.MouseButtonEventArgs)
            .GetProperty(nameof(System.Windows.Input.MouseButtonEventArgs.ClickCount))!
            .GetSetMethod(nonPublic: true)!.Invoke(clic, [numeroClic]);
        return clic;
    }

    internal static T? FindFirst<T>(DependencyObject radice) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(radice); i++)
        {
            var figlio = VisualTreeHelper.GetChild(radice, i);
            if (figlio is T trovato)
                return trovato;
            if (FindFirst<T>(figlio) is { } piuInProfondita)
                return piuInProfondita;
        }
        return null;
    }

    /// <summary>Un archivio con scadenze di ogni urgenza, per vedere colori e icone. "Oggi" è il 1 ottobre 2026.</summary>
    internal static async Task<(MainViewModel Vm, AlertService Avvisi)> ArchivioConAvvisiAsync(ArchivioDiProva a)
    {
        var avvisi = new AlertService(30, 7, true, new TempoFisso(new DateTime(2026, 10, 1)));
        var fatture = await a.Servizio.CreaAreaAsync("Fatture");
        var inps = await a.Servizio.CreaAreaAsync("INPS");
        var agenzia = await a.Servizio.CreaAreaAsync("Agenzia Entrate");
        await a.CreaCartellaInAreaAsync(fatture, "Fattura 123", ["Fattura 123.pdf", "scansione.jpg"], new DateOnly(2026, 10, 4));
        await a.CreaCartellaInAreaAsync(fatture, "Fattura 124", ["Fattura 124.pdf"], new DateOnly(2026, 10, 25));
        await a.CreaCartellaInAreaAsync(inps, "Contributi", ["F24 settembre.pdf"], new DateOnly(2026, 9, 28));
        await a.CreaCartellaInAreaAsync(inps, "Malattia", ["certificato.pdf"], new DateOnly(2027, 3, 1));
        await a.CreaCartellaInAreaAsync(agenzia, "Dichiarazione", ["modello.pdf"]);

        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell,
            new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false }, avvisi);
        await vm.InizializzaAsync();
        await vm.CaricamentoElencoCompletato;
        foreach (var area in vm.Radici[0].Aree)
            area.IsExpanded = true;
        return (vm, avvisi);
    }

    [Fact]
    public async Task FinestraPrincipale_ConAvvisi_MostraIconeNellAlbero_ERigheColorateNellaGriglia()
    {
        using var a = new ArchivioDiProva();
        var (vm, _) = await ArchivioConAvvisiAsync(a);

        var errori = InSta(() =>
        {
            var finestra = new MainWindow(vm);
            Disegna((FrameworkElement)finestra.Content, 1100, 680, "avvisi-radice");
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_NodoScadenze_MostraLElencoDelleCartelleInAvviso()
    {
        using var a = new ArchivioDiProva();
        var (vm, _) = await ArchivioConAvvisiAsync(a);
        vm.Radici[0].Figli.Single(f => f.Tipo == TipoNodo.Scadenze).IsSelected = true;
        await vm.CaricamentoElencoCompletato;

        var errori = InSta(() =>
        {
            var finestra = new MainWindow(vm);
            Disegna((FrameworkElement)finestra.Content, 1100, 680, "avvisi-scadenze");
            Assert.Equal(3, FindDataGrid((FrameworkElement)finestra.Content, "Cartella")!.Items.Count);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task ScadenzeView_DoppioClicSuUnaRiga_VaAllaCartella_MaNonSuUnPulsante()
    {
        using var a = new ArchivioDiProva();
        var (vm, avvisi) = await ArchivioConAvvisiAsync(a);
        var scadenze = new ScadenzeViewModel(a.Servizio, avvisi);
        await scadenze.CaricaAsync();
        var richieste = new List<int>();
        scadenze.VaiAllaCartellaRichiesto += richieste.Add;

        InSta(() =>
        {
            var vista = new ScadenzeView { DataContext = scadenze };
            Disegna(vista, 780, 300, "scadenze-vista");
            var riga = (DataGridRow)FindDataGrid(vista)!.ItemContainerGenerator.ContainerFromIndex(0);

            FindFirst<TextBlock>(riga)!.RaiseEvent(NuovoDoppioClic());
            Assert.Single(richieste);

            FindFirst<Button>(riga)!.RaiseEvent(NuovoDoppioClic());
            Assert.Single(richieste);
        });
    }

    [Fact]
    public async Task CartellaView_ConScadenzaVicina_MostraQuantoManca()
    {
        using var a = new ArchivioDiProva();
        var (_, avvisi) = await ArchivioConAvvisiAsync(a);
        var dettaglio = (await a.Servizio.CaricaScadenzeAsync()).First();
        var form = new CartellaFormViewModel(a.Servizio, a.Files, a.Dialog, a.Shell,
            (await a.Servizio.CaricaCartellaAsync(dettaglio.CartellaId))!, avvisi);

        var errori = InSta(() => Disegna(new CartellaView { DataContext = form }, 780, 480, "form-con-scadenza"));

        Assert.Empty(errori);
        Assert.Equal(StatoAvviso.Rosso, form.StatoScadenza);
    }

    [Fact]
    public async Task FinestraPrincipale_ConLaRadiceSelezionata_MostraLaGrigliaDiTuttiIDocumenti()
    {
        using var a = new ArchivioDiProva();
        var fatture = await a.Servizio.CreaAreaAsync("Fatture");
        await a.CreaCartellaInAreaAsync(fatture, "Fattura 123", ["Fattura 123.pdf", "scansione.jpg"], new DateOnly(2026, 10, 31));
        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false });
        await vm.InizializzaAsync();
        await vm.CaricamentoElencoCompletato;

        var errori = InSta(() =>
        {
            var finestra = new MainWindow(vm);
            Disegna((FrameworkElement)finestra.Content, 1100, 680, "finestra-radice");
            Assert.Equal(2, FindDataGrid((FrameworkElement)finestra.Content)!.Items.Count);
        });

        Assert.Empty(errori);
    }

    /// <summary>La prima griglia nell'albero visuale; con <paramref name="intestazione"/> quella che ha una colonna con quel titolo.</summary>
    internal static DataGrid? FindDataGrid(DependencyObject radice, string? intestazione = null)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(radice); i++)
        {
            var figlio = VisualTreeHelper.GetChild(radice, i);
            if (figlio is DataGrid griglia && (intestazione is null || griglia.Columns.Any(c => c.Header as string == intestazione)))
                return griglia;
            if (FindDataGrid(figlio, intestazione) is { } trovata)
                return trovata;
        }
        return null;
    }
}
