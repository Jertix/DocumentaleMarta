using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Data;

namespace DocumentaleMarta.Tests;

/// <summary>I calcoli dello zoom della finestra ingrandita (funzioni pure).</summary>
public class IngrandimentoTests
{
    [Fact]
    public void PaginaIntera_DiUnaPaginaAlta_LimitataDallAltezza_ConIMargini()
    {
        // Pagina 1000x1400 in un'area 1000x800: 774 di altezza utile (800 meno 2*12 di margine e 2 di bordo).
        var d = Ingrandimento.Dimensioni(new Size(1000, 1400), new Size(1000, 800), 1);

        Assert.Equal(774, d.Height, 3);
        Assert.Equal(1000.0 / 1400 * 774, d.Width, 3); // le proporzioni restano
    }

    [Fact]
    public void PaginaIntera_DiUnaPaginaLarga_LimitataDallaLarghezza()
    {
        var d = Ingrandimento.Dimensioni(new Size(2000, 1000), new Size(800, 800), 1);

        Assert.Equal(774, d.Width, 3);
        Assert.Equal(387, d.Height, 3);
    }

    [Fact]
    public void ConLoZoom_LaPaginaSiIngrandisceInProporzione()
    {
        var intera = Ingrandimento.Dimensioni(new Size(1000, 1400), new Size(1000, 800), 1);
        var doppia = Ingrandimento.Dimensioni(new Size(1000, 1400), new Size(1000, 800), 2);

        Assert.Equal(intera.Width * 2, doppia.Width, 6);
        Assert.Equal(intera.Height * 2, doppia.Height, 6);
    }

    [Fact]
    public void UnaPaginaPiccola_SiIngrandisceFinoAdAdattarsi()
    {
        var d = Ingrandimento.Dimensioni(new Size(100, 140), new Size(1000, 800), 1);

        Assert.Equal(774, d.Height, 3); // è un'anteprima: riempie la finestra
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(0, 0)]
    public void UnaImmagineSenzaDimensioni_DaVuoto(double larghezza, double altezza) =>
        Assert.True(Ingrandimento.Dimensioni(new Size(larghezza, altezza), new Size(800, 600), 1).IsEmpty);

    [Fact]
    public void UnAreaMinuscola_NonDaDivisioniPerZero()
    {
        var d = Ingrandimento.Dimensioni(new Size(500, 700), new Size(0, 0), 1);

        Assert.True(d.Width > 0 && d.Height > 0);
        Assert.False(double.IsNaN(d.Width));
    }

    [Fact]
    public void LoZoomNonScendeSottoLaPaginaIntera_NeSaleOltreIlMassimo()
    {
        Assert.Equal(1.0, Ingrandimento.Limita(0.2));
        Assert.Equal(1.0, Ingrandimento.Limita(-3));
        Assert.Equal(8.0, Ingrandimento.Limita(50));
        Assert.Equal(1.0, Ingrandimento.Diminuisci(1.0));
        Assert.Equal(8.0, Ingrandimento.Aumenta(8.0));
    }

    [Fact]
    public void PiuEMeno_SiCompensano_ASalti()
    {
        var z = 1.0;
        z = Ingrandimento.Aumenta(z);
        Assert.Equal(1.25, z);
        z = Ingrandimento.Aumenta(z);
        Assert.Equal(1.5625, z);
        z = Ingrandimento.Diminuisci(z);
        Assert.Equal(1.25, z);
        z = Ingrandimento.Diminuisci(z);
        Assert.Equal(1.0, z);
    }

    [Fact]
    public void ILDoppioClic_AlternaTraPaginaInteraEDoppio()
    {
        Assert.Equal(2.0, Ingrandimento.AlternaDoppioClic(1.0));
        Assert.Equal(1.0, Ingrandimento.AlternaDoppioClic(2.0));
        Assert.Equal(1.0, Ingrandimento.AlternaDoppioClic(1.25)); // già ingrandita: si torna alla pagina intera
    }

