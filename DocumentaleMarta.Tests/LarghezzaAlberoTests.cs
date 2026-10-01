using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;

namespace DocumentaleMarta.Tests;

/// <summary>Aprendo un ramo dell'albero con nomi lunghi, la colonna dell'albero si allarga da sola per mostrarli per intero.</summary>
[Collection("WPF")]
public class LarghezzaAlberoTests
{
    private const string NomeLungo = "Fattura 2026/0147 fornitura carpenteria metallica cantiere via Roma";
    private static readonly string NomeLunghissimo = string.Join(" ", Enumerable.Repeat("verbale di collaudo strutture", 6));

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

    private static async Task<(MainViewModel Vm, ArchivioDiProva Archivio)> PreparaAsync(params string[] titoli)
    {
        var a = new ArchivioDiProva();
        var area = await a.Servizio.CreaAreaAsync("Fatture");
        foreach (var titolo in titoli)
            await a.CreaCartellaInAreaAsync(area, titolo, ["x.pdf"]);

        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false });
        await vm.InizializzaAsync();
        return (vm, a);
    }

    private static NodoAlberoViewModel Area(MainViewModel vm) => vm.Radici.Single().Aree.Single();

    private static double Larghezza(MainWindow finestra) => ((ColumnDefinition)finestra.FindName("ColonnaAlbero")).Width.Value;

    private static void Disegna(FrameworkElement contenuto, int larghezza = 1180, string nome = "albero-larghezza")
    {
        // Due passate: la prima crea i nodi e fa partire l'adattamento, la seconda dispone con la colonna già allargata.
        VisteTests.Disegna(contenuto, larghezza, 600, nome);
        VisteTests.Disegna(contenuto, larghezza, 600, nome);
    }

    private static TextBlock TestoDelNodo(FrameworkElement contenuto, string testo) =>
        Tutti<TextBlock>(Tutti<TreeView>(contenuto).Single()).Single(t => t.Text == testo);

    /// <summary>Dove finisce il testo del nodo, rispetto al bordo sinistro dell'albero.</summary>
    private static double FineDelTesto(FrameworkElement contenuto, string testo)
    {
        var albero = Tutti<TreeView>(contenuto).Single();
        var t = TestoDelNodo(contenuto, testo);
        return t.TransformToAncestor(albero).Transform(new Point(t.ActualWidth, 0)).X;
    }

    private static bool ScorrimentoOrizzontale(FrameworkElement contenuto) =>
        AlberoVisuale.Discendente<ScrollViewer>(Tutti<TreeView>(contenuto).Single())!.ComputedHorizontalScrollBarVisibility == Visibility.Visible;

    // ---------- Si allarga ----------

    [Fact]
    public async Task ConNomiCorti_LaColonnaRestaCom_E()
    {
        var (vm, a) = await PreparaAsync("Fattura 1", "Fattura 2");
        using var _ = a;
        Area(vm).IsExpanded = true;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            Disegna((FrameworkElement)finestra.Content, nome: "albero-nomi-corti");

            Assert.Equal(270, Larghezza(finestra));
            Assert.False(ScorrimentoOrizzontale((FrameworkElement)finestra.Content));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task EspandendoUnRamoConUnNomeLungo_LaColonnaSiAllargaFinoAVederloPerIntero()
    {
        var (vm, a) = await PreparaAsync("Fattura 1", NomeLungo);
        using var _ = a;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            Disegna(contenuto, nome: "albero-ramo-chiuso");
            Assert.Equal(270, Larghezza(finestra)); // ramo chiuso: non si vede il nome lungo

            Area(vm).IsExpanded = true;
            Disegna(contenuto, nome: "albero-ramo-aperto");

            Assert.True(Larghezza(finestra) > 270);
            Assert.Equal(Larghezza(finestra), ((ColumnDefinition)finestra.FindName("ColonnaAlbero")).ActualWidth, 1);
            // Il nome si vede tutto: finisce dentro la colonna e l'albero non ha bisogno di scorrere di lato.
            Assert.True(FineDelTesto(contenuto, NomeLungo) <= Larghezza(finestra), $"il testo finisce a {FineDelTesto(contenuto, NomeLungo)} su {Larghezza(finestra)}");
            Assert.False(ScorrimentoOrizzontale(contenuto));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task LaColonna_NonSiAllargaPiuDelNecessario()
    {
        var (vm, a) = await PreparaAsync(NomeLungo);
        using var _ = a;
        Area(vm).IsExpanded = true;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            Disegna(contenuto, nome: "albero-larghezza-giusta");

            // Tra il fondo del testo e il bordo della colonna c'è poco spazio (aria a destra e margini), non decine di pixel in più.
            var avanzo = Larghezza(finestra) - FineDelTesto(contenuto, NomeLungo);
            Assert.InRange(avanzo, 0, 60);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task RichiudendoIlRamo_LaColonnaTornaAllaLarghezzaDiPartenza()
    {
        var (vm, a) = await PreparaAsync(NomeLungo);
        using var _ = a;
        Area(vm).IsExpanded = true;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            Disegna(contenuto);
            Assert.True(Larghezza(finestra) > 270);

            Area(vm).IsExpanded = false;
            Disegna(contenuto, nome: "albero-ramo-richiuso");

            Assert.Equal(270, Larghezza(finestra));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task ApertoUnRamoConNomeLungo_ILNomeDelloRamoAperto_ComandaLaLarghezza()
    {
        // Con due rami aperti conta il nome più largo, e chiudendo quello largo ci si adatta all'altro.
        var a = new ArchivioDiProva();
        using var _ = a;
        var uno = await a.Servizio.CreaAreaAsync("Fatture");
        var due = await a.Servizio.CreaAreaAsync("INPS");
        await a.CreaCartellaInAreaAsync(uno, NomeLungo, ["x.pdf"]);
        await a.CreaCartellaInAreaAsync(due, "Contributi di settembre dell'anno scorso per i dipendenti", ["y.pdf"]);
        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false });
        await vm.InizializzaAsync();
        var fatture = vm.Radici.Single().Aree.Single(x => x.Nome == "Fatture");
        var inps = vm.Radici.Single().Aree.Single(x => x.Nome == "INPS");
        fatture.IsExpanded = true;
        inps.IsExpanded = true;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            Disegna(contenuto, nome: "albero-due-rami");
            var larghezzaConEntrambi = Larghezza(finestra);

            fatture.IsExpanded = false; // resta aperto solo il ramo con il nome più corto
            Disegna(contenuto);
            var larghezzaConUno = Larghezza(finestra);

            Assert.True(larghezzaConEntrambi > larghezzaConUno);
            Assert.True(larghezzaConUno >= 270);
            Assert.True(FineDelTesto(contenuto, "Contributi di settembre dell'anno scorso per i dipendenti") <= larghezzaConUno);
        });

        Assert.Empty(errori);
    }

    // ---------- Livelli più profondi ----------

    [Fact]
    public async Task UnNomeInUnLivelloPiuProfondo_RichiedePiuLarghezza_PerIlRientro()
    {
        // Lo stesso titolo nell'area (livello 2) e nell'archivio (livello 3, dentro un gruppo).
        var (vmArea, a1) = await PreparaAsync(NomeLungo);
        using var _1 = a1;
        Area(vmArea).IsExpanded = true;

        var a2 = new ArchivioDiProva();
        using var _2 = a2;
        var area = await a2.Servizio.CreaAreaAsync("Fatture");
        var c = await a2.CreaCartellaInAreaAsync(area, NomeLungo, ["x.pdf"]);
        await a2.Servizio.AggiornaCartellaAsync(c.Id, c.Dati with { Completato = true, DataCompletamento = new DateOnly(2026, 10, 1) });
        await a2.Servizio.ArchiviaCartellaAsync(c.Id);
        var vmArchivio = new MainViewModel(a2.Servizio, a2.Files, a2.Dialog, a2.Shell, new ImpostazioniApp { PercorsoRadice = a2.Radice, RiepilogoAvvio = false });
        await vmArchivio.InizializzaAsync();
        var archivio = vmArchivio.Radici.Single().Figli.Last();
        archivio.IsExpanded = true;
        archivio.Figli.Single().IsExpanded = true;

        double larghezzaArea = 0, larghezzaArchivio = 0;
        var errori = VisteTests.InSta(() =>
        {
            var f1 = new MainWindow(vmArea);
            Disegna((FrameworkElement)f1.Content, nome: "albero-livello-area");
            larghezzaArea = Larghezza(f1);

            var f2 = new MainWindow(vmArchivio);
            Disegna((FrameworkElement)f2.Content, nome: "albero-livello-archivio");
            larghezzaArchivio = Larghezza(f2);
            Assert.False(ScorrimentoOrizzontale((FrameworkElement)f2.Content));
        });

        Assert.Empty(errori);
        Assert.True(larghezzaArchivio > larghezzaArea, $"archivio {larghezzaArchivio} area {larghezzaArea}");
        Assert.InRange(larghezzaArchivio - larghezzaArea, 10, 45); // un livello di rientro in più
    }

    // ---------- I limiti ----------

    [Fact]
    public async Task ConUnNomeLunghissimo_LaColonnaNonSuperaMetaFinestra_EL_AlberoScorre()
    {
        var (vm, a) = await PreparaAsync(NomeLunghissimo);
        using var _ = a;
        Area(vm).IsExpanded = true;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            Disegna(contenuto, 1180, "albero-nome-lunghissimo");

            Assert.Equal(1180 * 0.5, Larghezza(finestra), 0);
            Assert.True(ScorrimentoOrizzontale(contenuto)); // il resto del nome si vede scorrendo
            // Al contenuto resta il suo spazio.
            Assert.True(((ColumnDefinition)finestra.FindName("ColonnaContenuto")).ActualWidth >= 300);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task InUnaFinestraStretta_ILContenutoConservaIlSuoSpazioMinimo()
    {
        var (vm, a) = await PreparaAsync(NomeLungo);
        using var _ = a;
        Area(vm).IsExpanded = true;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            Disegna(contenuto, 800, "albero-finestra-stretta");

            Assert.True(Larghezza(finestra) <= 800 * 0.5 + 1);
            Assert.True(((ColumnDefinition)finestra.FindName("ColonnaContenuto")).ActualWidth >= 300);
            Assert.True(Larghezza(finestra) >= 200); // mai sotto il minimo dell'albero
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task CollAnteprimaAperta_ILContenutoEL_AnteprimaConservanoIlLoroSpazio()
    {
        var a = new ArchivioDiProva();
        using var _ = a;
        var area = await a.Servizio.CreaAreaAsync("Fatture");
        await a.CreaCartellaInAreaAsync(area, NomeLunghissimo, ["x.pdf"]);
        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false },
            generatoreAnteprima: new FintoGeneratoreAnteprima());
        await vm.InizializzaAsync();
        vm.Radici.Single().Aree.Single().IsExpanded = true;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            Disegna(contenuto, 1180, "albero-con-anteprima");

            var anteprima = ((ColumnDefinition)finestra.FindName("ColonnaAnteprima")).ActualWidth;
            var contenutoCol = ((ColumnDefinition)finestra.FindName("ColonnaContenuto")).ActualWidth;
            Assert.True(anteprima > 0);
            Assert.True(contenutoCol >= 300);
            Assert.True(Larghezza(finestra) + 5 + contenutoCol + anteprima <= 1180 + 1);
        });

        Assert.Empty(errori);
    }

    // ---------- La larghezza scelta dall'utente ----------

    [Fact]
    public async Task SeLUtenteHaTrascinatoIlDivisore_QuellaLarghezzaDiventaQuellaDiPartenza()
    {
        var (vm, a) = await PreparaAsync(NomeLungo);
        using var _ = a;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            Disegna(contenuto);

            // L'utente porta il divisore a 400 pixel (con il mouse: qui si imposta la larghezza e si segnala la fine del trascinamento).
            ((ColumnDefinition)finestra.FindName("ColonnaAlbero")).Width = new GridLength(400);
            Disegna(contenuto);
            ((GridSplitter)finestra.FindName("Divisore")).RaiseEvent(new DragCompletedEventArgs(0, 0, canceled: false));

            Area(vm).IsExpanded = true;      // il nome lungo ha bisogno di più di 400
            Disegna(contenuto, nome: "albero-scelta-utente-aperto");
            var aperto = Larghezza(finestra);
            Assert.True(aperto > 400);

            Area(vm).IsExpanded = false;     // si richiude: si torna ai 400 scelti, non ai 270 di partenza
            Disegna(contenuto, nome: "albero-scelta-utente-chiuso");
            Assert.Equal(400, Larghezza(finestra));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task SeLUtenteHaGiaAllargatoAbbastanza_AprireUnRamoNonCambiaNulla()
    {
        var (vm, a) = await PreparaAsync(NomeLungo);
        using var _ = a;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            Disegna(contenuto);
            ((ColumnDefinition)finestra.FindName("ColonnaAlbero")).Width = new GridLength(560);
            Disegna(contenuto);
            ((GridSplitter)finestra.FindName("Divisore")).RaiseEvent(new DragCompletedEventArgs(0, 0, canceled: false));

            Area(vm).IsExpanded = true;
            Disegna(contenuto);

            Assert.Equal(560, Larghezza(finestra)); // il nome già ci sta: la scelta dell'utente non si tocca
            Assert.False(ScorrimentoOrizzontale(contenuto));
        });

        Assert.Empty(errori);
    }

    // ---------- Altri momenti ----------

    [Fact]
    public async Task AllApertura_ConIRamiGiaAperti_LaColonnaSiAdattaSubito()
    {
        var (vm, a) = await PreparaAsync(NomeLungo);
        using var _ = a;
        Area(vm).IsExpanded = true; // com'è dopo aver ricaricato un albero che aveva i rami aperti

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            Disegna((FrameworkElement)finestra.Content, nome: "albero-apertura");

            Assert.True(Larghezza(finestra) > 270);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task ILMetodoPubblico_PuoEssereChiamatoQuandoSiVuole_SenzaDanni()
    {
        var (vm, a) = await PreparaAsync(NomeLungo);
        using var _ = a;
        Area(vm).IsExpanded = true;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            Disegna(contenuto);
            var prima = Larghezza(finestra);

            finestra.AdattaLarghezzaAlbero();
            finestra.AdattaLarghezzaAlbero();
            Disegna(contenuto);

            Assert.Equal(prima, Larghezza(finestra), 1); // stabile: non cresce a ogni chiamata
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task SenzaNessunNodoDisposto_NonSuccedeNulla()
    {
        var (vm, a) = await PreparaAsync(NomeLungo);
        using var _ = a;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            finestra.AdattaLarghezzaAlbero(); // prima di qualsiasi disposizione: niente da misurare

            Assert.Equal(270, Larghezza(finestra));
        });

        Assert.Empty(errori);
    }
}