    [Theory]
    [InlineData(1.0, "Pagina intera")]
    [InlineData(1.25, "125%")]
    [InlineData(2.0, "200%")]
    [InlineData(8.0, "800%")]
    [InlineData(0.5, "Pagina intera")]
    public void IlTestoDelloZoom_EInItaliano(double zoom, string atteso) =>
        Assert.Equal(atteso, Ingrandimento.Testo(zoom));
}

[Collection("WPF")]
public class AnteprimaIngranditaViewModelTests : IDisposable
{
    private record Doc(string NomeFile, string PercorsoRelativo) : IDocumentoAnteprima;

    private readonly CartellaTemporanea _tmp = new();
    private readonly ArchivioFileService _files;
    private readonly FintoGeneratoreAnteprima _generatore = new() { Pagine = 3 };
    private readonly List<AnteprimaViewModel> _aperte = [];
    private readonly AnteprimaViewModel _vm;

    public AnteprimaIngranditaViewModelTests()
    {
        _files = new ArchivioFileService(_tmp.Combina("Documentale"), usaCestino: false);
        _vm = new AnteprimaViewModel(_generatore, _files, mostraIngrandita: _aperte.Add) { Ritardo = TimeSpan.Zero };
    }

    public void Dispose() => _tmp.Dispose();

    private Doc Documento(string nome = "fattura.pdf")
    {
        var relativo = Path.Combine("Fatture", "F", nome);
        var percorso = _files.PercorsoAssoluto(relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);
        File.WriteAllText(percorso, "contenuto");
        return new Doc(nome, relativo);
    }

    // ---------- Quando si può ingrandire ----------

    [Fact]
    public async Task SenzaUnDocumento_NonSiPuoIngrandire()
    {
        Assert.False(_vm.IngrandisciCommand.CanExecute(null));

        _vm.Mostra(Documento());
        await _vm.Completamento;
        _vm.Svuota();

        Assert.False(_vm.IngrandisciCommand.CanExecute(null));
    }

    [Fact]
    public async Task ConUnImmagineDaVedere_SiPuoIngrandire_ELoStatoDelComandoSegueLImmagine()
    {
        var cambi = 0;
        _vm.IngrandisciCommand.CanExecuteChanged += (_, _) => cambi++;

        _vm.Mostra(Documento());
        await _vm.Completamento;

        Assert.True(_vm.IngrandisciCommand.CanExecute(null));
        Assert.True(cambi > 0);
    }

    [Fact]
    public async Task UnFormatoSenzaAnteprima_NonSiPuoIngrandire()
    {
        _generatore.Comportamento = (_, _, _) => Task.FromResult(RisultatoAnteprima.NonDisponibile("niente anteprima"));

        _vm.Mostra(Documento("lettera.docx"));
        await _vm.Completamento;

        Assert.False(_vm.IngrandisciCommand.CanExecute(null));
    }

    [Fact]
    public async Task UnErrore_NonSiPuoIngrandire()
    {
        _generatore.Comportamento = (_, _, _) => Task.FromResult(RisultatoAnteprima.InErrore("rovinato"));

        _vm.Mostra(Documento());
        await _vm.Completamento;

        Assert.False(_vm.IngrandisciCommand.CanExecute(null));
    }

    [Fact]
    public async Task SenzaChiSaAprireLaFinestra_NonSiPuoIngrandire()
    {
        var senza = new AnteprimaViewModel(_generatore, _files) { Ritardo = TimeSpan.Zero };

        senza.Mostra(Documento());
        await senza.Completamento;

        Assert.True(senza.HaImmagine);
        Assert.False(senza.IngrandisciCommand.CanExecute(null));
    }

    // ---------- Cosa succede ingrandendo ----------

    [Fact]
    public async Task Ingrandendo_SiApreLaFinestraConLaStessaPagina_AUnaRisoluzionePiuAlta()
    {
        _vm.Mostra(Documento("contratto.pdf"));
        await _vm.Completamento;

        _vm.IngrandisciCommand.Execute(null);

        var ingrandita = Assert.Single(_aperte);
        await ingrandita.Completamento;
        Assert.Equal("contratto.pdf", ingrandita.Titolo);
        Assert.True(ingrandita.HaImmagine);
        Assert.Equal(StatoAnteprima.Pronta, ingrandita.Stato);
        // Il pannello ha chiesto la larghezza normale, la finestra grande quella alta.
        Assert.Equal([GeneratoreAnteprima.LarghezzaMassima, GeneratoreAnteprima.LarghezzaIngrandita], _generatore.Larghezze);
        Assert.True(GeneratoreAnteprima.LarghezzaIngrandita > GeneratoreAnteprima.LarghezzaMassima);
    }

    [Fact]
    public async Task LaFinestraGrande_PartiDallaPaginaChePrimaSiGuardava()
    {
        _vm.Mostra(Documento());
        await _vm.Completamento;
        _vm.PaginaSuccessivaCommand.Execute(null);
        await _vm.Completamento;
        _vm.PaginaSuccessivaCommand.Execute(null);
        await _vm.Completamento; // pagina 3 di 3

        _vm.IngrandisciCommand.Execute(null);
        var ingrandita = Assert.Single(_aperte);
        await ingrandita.Completamento;

        Assert.Equal((_generatore.Richieste[^1].Percorso, 2), (_generatore.Richieste[^1].Percorso, _generatore.Richieste[^1].Pagina));
        Assert.Equal("Pagina 3 di 3", ingrandita.TestoPagina);
    }

    [Fact]
    public async Task LaFinestraGrande_SiSfogliaDaSola_SenzaToccareIlPannello()
    {
        _vm.Mostra(Documento());
        await _vm.Completamento;
        _vm.IngrandisciCommand.Execute(null);
        var ingrandita = Assert.Single(_aperte);
        await ingrandita.Completamento;

        ingrandita.PaginaSuccessivaCommand.Execute(null);
        await ingrandita.Completamento;

        Assert.Equal("Pagina 2 di 3", ingrandita.TestoPagina);
        Assert.Equal("Pagina 1 di 3", _vm.TestoPagina);
        Assert.Equal(GeneratoreAnteprima.LarghezzaIngrandita, _generatore.Larghezze[^1]); // anche le altre pagine, nitide
    }

    [Fact]
    public async Task LaFinestraGrande_NonAspettaIlRitardoDelPannello()
    {
        _vm.Ritardo = TimeSpan.FromSeconds(30); // il pannello, scorrendo l'elenco, aspetta
        _vm.Mostra(Documento());
        // Il pannello sta ancora aspettando: ma c'è già un'immagine? No. Si prepara una situazione con immagine già pronta:
        _vm.Ritardo = TimeSpan.Zero;
        _vm.Mostra(Documento("x.pdf"));
        await _vm.Completamento;
        _vm.Ritardo = TimeSpan.FromSeconds(30);

        _vm.IngrandisciCommand.Execute(null);
        var ingrandita = Assert.Single(_aperte);

        await ingrandita.Completamento.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.True(ingrandita.HaImmagine);
        Assert.Equal(TimeSpan.Zero, ingrandita.Ritardo);
    }

    [Fact]
    public async Task LaFinestraGrande_NonPuoIngrandireAncora()
    {
        _vm.Mostra(Documento());
        await _vm.Completamento;
        _vm.IngrandisciCommand.Execute(null);
        var ingrandita = Assert.Single(_aperte);
        await ingrandita.Completamento;

        Assert.False(ingrandita.IngrandisciCommand.CanExecute(null));
    }

    [Fact]
    public async Task ChiudendoLaFinestra_SiInterrompeIlDisegnoInCorso()
    {
        var via = new TaskCompletionSource<RisultatoAnteprima>();
        _vm.Mostra(Documento());
        await _vm.Completamento;
        _generatore.Comportamento = (_, _, _) => via.Task;
        _vm.IngrandisciCommand.Execute(null);
        var ingrandita = Assert.Single(_aperte);

        ingrandita.Visibile = false; // come fa la finestra alla chiusura
        via.SetResult(new RisultatoAnteprima(StatoAnteprima.Pronta, FintoGeneratoreAnteprima.ImmagineFinta, 3));
        await Task.Delay(50);

        Assert.False(ingrandita.HaImmagine); // la risposta in ritardo non conta più
    }

    [Fact]
    public async Task UnaPaginaNegativa_ComeInizio_ConDiventaLaPrima()
    {
        _vm.Mostra(Documento(), pagina: -4);
        await _vm.Completamento;

        Assert.Equal(0, _generatore.Richieste.Single().Pagina);
    }

    [Fact]
    public async Task ILPannello_ResteComeEraDopoAverIngrandito()
    {
        _vm.Mostra(Documento("a.pdf"));
        await _vm.Completamento;

        _vm.IngrandisciCommand.Execute(null);

        Assert.Equal("a.pdf", _vm.Titolo);
        Assert.Equal(StatoAnteprima.Pronta, _vm.Stato);
        Assert.True(_vm.HaImmagine);
        Assert.True(_vm.IngrandisciCommand.CanExecute(null)); // si può ingrandire di nuovo
    }
}

/// <summary>Con il generatore vero: l'immagine ingrandita è davvero più grande.</summary>
[Collection("WPF")]
public class GeneratoreAnteprimaLarghezzaTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();
    private readonly GeneratoreAnteprima _generatore = new();

    public void Dispose() => _tmp.Dispose();

    private Task<RisultatoAnteprima> Genera(string percorso, int larghezza) =>
        Task.Run(() => _generatore.GeneraAsync(percorso, 0, CancellationToken.None, larghezza));

    [Fact]
    public async Task UnPdf_SiDisegnaAllaLarghezzaChiesta()
    {
        var pdf = FileDiProva.Pdf(_tmp.Combina("a.pdf"), "Fattura numero 123");

        var normale = (BitmapSource)(await Genera(pdf, GeneratoreAnteprima.LarghezzaMassima)).Immagine!;
        var grande = (BitmapSource)(await Genera(pdf, GeneratoreAnteprima.LarghezzaIngrandita)).Immagine!;

        Assert.Equal(GeneratoreAnteprima.LarghezzaMassima, normale.PixelWidth);
        Assert.Equal(GeneratoreAnteprima.LarghezzaIngrandita, grande.PixelWidth);
        Assert.True(grande.PixelHeight > normale.PixelHeight);
    }

    [Fact]
    public async Task SenzaIndicareLaLarghezza_ValeQuellaDelPannello()
    {
        var pdf = FileDiProva.Pdf(_tmp.Combina("a.pdf"), "x");

        var immagine = (BitmapSource)(await Task.Run(() => _generatore.GeneraAsync(pdf, 0, CancellationToken.None))).Immagine!;

        Assert.Equal(GeneratoreAnteprima.LarghezzaMassima, immagine.PixelWidth);
    }

    [Fact]
    public async Task UnaFotoGrande_SiRimpicciolisceSoloOltreLaLarghezzaChiesta()
    {
        var sorgente = BitmapSource.Create(3200, 100, 96, 96, PixelFormats.Gray8, null, new byte[3200 * 100], 3200);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(sorgente));
        var percorso = _tmp.Combina("foto.png");
        using (var file = File.Create(percorso))
            encoder.Save(file);

        var normale = (BitmapSource)(await Genera(percorso, GeneratoreAnteprima.LarghezzaMassima)).Immagine!;
        var grande = (BitmapSource)(await Genera(percorso, GeneratoreAnteprima.LarghezzaIngrandita)).Immagine!;
        var enorme = (BitmapSource)(await Genera(percorso, 4000)).Immagine!;

        Assert.Equal(1100, normale.PixelWidth);
        Assert.Equal(2600, grande.PixelWidth);
        Assert.Equal(3200, enorme.PixelWidth); // mai ingrandita oltre l'originale
    }

    [Fact]
    public async Task LaMiniaturaDiUnDocumentoOpenOffice_NonSiIngrandisceOltreLOriginale()
    {
        var odt = OpenDocumentDiProva.Odt(_tmp.Combina("a.odt"), ["x"], miniatura: OpenDocumentDiProva.PngDiProva(128, 180));

        var risultato = await Genera(odt, GeneratoreAnteprima.LarghezzaIngrandita);

        Assert.Equal(128, ((BitmapSource)risultato.Immagine!).PixelWidth);
    }
}

/// <summary>Il doppio clic sul pannello, il pulsante e la finestra grande.</summary>
[Collection("WPF")]
public class AnteprimaIngranditaVisteTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();
    private readonly ArchivioFileService _files;
    private readonly List<AnteprimaViewModel> _aperte = [];

    public AnteprimaIngranditaVisteTests() =>
        _files = new ArchivioFileService(_tmp.Combina("Documentale"), usaCestino: false);

    public void Dispose() => _tmp.Dispose();

    private sealed record Doc(string NomeFile, string PercorsoRelativo) : IDocumentoAnteprima;

    private Doc PdfVero(string nome, params string[] pagine)
    {
        var relativo = Path.Combine("Fatture", nome);
        var percorso = _files.PercorsoAssoluto(relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);
        FileDiProva.Pdf(percorso, pagine);
        return new Doc(nome, relativo);
    }

    private async Task<AnteprimaViewModel> PannelloPronto(params string[] pagine)
    {
        var vm = new AnteprimaViewModel(new GeneratoreAnteprima(), _files, mostraIngrandita: _aperte.Add) { Ritardo = TimeSpan.Zero };
        vm.Mostra(PdfVero("fattura.pdf", pagine.Length == 0 ? ["Fattura numero 123", "Seconda pagina"] : pagine));
        await vm.Completamento;
        return vm;
    }

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

    // ---------- Il pannello ----------

    [Fact]
    public async Task IlDoppioClicSuLImmagine_ApreLaFinestraGrande_UnClicSoloNo()
    {
        var vm = await PannelloPronto();

        var errori = VisteTests.InSta(() =>
        {
            var pannello = new AnteprimaView { DataContext = vm, Width = 320, Height = 520 };
            VisteTests.Disegna(pannello, 320, 520, "anteprima-con-pulsante-ingrandisci");
            var immagine = Tutti<Border>(pannello).First(b => b.Child is Image);

            immagine.RaiseEvent(VisteTests.NuovoClic(1, UIElement.MouseLeftButtonDownEvent));
            Assert.Empty(_aperte);

            immagine.RaiseEvent(VisteTests.NuovoClic(2, UIElement.MouseLeftButtonDownEvent));
            Assert.Single(_aperte);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task IlPulsanteIngrandisci_CompareConLImmagine_EFaLoStesso()
    {
        var vm = await PannelloPronto();

        var errori = VisteTests.InSta(() =>
        {
            var pannello = new AnteprimaView { DataContext = vm };
            VisteTests.Disegna(pannello, 320, 520, "anteprima-pannello");
            var pulsante = Tutti<Button>(pannello).Single(b => (string?)System.Windows.Automation.AutomationProperties.GetName(b) == "Ingrandisci l'anteprima");

            Assert.True(pulsante.IsEnabled);
            Assert.Same(vm.IngrandisciCommand, pulsante.Command);
            Assert.Contains("doppio clic", (string)pulsante.ToolTip);
            Assert.Equal(Visibility.Visible, pulsante.Visibility);
            Assert.Contains("Doppio clic per ingrandire", Tutti<Border>(pannello).Select(b => b.ToolTip).OfType<string>());
        });

        Assert.Empty(errori);
    }

    [Fact]
    public void SenzaImmagine_IlPulsanteIngrandisciNonSiVede()
    {
        var vm = new AnteprimaViewModel(new GeneratoreAnteprima(), _files, mostraIngrandita: _aperte.Add);

        var errori = VisteTests.InSta(() =>
        {
            var pannello = new AnteprimaView { DataContext = vm };
            VisteTests.Disegna(pannello, 320, 520, "anteprima-vuota-senza-pulsante");

            var pulsante = Tutti<Button>(pannello).Single(b => (string?)System.Windows.Automation.AutomationProperties.GetName(b) == "Ingrandisci l'anteprima");
            Assert.Equal(Visibility.Collapsed, pulsante.Visibility);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task IlDoppioClic_SenzaImmagine_NonFaNulla()
    {
        var vm = new AnteprimaViewModel(new GeneratoreAnteprima(), _files, mostraIngrandita: _aperte.Add);
        await Task.CompletedTask;

        var errori = VisteTests.InSta(() =>
        {
            var pannello = new AnteprimaView { DataContext = vm };
            VisteTests.Disegna(pannello, 320, 520, "anteprima-vuota");
            // Il riquadro dell'immagine è nascosto: l'evento lo si manda comunque per vedere che non succeda nulla.
            Tutti<Border>(pannello).First(b => b.Child is Image).RaiseEvent(VisteTests.NuovoClic(2, UIElement.MouseLeftButtonDownEvent));
        });

        Assert.Empty(errori);
        Assert.Empty(_aperte);
    }

    // ---------- La finestra grande ----------

    private async Task<AnteprimaViewModel> FinestraPronta(params string[] pagine)
    {
        var pannello = await PannelloPronto(pagine);
        pannello.IngrandisciCommand.Execute(null);
        var ingrandita = _aperte.Single();
        await ingrandita.Completamento;
        return ingrandita;
    }

    [Fact]
    public async Task LaFinestraGrande_SiApreSullaPaginaIntera_ConIlNomeDelDocumento()
    {
        var modello = await FinestraPronta();

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new AnteprimaIngranditaDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1100, 780, "anteprima-ingrandita");

            var immagine = (Image)finestra.FindName("Immagine")!;
            Assert.NotNull(immagine.Source);
            Assert.Equal(GeneratoreAnteprima.LarghezzaIngrandita, ((BitmapSource)immagine.Source).PixelWidth); // disegnata nitida
            // Pagina intera: sta tutta nell'area visibile (780 - barra), senza deformarsi.
            var scorrimento = (ScrollViewer)finestra.FindName("Scorrimento")!;
            Assert.True(immagine.Width <= scorrimento.ActualWidth && immagine.Height <= scorrimento.ActualHeight);
            // E non sforando nemmeno con margine e bordo: a pagina intera non compaiono le barre di scorrimento.
            Assert.Equal(Visibility.Collapsed, scorrimento.ComputedVerticalScrollBarVisibility);
            Assert.Equal(Visibility.Collapsed, scorrimento.ComputedHorizontalScrollBarVisibility);
            Assert.Equal(immagine.Source.Width / immagine.Source.Height, immagine.Width / immagine.Height, 3);

            Assert.Equal("fattura.pdf  —  anteprima ingrandita", finestra.Title);
            Assert.Equal(1.0, finestra.Zoom);
            Assert.Equal("Pagina intera", ((TextBlock)finestra.FindName("TestoZoom")!).Text);
            Assert.False(((Button)finestra.FindName("PulsanteMeno")!).IsEnabled);   // già a pagina intera
            Assert.True(((Button)finestra.FindName("PulsantePiu")!).IsEnabled);
            Assert.False(((Button)finestra.FindName("PulsanteIntera")!).IsEnabled);
            Assert.Contains("Pagina 1 di 2", Tutti<TextBlock>(contenuto).Where(t => t.IsVisible || t.Visibility == Visibility.Visible).Select(t => t.Text));
        });

        Assert.Empty(errori);
        modello.Visibile = false;
    }

    [Fact]
    public async Task Ingrandendo_LaPaginaCresce_EIPulsantiSiAdeguano()
    {
        var modello = await FinestraPronta();

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new AnteprimaIngranditaDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1100, 780, "anteprima-ingrandita-intera");
            var immagine = (Image)finestra.FindName("Immagine")!;
            var larghezzaIntera = immagine.Width;

            ((Button)finestra.FindName("PulsantePiu")!).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            VisteTests.Disegna(contenuto, 1100, 780, "anteprima-ingrandita-125");

            Assert.Equal(1.25, finestra.Zoom);
            Assert.Equal("125%", ((TextBlock)finestra.FindName("TestoZoom")!).Text);
            Assert.Equal(larghezzaIntera * 1.25, immagine.Width, 3);
            Assert.True(((Button)finestra.FindName("PulsanteMeno")!).IsEnabled);
            Assert.True(((Button)finestra.FindName("PulsanteIntera")!).IsEnabled);

            ((Button)finestra.FindName("PulsanteIntera")!).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            Assert.Equal(1.0, finestra.Zoom);
            Assert.Equal(larghezzaIntera, immagine.Width, 3);
        });

        Assert.Empty(errori);
        modello.Visibile = false;
    }

    [Fact]
    public async Task IlDoppioClicSullaPagina_IngrandisceETornaAllaPaginaIntera()
    {
        var modello = await FinestraPronta();

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new AnteprimaIngranditaDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1100, 780, "anteprima-ingrandita-doppio-clic");
            var pagina = (Border)finestra.FindName("Pagina")!;

            pagina.RaiseEvent(VisteTests.NuovoClic(2, UIElement.MouseLeftButtonDownEvent));
            Assert.Equal(2.0, finestra.Zoom);
            Assert.Equal("200%", ((TextBlock)finestra.FindName("TestoZoom")!).Text);
            VisteTests.Disegna(contenuto, 1100, 780, "anteprima-ingrandita-200");

            pagina.RaiseEvent(VisteTests.NuovoClic(2, UIElement.MouseLeftButtonDownEvent));
            Assert.Equal(1.0, finestra.Zoom);
        });

        Assert.Empty(errori);
        modello.Visibile = false;
    }

    [Fact]
    public async Task LaTastiera_CambiaLoZoom_EEscChiude()
    {
        var modello = await FinestraPronta();

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new AnteprimaIngranditaDialog(modello);
            VisteTests.Disegna((FrameworkElement)finestra.Content, 1100, 780, "anteprima-ingrandita-tastiera");

            Assert.True(finestra.GestisciTasto(Key.OemPlus, ModifierKeys.None));
            Assert.Equal(1.25, finestra.Zoom);
            Assert.True(finestra.GestisciTasto(Key.Add, ModifierKeys.None));
            Assert.Equal(1.5625, finestra.Zoom);
            Assert.True(finestra.GestisciTasto(Key.OemMinus, ModifierKeys.None));
            Assert.Equal(1.25, finestra.Zoom);
            Assert.True(finestra.GestisciTasto(Key.D0, ModifierKeys.Control));
            Assert.Equal(1.0, finestra.Zoom);
            Assert.True(finestra.GestisciTasto(Key.Add, ModifierKeys.Control));
            Assert.Equal(1.25, finestra.Zoom);

            Assert.False(finestra.GestisciTasto(Key.A, ModifierKeys.None));          // gli altri tasti non si toccano
            Assert.False(finestra.GestisciTasto(Key.Escape, ModifierKeys.Shift));    // con un modificatore, Esc non chiude

            Assert.True(finestra.GestisciTasto(Key.Escape, ModifierKeys.None));      // Esc chiude
        });

        Assert.Empty(errori);
        modello.Visibile = false;
    }

    [Fact]
    public async Task CtrlERotellina_ZoomAno_LaRotellinaDaSolaNo()
    {
        var modello = await FinestraPronta();

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new AnteprimaIngranditaDialog(modello);
            VisteTests.Disegna((FrameworkElement)finestra.Content, 1100, 780, "anteprima-ingrandita-rotellina");

            Assert.False(finestra.GestisciRotellina(120, ModifierKeys.None)); // scorre soltanto
            Assert.Equal(1.0, finestra.Zoom);

            Assert.True(finestra.GestisciRotellina(120, ModifierKeys.Control));
            Assert.Equal(1.25, finestra.Zoom);
            Assert.True(finestra.GestisciRotellina(-120, ModifierKeys.Control));
            Assert.Equal(1.0, finestra.Zoom);
            Assert.True(finestra.GestisciRotellina(-120, ModifierKeys.Control)); // gestita anche al minimo
            Assert.Equal(1.0, finestra.Zoom);
        });

        Assert.Empty(errori);
        modello.Visibile = false;
    }

    [Fact]
    public async Task LeFrecce_SfoglianoLePagine_SoloAPaginaIntera()
    {
        var modello = await FinestraPronta("Uno", "Due");

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new AnteprimaIngranditaDialog(modello);
            VisteTests.Disegna((FrameworkElement)finestra.Content, 1100, 780, "anteprima-ingrandita-frecce");

            // Ingrandita, le frecce fanno scorrere la pagina: non sono gestite dalla finestra (e non cambiano pagina).
            finestra.GestisciTasto(Key.OemPlus, ModifierKeys.None);
            Assert.False(finestra.GestisciTasto(Key.Right, ModifierKeys.None));
            Assert.False(finestra.GestisciTasto(Key.PageDown, ModifierKeys.None));

            // A pagina intera sì: il comando parte (la pagina nuova arriva in modo asincrono, qui non si aspetta).
            finestra.GestisciTasto(Key.D0, ModifierKeys.Control);
            Assert.True(finestra.GestisciTasto(Key.Right, ModifierKeys.None));
            finestra.DataContext = null; // si scollega: la pagina nuova arriverà su un altro thread
        });

        Assert.Empty(errori);
        await modello.Completamento;
        Assert.Equal("Pagina 2 di 2", modello.TestoPagina);
        modello.Visibile = false;
    }

    [Fact]
    public async Task UnDocumentoDiUnaSolaPagina_NonHaIPulsantiDelleFrecce_NeLeFrecceFannoNulla()
    {
        var modello = await FinestraPronta("Unica");

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new AnteprimaIngranditaDialog(modello);
            VisteTests.Disegna((FrameworkElement)finestra.Content, 1100, 780, "anteprima-ingrandita-una-pagina");

            Assert.False(finestra.GestisciTasto(Key.Right, ModifierKeys.None));
            Assert.False(finestra.GestisciTasto(Key.Left, ModifierKeys.None));
        });

        Assert.Empty(errori);
        modello.Visibile = false;
    }

    [Fact]
    public void SenzaImmagine_LaFinestraGrandeDicePerche()
    {
        var modello = new AnteprimaViewModel(new GeneratoreAnteprima(), _files);

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new AnteprimaIngranditaDialog(modello);
            VisteTests.Disegna((FrameworkElement)finestra.Content, 900, 600, "anteprima-ingrandita-vuota");

            Assert.Equal("Anteprima ingrandita", finestra.Title);
            Assert.Equal(Visibility.Collapsed, ((ScrollViewer)finestra.FindName("Scorrimento")!).Visibility);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task Chiudendo_LaFinestraFermaIlModello()
    {
        var modello = await FinestraPronta();

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new AnteprimaIngranditaDialog(modello);
            VisteTests.Disegna((FrameworkElement)finestra.Content, 900, 600, "anteprima-ingrandita-chiusura");
            finestra.Close();
        });

        Assert.Empty(errori);
        Assert.False(modello.Visibile);
    }
}

public class AnteprimaIngranditaNelProgrammaTests : IDisposable
{
    private readonly ArchivioDiProva _a = new();
    private readonly FintoGeneratoreAnteprima _generatore = new();

    public void Dispose() => _a.Dispose();

    [Fact]
    public async Task DalModelloPrincipale_IngrandireApreLaFinestraDelServizioDiDialogo()
    {
        await _a.CreaCartellaAsync("Fatture", "Fattura", "uno.pdf");
        var vm = new MainViewModel(
            _a.Servizio, _a.Files, _a.Dialog, _a.Shell, new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false },
            generatoreAnteprima: _generatore);
        vm.Anteprima!.Ritardo = TimeSpan.Zero;
        await vm.InizializzaAsync();
        vm.Radici.Single().Aree.Single().Figli.Single().IsSelected = true;
        await vm.CaricamentoFormCompletato;
        vm.FormCartella!.DocumentoSelezionato = vm.FormCartella.Documenti.Single();
        await vm.Anteprima.Completamento;

        vm.Anteprima.IngrandisciCommand.Execute(null);

        var ingrandita = Assert.Single(_a.Dialog.AnteprimeIngrandite);
        await ingrandita.Completamento;
        Assert.Equal("uno.pdf", ingrandita.Titolo);
        Assert.Equal(_a.Fisico("Fatture", "Fattura", "uno.pdf"), _generatore.Richieste[^1].Percorso);
        Assert.Equal(GeneratoreAnteprima.LarghezzaIngrandita, _generatore.Larghezze[^1]);
    }
}
