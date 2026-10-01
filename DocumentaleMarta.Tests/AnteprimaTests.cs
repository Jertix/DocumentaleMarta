using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;

namespace DocumentaleMarta.Tests;

/// <summary>L'anteprima disegnata da file veri: PDF (con le funzioni di Windows) e immagini.</summary>
[Collection("WPF")]
public class GeneratoreAnteprimaTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();
    private readonly GeneratoreAnteprima _generatore = new();

    public void Dispose() => _tmp.Dispose();

    private static BitmapSource Pixel(int larghezza, int altezza)
    {
        var stride = larghezza * 3;
        var dati = new byte[stride * altezza];
        for (var y = 0; y < altezza; y++)
            for (var x = 0; x < larghezza; x++)
            {
                dati[y * stride + x * 3] = (byte)(x * 255 / Math.Max(1, larghezza - 1)); // un gradiente: l'immagine non è un colore unico
                dati[y * stride + x * 3 + 1] = (byte)(y * 255 / Math.Max(1, altezza - 1));
            }
        return BitmapSource.Create(larghezza, altezza, 96, 96, PixelFormats.Bgr24, null, dati, stride);
    }

    private string Salva(string nome, BitmapEncoder encoder)
    {
        var percorso = _tmp.Combina(nome);
        using var file = File.Create(percorso);
        encoder.Save(file);
        return percorso;
    }

    private string Png(string nome, int larghezza, int altezza)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Pixel(larghezza, altezza)));
        return Salva(nome, encoder);
    }

    /// <summary>Una JPEG con l'orientamento EXIF indicato (6 = da ruotare di 90° in senso orario, come tante foto del telefono).</summary>
    private string JpegConOrientamento(string nome, int larghezza, int altezza, ushort orientamento)
    {
        var metadati = new BitmapMetadata("jpg");
        metadati.SetQuery("/app1/ifd/{ushort=274}", orientamento);
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Pixel(larghezza, altezza), null, metadati, null));
        return Salva(nome, encoder);
    }

    private string TiffMultipagina(string nome, int pagine)
    {
        var encoder = new TiffBitmapEncoder();
        for (var i = 0; i < pagine; i++)
            encoder.Frames.Add(BitmapFrame.Create(Pixel(100 + i * 10, 60))); // larghezze diverse: si riconosce la pagina
        return Salva(nome, encoder);
    }

    private Task<RisultatoAnteprima> Genera(string percorso, int pagina = 0) =>
        Task.Run(() => _generatore.GeneraAsync(percorso, pagina, CancellationToken.None));

    // ---------- Quali file ----------

    [Theory]
    [InlineData(".pdf", true)]
    [InlineData(".PDF", true)]
    [InlineData(".jpg", true)]
    [InlineData(".jpeg", true)]
    [InlineData(".png", true)]
    [InlineData(".bmp", true)]
    [InlineData(".gif", true)]
    [InlineData(".tif", true)]
    [InlineData(".tiff", true)]
    [InlineData(".odt", true)]
    [InlineData(".ott", true)]
    [InlineData(".ods", true)]
    [InlineData(".ots", true)]
    [InlineData(".odp", true)]
    [InlineData(".otp", true)]
    [InlineData(".odg", true)]
    [InlineData(".otg", true)]
    [InlineData(".ODT", true)]
    [InlineData(".docx", false)]
    [InlineData(".xlsx", false)]
    [InlineData(".doc", false)]
    [InlineData(".txt", false)]
    [InlineData("", false)]
    public void Supporta_SoloPdfEImmagini(string estensione, bool atteso) =>
        Assert.Equal(atteso, _generatore.Supporta(estensione));

    // ---------- Immagini ----------

    [Fact]
    public async Task UnaImmagine_SiDisegna_UnaVolta_ESiPuoUsareDaOgniThread()
    {
        var risultato = await Genera(Png("foto.png", 200, 120));

        Assert.Equal(StatoAnteprima.Pronta, risultato.Stato);
        Assert.Equal(1, risultato.Pagine);
        var immagine = Assert.IsAssignableFrom<BitmapSource>(risultato.Immagine);
        Assert.Equal((200, 120), (immagine.PixelWidth, immagine.PixelHeight));
        Assert.True(immagine.IsFrozen);
    }

    [Fact]
    public async Task UnaImmagineGrande_SiRimpicciolisce()
    {
        var risultato = await Genera(Png("grande.png", 3000, 2000));

        var immagine = (BitmapSource)risultato.Immagine!;
        Assert.Equal(GeneratoreAnteprima.LarghezzaMassima, immagine.PixelWidth);
        Assert.InRange(immagine.PixelHeight, 732, 734); // 2000 * 1100 / 3000: le proporzioni restano
    }

    [Fact]
    public async Task UnaImmaginePiccola_NonSiIngrandisce()
    {
        var immagine = (BitmapSource)(await Genera(Png("piccola.png", 50, 40))).Immagine!;

        Assert.Equal(50, immagine.PixelWidth);
    }

    [Fact]
    public async Task UnaFotoDelTelefono_ConOrientamentoExif_SiRuota()
    {
        var risultato = await Genera(JpegConOrientamento("telefono.jpg", 400, 200, orientamento: 6));

        var immagine = (BitmapSource)risultato.Immagine!;
        Assert.Equal((200, 400), (immagine.PixelWidth, immagine.PixelHeight)); // larghezza e altezza si scambiano
    }

    [Fact]
    public async Task UnaFotoSenzaRotazione_RestaCom_E()
    {
        var immagine = (BitmapSource)(await Genera(JpegConOrientamento("dritta.jpg", 400, 200, orientamento: 1))).Immagine!;

        Assert.Equal((400, 200), (immagine.PixelWidth, immagine.PixelHeight));
    }

    [Fact]
    public async Task UnTiffAPiuPagine_MostraLaPaginaScelta_EDiceQuanteSono()
    {
        var tiff = TiffMultipagina("scansione.tif", 3);

        var seconda = await Genera(tiff, pagina: 1);
        var terza = await Genera(tiff, pagina: 2);

        Assert.Equal(3, seconda.Pagine);
        Assert.Equal(110, ((BitmapSource)seconda.Immagine!).PixelWidth);
        Assert.Equal(120, ((BitmapSource)terza.Immagine!).PixelWidth);
    }

    [Fact]
    public async Task UnaPaginaOltreLUltima_MostraLUltima()
    {
        var risultato = await Genera(TiffMultipagina("scansione.tif", 3), pagina: 99);

        Assert.Equal(120, ((BitmapSource)risultato.Immagine!).PixelWidth);
    }

    [Fact]
    public async Task UnaPaginaNegativa_MostraLaPrima()
    {
        var risultato = await Genera(TiffMultipagina("scansione.tif", 3), pagina: -4);

        Assert.Equal(100, ((BitmapSource)risultato.Immagine!).PixelWidth);
    }

    // ---------- PDF ----------

    [Fact]
    public async Task UnPdf_SiDisegnaComeImmagineDellaPrimaPagina()
    {
        var pdf = FileDiProva.Pdf(_tmp.Combina("fattura.pdf"), "Fattura numero 123");

        var risultato = await Genera(pdf);

        Assert.Equal(StatoAnteprima.Pronta, risultato.Stato);
        Assert.Equal(1, risultato.Pagine);
        var immagine = (BitmapSource)risultato.Immagine!;
        Assert.Equal(GeneratoreAnteprima.LarghezzaMassima, immagine.PixelWidth);
        Assert.True(immagine.PixelHeight > immagine.PixelWidth); // una pagina A4 è più alta che larga
        Assert.True(immagine.IsFrozen);
    }

    [Fact]
    public async Task UnPdfConPiuPagine_DiceQuanteSono_EOgniPaginaECosaDiversa()
    {
        var pdf = FileDiProva.Pdf(_tmp.Combina("libro.pdf"), "Prima pagina con un po di testo", "Seconda", "Terza pagina completamente diversa da tutte");

        var prima = await Genera(pdf, 0);
        var terza = await Genera(pdf, 2);

        Assert.Equal(3, prima.Pagine);
        Assert.NotEqual(Byte((BitmapSource)prima.Immagine!), Byte((BitmapSource)terza.Immagine!));
    }

    [Fact]
    public async Task UnPdfSenzaTesto_SiDisegnaLoStesso_BiancoMaPronto()
    {
        var risultato = await Genera(FileDiProva.PdfSenzaTesto(_tmp.Combina("vuoto.pdf"), pagine: 2));

        Assert.Equal(StatoAnteprima.Pronta, risultato.Stato);
        Assert.Equal(2, risultato.Pagine);
    }

    private static byte[] Byte(BitmapSource immagine)
    {
        var stride = immagine.PixelWidth * (immagine.Format.BitsPerPixel / 8);
        var dati = new byte[stride * immagine.PixelHeight];
        immagine.CopyPixels(dati, stride, 0);
        return dati;
    }

    // ---------- Quando non si può ----------

    [Theory]
    [InlineData("documento.docx", "DOCX")]
    [InlineData("foglio.xlsx", "XLSX")]
    [InlineData("vecchio.doc", "DOC")]
    public async Task UnFileSenzaAnteprima_DiceCosaFare_ENonEUnErrore(string nome, string tipo)
    {
        var percorso = _tmp.CreaFile(nome, "contenuto");

        var risultato = await Genera(percorso);

        Assert.Equal(StatoAnteprima.NonDisponibile, risultato.Stato);
        Assert.Contains($"file {tipo}", risultato.Messaggio);
        Assert.Contains("Apri", risultato.Messaggio);
        Assert.Null(risultato.Immagine);
    }

    [Fact]
    public async Task UnFileSparito_DiceCheNonCEPiu()
    {
        var risultato = await Genera(_tmp.Combina("sparito.pdf"));

        Assert.Equal(StatoAnteprima.Errore, risultato.Stato);
        Assert.Contains("non si trova più", risultato.Messaggio);
    }

    [Fact]
    public async Task UnPdfRovinato_DaUnMessaggio_NonUnEccezione()
    {
        var risultato = await Genera(_tmp.CreaFile("rovinato.pdf", "questo non è un PDF"));

        Assert.Equal(StatoAnteprima.Errore, risultato.Stato);
        Assert.Contains("PDF", risultato.Messaggio);
        Assert.Contains("password o danneggiato", risultato.Messaggio);
    }

    [Fact]
    public async Task UnaImmagineRovinata_DaUnMessaggio_NonUnEccezione()
    {
        var risultato = await Genera(_tmp.CreaFile("rovinata.png", "non sono pixel"));

        Assert.Equal(StatoAnteprima.Errore, risultato.Stato);
        Assert.Contains("immagine", risultato.Messaggio);
    }

    [Fact]
    public async Task UnFileVuoto_DaUnMessaggio()
    {
        var risultato = await Genera(_tmp.CreaFile("vuoto.png", ""));

        Assert.Equal(StatoAnteprima.Errore, risultato.Stato);
    }

    [Fact]
    public async Task Annullando_SiInterrompe()
    {
        var pdf = FileDiProva.Pdf(_tmp.Combina("a.pdf"), "x");
        using var annullamento = new CancellationTokenSource();
        await annullamento.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Task.Run(() => _generatore.GeneraAsync(pdf, 0, annullamento.Token)));
    }

    // ---------- OpenOffice e LibreOffice: la miniatura della prima pagina ----------

    [Fact]
    public async Task UnOdt_ConLaMiniatura_MostraLaPrimaPagina()
    {
        var odt = OpenDocumentDiProva.Odt(_tmp.Combina("lettera.odt"), ["Gentile cliente"], miniatura: OpenDocumentDiProva.PngDiProva(128, 180));

        var risultato = await Genera(odt);

        Assert.Equal(StatoAnteprima.Pronta, risultato.Stato);
        Assert.Equal(1, risultato.Pagine);
        var immagine = Assert.IsAssignableFrom<BitmapSource>(risultato.Immagine);
        Assert.Equal((128, 180), (immagine.PixelWidth, immagine.PixelHeight));
        Assert.True(immagine.IsFrozen);
        Assert.Equal(GeneratoreAnteprima.NotaMiniatura, risultato.Nota); // si dice che è solo la miniatura salvata nel documento
    }

    [Fact]
    public async Task PdfEImmagini_NonHannoNote()
    {
        Assert.Equal("", (await Genera(Png("a.png", 20, 20))).Nota);
        Assert.Equal("", (await Genera(FileDiProva.Pdf(_tmp.Combina("a.pdf"), "x"))).Nota);
    }

    [Theory]
    [InlineData("spreadsheet", ".ods")]
    [InlineData("presentation", ".odp")]
    [InlineData("drawing", ".odg")]
    [InlineData("text", ".ott")]
    public async Task AncheFogliPresentazioniDisegniEModelli_MostranoLaMiniatura(string tipo, string estensione)
    {
        var file = OpenDocumentDiProva.Documento(_tmp.Combina("doc" + estensione), tipo, "", miniatura: OpenDocumentDiProva.PngDiProva());

        Assert.Equal(StatoAnteprima.Pronta, (await Genera(file)).Stato);
    }

    [Fact]
    public async Task UnOdt_SenzaMiniatura_DiceCheNonCeL_Anteprima_ENonEUnErrore()
    {
        var odt = OpenDocumentDiProva.Odt(_tmp.Combina("senza.odt"), ["Gentile cliente"]);

        var risultato = await Genera(odt);

        Assert.Equal(StatoAnteprima.NonDisponibile, risultato.Stato);
        Assert.Contains("non contiene l'anteprima", risultato.Messaggio);
        Assert.Contains("Apri documento", risultato.Messaggio);
    }

    [Fact]
    public async Task UnOdtRovinato_DaUnMessaggioSuUnDocumento()
    {
        var risultato = await Genera(_tmp.CreaFile("rovinato.odt", "non sono uno zip"));

        Assert.Equal(StatoAnteprima.Errore, risultato.Stato);
        Assert.Contains("questo documento", risultato.Messaggio);
    }

    [Fact]
    public async Task UnaMiniaturaRovinata_DaUnMessaggio_NonUnEccezione()
    {
        var odt = OpenDocumentDiProva.Odt(_tmp.Combina("brutta.odt"), ["x"], miniatura: [1, 2, 3, 4, 5]);

        Assert.Equal(StatoAnteprima.Errore, (await Genera(odt)).Stato);
    }

    [Fact]
    public async Task UnOdt_SiVedeAncheSeEAperto_ESiPuoEliminareDopo()
    {
        var odt = OpenDocumentDiProva.Odt(_tmp.Combina("aperto.odt"), ["x"], miniatura: OpenDocumentDiProva.PngDiProva());
        using (new FileStream(odt, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
            Assert.Equal(StatoAnteprima.Pronta, (await Genera(odt)).Stato);

        File.Delete(odt);
        Assert.False(File.Exists(odt));
    }

    [Fact]
    public async Task UnOdtGrande_ConLaMiniatura_SiVedeSenzaCaricareTuttoIlFile()
    {
        // Il documento è grosso (qui 30 MB di dati poco comprimibili) ma la miniatura è piccola: l'anteprima non lo carica.
        var odt = OpenDocumentDiProva.Odt(_tmp.Combina("grosso.odt"), ["x"], miniatura: OpenDocumentDiProva.PngDiProva());
        using (var zip = ZipFile.Open(odt, ZipArchiveMode.Update))
        using (var voce = zip.CreateEntry("Pictures/foto.bin", System.IO.Compression.CompressionLevel.NoCompression).Open())
            voce.Write(new byte[30 * 1024 * 1024]);

        Assert.Equal(StatoAnteprima.Pronta, (await Genera(odt)).Stato);
    }

    // ---------- Il file non resta bloccato: si può spostare, eliminare, modificare ----------

    [Theory]
    [InlineData("a.pdf")]
    [InlineData("a.png")]
    [InlineData("a.tif")]
    public async Task DopoL_Anteprima_IlFileSiPuoEliminare(string nome)
    {
        var percorso = nome.EndsWith(".pdf") ? FileDiProva.Pdf(_tmp.Combina(nome), "ciao")
            : nome.EndsWith(".png") ? Png(nome, 80, 80) : TiffMultipagina(nome, 2);

        await Genera(percorso);

        File.Delete(percorso); // se l'anteprima lo tenesse aperto, qui ci sarebbe un'eccezione
        Assert.False(File.Exists(percorso));
    }

    [Fact]
    public async Task UnFileAperto_InUnAltroProgramma_SiVedeLoStesso()
    {
        var pdf = FileDiProva.Pdf(_tmp.Combina("aperto.pdf"), "ciao");
        using var altroProgramma = new FileStream(pdf, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal(StatoAnteprima.Pronta, (await Genera(pdf)).Stato);
    }
}

/// <summary>Un generatore che non disegna davvero: il test decide cosa risponde e quando.</summary>
public class FintoGeneratoreAnteprima : IGeneratoreAnteprima
{
    public static readonly ImageSource ImmagineFinta = CreaImmagine();

    public List<(string Percorso, int Pagina)> Richieste { get; } = [];

    /// <summary>La larghezza chiesta a ogni richiesta (la stessa posizione di <see cref="Richieste"/>).</summary>
    public List<int> Larghezze { get; } = [];
    public int Pagine { get; set; } = 1;
    public Func<string, int, CancellationToken, Task<RisultatoAnteprima>>? Comportamento { get; set; }

    private static ImageSource CreaImmagine()
    {
        var immagine = BitmapSource.Create(4, 6, 96, 96, PixelFormats.Gray8, null, new byte[24], 4);
        immagine.Freeze();
        return immagine;
    }

    public bool Supporta(string estensione) => estensione is ".pdf" or ".png";

    public Task<RisultatoAnteprima> GeneraAsync(
        string percorsoAssoluto, int pagina, CancellationToken annullamento, int larghezzaMassima = GeneratoreAnteprima.LarghezzaMassima)
    {
        Richieste.Add((percorsoAssoluto, pagina));
        Larghezze.Add(larghezzaMassima);
        if (Comportamento is not null)
            return Comportamento(percorsoAssoluto, pagina, annullamento);
        return Task.FromResult(new RisultatoAnteprima(StatoAnteprima.Pronta, ImmagineFinta, Pagine));
    }
}

[Collection("WPF")]
public class AnteprimaViewModelTests : IDisposable
{
    private record Doc(string NomeFile, string PercorsoRelativo) : IDocumentoAnteprima;

    private readonly CartellaTemporanea _tmp = new();
    private readonly ArchivioFileService _files;
    private readonly FintoGeneratoreAnteprima _generatore = new();
    private readonly AnteprimaViewModel _vm;

    public AnteprimaViewModelTests()
    {
        _files = new ArchivioFileService(_tmp.Combina("Documentale"), usaCestino: false);
        _vm = new AnteprimaViewModel(_generatore, _files) { Ritardo = TimeSpan.Zero };
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

    // ---------- Stati ----------

    [Fact]
    public void All_Inizio_NonCEUnDocumento_ELoDice()
    {
        Assert.Equal(StatoAnteprima.Vuota, _vm.Stato);
        Assert.Equal(AnteprimaViewModel.TestoNessunDocumento, _vm.Messaggio);
        Assert.True(_vm.HaMessaggio);
        Assert.False(_vm.HaImmagine);
        Assert.True(_vm.Visibile);
    }

    [Fact]
    public async Task ScegliendoUnDocumento_SiMostraLaSuaPrimaPagina()
    {
        var documento = Documento();

        _vm.Mostra(documento);
        await _vm.Completamento;

        Assert.Equal(StatoAnteprima.Pronta, _vm.Stato);
        Assert.Same(FintoGeneratoreAnteprima.ImmagineFinta, _vm.Immagine);
        Assert.Equal("fattura.pdf", _vm.Titolo);
        Assert.True(_vm.HaImmagine);
        Assert.False(_vm.HaMessaggio);
        Assert.Equal([(_files.PercorsoAssoluto(documento.PercorsoRelativo), 0)], _generatore.Richieste);
    }

    [Fact]
    public async Task MentreSiPrepara_DiceCaricamento_EAlTermineMostraLImmagine()
    {
        var via = new TaskCompletionSource<RisultatoAnteprima>();
        _generatore.Comportamento = (_, _, _) => via.Task;

        _vm.Mostra(Documento());
        await Attendi(() => _generatore.Richieste.Count == 1);

        Assert.Equal(StatoAnteprima.Caricamento, _vm.Stato);
        Assert.True(_vm.CaricamentoVisibile);
        Assert.False(_vm.HaImmagine);

        via.SetResult(new RisultatoAnteprima(StatoAnteprima.Pronta, FintoGeneratoreAnteprima.ImmagineFinta, 1));
        await _vm.Completamento;

        Assert.False(_vm.CaricamentoVisibile);
        Assert.True(_vm.HaImmagine);
    }

    [Fact]
    public async Task LaNotaSottoLImmagine_SiMostraSoloConLImmagine_EScompareCambiandoDocumento()
    {
        _generatore.Comportamento = (_, _, _) => Task.FromResult(
            new RisultatoAnteprima(StatoAnteprima.Pronta, FintoGeneratoreAnteprima.ImmagineFinta, 1, Nota: "Solo una miniatura."));
        _vm.Mostra(Documento("lettera.odt"));
        await _vm.Completamento;
        Assert.Equal("Solo una miniatura.", _vm.Nota);
        Assert.True(_vm.HaNota);

        _generatore.Comportamento = null; // il prossimo documento non ha note
        _vm.Mostra(Documento("fattura.pdf"));
        await _vm.Completamento;
        Assert.Equal("", _vm.Nota);
        Assert.False(_vm.HaNota);

        _generatore.Comportamento = (_, _, _) => Task.FromResult(
            new RisultatoAnteprima(StatoAnteprima.Pronta, FintoGeneratoreAnteprima.ImmagineFinta, 1, Nota: "Ancora."));
        _vm.Mostra(Documento("altro.odt"));
        await _vm.Completamento;
        _vm.Svuota();
        Assert.Equal("", _vm.Nota);
    }

    [Fact]
    public async Task UnFileSenzaAnteprima_MostraIlMessaggioDelGeneratore()
    {
        _generatore.Comportamento = (_, _, _) => Task.FromResult(RisultatoAnteprima.NonDisponibile("Per i file DOCX non c'è l'anteprima."));

        _vm.Mostra(Documento("lettera.docx"));
        await _vm.Completamento;

        Assert.Equal(StatoAnteprima.NonDisponibile, _vm.Stato);
        Assert.Equal("Per i file DOCX non c'è l'anteprima.", _vm.Messaggio);
        Assert.True(_vm.HaMessaggio);
        Assert.False(_vm.HaImmagine);
        Assert.Equal("lettera.docx", _vm.Titolo);
    }

    [Fact]
    public async Task UnFileSparitoDalDisco_NonChiamaIlGeneratore()
    {
        var documento = Documento();
        File.Delete(_files.PercorsoAssoluto(documento.PercorsoRelativo));

        _vm.Mostra(documento);
        await _vm.Completamento;

        Assert.Equal(StatoAnteprima.Errore, _vm.Stato);
        Assert.Contains("non si trova più", _vm.Messaggio);
        Assert.Empty(_generatore.Richieste);
    }

    [Fact]
    public async Task SeIlGeneratoreLanciaUnEccezione_SiMostraUnMessaggio()
    {
        _generatore.Comportamento = (_, _, _) => throw new InvalidOperationException("guasto");

        _vm.Mostra(Documento());
        await _vm.Completamento;

        Assert.Equal(StatoAnteprima.Errore, _vm.Stato);
        Assert.Contains("guasto", _vm.Messaggio);
    }

    [Fact]
    public async Task Svuotando_TornaAlMessaggioIniziale_SenzaImmagine()
    {
        _vm.Mostra(Documento());
        await _vm.Completamento;

        _vm.Svuota();

        Assert.Equal(StatoAnteprima.Vuota, _vm.Stato);
        Assert.Null(_vm.Immagine);
        Assert.Equal("", _vm.Titolo);
        Assert.Equal(AnteprimaViewModel.TestoNessunDocumento, _vm.Messaggio);
    }

    [Fact]
    public async Task Mostra_Null_EcomeSvuotare()
    {
        _vm.Mostra(Documento());
        await _vm.Completamento;

        _vm.Mostra(null);

        Assert.Equal(StatoAnteprima.Vuota, _vm.Stato);
    }

    // ---------- Pagine ----------

    [Fact]
    public async Task UnDocumentoAPiuPagine_MostraIPulsantiEIlNumero()
    {
        _generatore.Pagine = 3;

        _vm.Mostra(Documento());
        await _vm.Completamento;

        Assert.True(_vm.HaPagine);
        Assert.Equal("Pagina 1 di 3", _vm.TestoPagina);
        Assert.False(_vm.PaginaPrecedenteCommand.CanExecute(null));
        Assert.True(_vm.PaginaSuccessivaCommand.CanExecute(null));
    }

    [Fact]
    public async Task UnDocumentoAUnaPagina_NonMostraIPulsanti()
    {
        _vm.Mostra(Documento());
        await _vm.Completamento;

        Assert.False(_vm.HaPagine);
    }

    [Fact]
    public async Task SfogliandoLePagine_SiChiedeQuellaGiusta_EIPulsantiSiAggiornano()
    {
        _generatore.Pagine = 3;
        _vm.Mostra(Documento());
        await _vm.Completamento;

        _vm.PaginaSuccessivaCommand.Execute(null);
        await _vm.Completamento;
        Assert.Equal("Pagina 2 di 3", _vm.TestoPagina);
        Assert.True(_vm.PaginaPrecedenteCommand.CanExecute(null));
        Assert.True(_vm.PaginaSuccessivaCommand.CanExecute(null));

        _vm.PaginaSuccessivaCommand.Execute(null);
        await _vm.Completamento;
        Assert.Equal("Pagina 3 di 3", _vm.TestoPagina);
        Assert.False(_vm.PaginaSuccessivaCommand.CanExecute(null));

        _vm.PaginaPrecedenteCommand.Execute(null);
        await _vm.Completamento;
        Assert.Equal("Pagina 2 di 3", _vm.TestoPagina);

        Assert.Equal([0, 1, 2, 1], _generatore.Richieste.Select(r => r.Pagina));
    }

    [Fact]
    public async Task CambiandoPagina_LImmagineResta_FinoAlNuovoArrivo()
    {
        _generatore.Pagine = 2;
        _vm.Mostra(Documento());
        await _vm.Completamento;
        var via = new TaskCompletionSource<RisultatoAnteprima>();
        _generatore.Comportamento = (_, _, _) => via.Task;

        _vm.PaginaSuccessivaCommand.Execute(null);
        await Attendi(() => _generatore.Richieste.Count == 2);

        Assert.True(_vm.HaImmagine);          // la pagina vecchia si vede ancora: niente lampo bianco
        Assert.False(_vm.CaricamentoVisibile);
        via.SetResult(new RisultatoAnteprima(StatoAnteprima.Pronta, FintoGeneratoreAnteprima.ImmagineFinta, 2));
        await _vm.Completamento;
    }

    [Fact]
    public async Task CambiandoDocumento_SiRicominciaDallaPrimaPagina_ESiSvuotaLImmagine()
    {
        _generatore.Pagine = 3;
        _vm.Mostra(Documento("uno.pdf"));
        await _vm.Completamento;
        _vm.PaginaSuccessivaCommand.Execute(null);
        await _vm.Completamento;

        var via = new TaskCompletionSource<RisultatoAnteprima>();
        _generatore.Comportamento = (_, _, _) => via.Task;
        _vm.Mostra(Documento("due.pdf"));
        await Attendi(() => _generatore.Richieste.Count == 3);

        Assert.Equal(0, _generatore.Richieste[2].Pagina);
        Assert.False(_vm.HaImmagine);
        Assert.True(_vm.CaricamentoVisibile);
        via.SetResult(new RisultatoAnteprima(StatoAnteprima.Pronta, FintoGeneratoreAnteprima.ImmagineFinta, 3));
        await _vm.Completamento;
        Assert.Equal("Pagina 1 di 3", _vm.TestoPagina);
    }

    // ---------- Più richieste ravvicinate ----------

    [Fact]
    public async Task ScorrendoVeloceL_Elenco_ValeSoloLUltimaSelezione()
    {
        var primo = new TaskCompletionSource<RisultatoAnteprima>();
        var risposte = new Queue<Task<RisultatoAnteprima>>([primo.Task]);
        _generatore.Comportamento = (_, _, _) =>
            risposte.Count > 0 ? risposte.Dequeue()
                : Task.FromResult(new RisultatoAnteprima(StatoAnteprima.Pronta, FintoGeneratoreAnteprima.ImmagineFinta, 1));

        _vm.Mostra(Documento("vecchio.pdf"));
        await Attendi(() => _generatore.Richieste.Count == 1);
        _vm.Mostra(Documento("nuovo.pdf"));
        await _vm.Completamento;

        // Arriva in ritardo la risposta del primo: non deve sovrascrivere quella del secondo.
        primo.SetResult(RisultatoAnteprima.InErrore("risposta vecchia"));
        await Task.Delay(50);

        Assert.Equal("nuovo.pdf", _vm.Titolo);
        Assert.Equal(StatoAnteprima.Pronta, _vm.Stato);
        Assert.DoesNotContain("vecchia", _vm.Messaggio);
    }

    [Fact]
    public async Task SeSiSvuotaMentreSiCarica_LaRispostaInRitardoNonRiapparisce()
    {
        var via = new TaskCompletionSource<RisultatoAnteprima>();
        _generatore.Comportamento = (_, _, _) => via.Task;
        _vm.Mostra(Documento());
        await Attendi(() => _generatore.Richieste.Count == 1);

        _vm.Svuota();
        via.SetResult(new RisultatoAnteprima(StatoAnteprima.Pronta, FintoGeneratoreAnteprima.ImmagineFinta, 1));
        await Task.Delay(50);

        Assert.Equal(StatoAnteprima.Vuota, _vm.Stato);
        Assert.Null(_vm.Immagine);
    }

    // ---------- Pannello chiuso ----------

    [Fact]
    public async Task ConIlPannelloChiuso_NonSiDisegnaNulla()
    {
        _vm.Visibile = false;

        _vm.Mostra(Documento());
        await _vm.Completamento;

        Assert.Empty(_generatore.Richieste);
    }

    [Fact]
    public async Task RiapertoIlPannello_SiMostraIlDocumentoSelezionatoNelFrattempo()
    {
        _vm.Visibile = false;
        _vm.Mostra(Documento("scelto.pdf"));

        _vm.Visibile = true;
        await _vm.Completamento;

        Assert.Equal("scelto.pdf", _vm.Titolo);
        Assert.Equal(StatoAnteprima.Pronta, _vm.Stato);
    }

    [Fact]
    public async Task ChiudendoIlPannelloMentreSiCarica_LaRispostaSiIgnora()
    {
        var via = new TaskCompletionSource<RisultatoAnteprima>();
        _generatore.Comportamento = (_, _, _) => via.Task;
        _vm.Mostra(Documento());
        await Attendi(() => _generatore.Richieste.Count == 1);

        _vm.Visibile = false;
        via.SetResult(new RisultatoAnteprima(StatoAnteprima.Pronta, FintoGeneratoreAnteprima.ImmagineFinta, 1));
        await Task.Delay(50);

        Assert.NotEqual(StatoAnteprima.Pronta, _vm.Stato);
    }

    [Fact]
    public async Task IlRitardo_EvitaDiDisegnareLeRigheAttraversate()
    {
        _vm.Ritardo = TimeSpan.FromMilliseconds(150);

        _vm.Mostra(Documento("a.pdf"));
        _vm.Mostra(Documento("b.pdf"));
        _vm.Mostra(Documento("c.pdf"));
        await _vm.Completamento;

        Assert.Equal(["c.pdf"], _generatore.Richieste.Select(r => Path.GetFileName(r.Percorso)));
    }

    private static async Task Attendi(Func<bool> condizione)
    {
        for (var i = 0; i < 200 && !condizione(); i++)
            await Task.Delay(10);
        Assert.True(condizione(), "la condizione attesa non si è verificata");
    }
}

[Collection("WPF")]
public class AnteprimaNelleGrigliaTests : IDisposable
{
    private readonly ArchivioDiProva _a = new();
    private readonly FintoGeneratoreAnteprima _generatore = new();
    private readonly MainViewModel _vm;

    public AnteprimaNelleGrigliaTests()
    {
        _vm = NuovoViewModel(_generatore);
        _vm.Anteprima!.Ritardo = TimeSpan.Zero;
    }

    public void Dispose() => _a.Dispose();

    private MainViewModel NuovoViewModel(IGeneratoreAnteprima? generatore) => new(
        _a.Servizio, _a.Files, _a.Dialog, _a.Shell, new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false },
        generatoreAnteprima: generatore);

    private async Task PreparaAsync()
    {
        await _a.CreaCartellaAsync("Fatture", "Fattura 1", "uno.pdf", "due.pdf");
        await _vm.InizializzaAsync();
    }

    private NodoAlberoViewModel Cartella => _vm.Radici.Single().Aree.Single().Figli.Single();

    private async Task ApriCartellaAsync()
    {
        Cartella.IsSelected = true;
        await _vm.CaricamentoFormCompletato;
    }

    [Fact]
    public async Task SelezionandoUnDocumentoDelForm_SiMostraLaSuaAnteprima()
    {
        await PreparaAsync();
        await ApriCartellaAsync();

        _vm.FormCartella!.DocumentoSelezionato = _vm.FormCartella.Documenti.Single(d => d.NomeFile == "due.pdf");
        await _vm.Anteprima!.Completamento;

        Assert.Equal("due.pdf", _vm.Anteprima.Titolo);
        Assert.Equal(StatoAnteprima.Pronta, _vm.Anteprima.Stato);
        Assert.Equal(_a.Fisico("Fatture", "Fattura 1", "due.pdf"), _generatore.Richieste.Single().Percorso);
    }

    [Fact]
    public async Task SelezionandoUnDocumentoDellaGrigliaDiUnArea_SiMostraLaSuaAnteprima()
    {
        await PreparaAsync();
        _vm.Radici.Single().Aree.Single().IsSelected = true;
        await _vm.CaricamentoElencoCompletato;

        _vm.ElencoDocumenti!.DocumentoSelezionato = _vm.ElencoDocumenti.Documenti.First();
        await _vm.Anteprima!.Completamento;

        Assert.Equal(StatoAnteprima.Pronta, _vm.Anteprima.Stato);
        Assert.Single(_generatore.Richieste);
    }

    [Fact]
    public async Task ScegliendoUnAltroElementoDellAlbero_LAnteprimaSiSvuota()
    {
        await PreparaAsync();
        await ApriCartellaAsync();
        _vm.FormCartella!.DocumentoSelezionato = _vm.FormCartella.Documenti.First();
        await _vm.Anteprima!.Completamento;

        _vm.Radici.Single().Aree.Single().IsSelected = true;

        Assert.Equal(StatoAnteprima.Vuota, _vm.Anteprima.Stato);
        Assert.Null(_vm.Anteprima.Immagine);
    }

    [Fact]
    public async Task EliminandoIlDocumentoMostrato_LAnteprimaSiSvuota()
    {
        await PreparaAsync();
        await ApriCartellaAsync();
        var documento = _vm.FormCartella!.Documenti.First(d => d.NomeFile == "uno.pdf");
        _vm.FormCartella.DocumentoSelezionato = documento;
        await _vm.Anteprima!.Completamento;

        await documento.EliminaCommand.ExecuteAsync(null);

        Assert.Equal(StatoAnteprima.Vuota, _vm.Anteprima.Stato);
        Assert.Null(_vm.FormCartella.DocumentoSelezionato);
    }

    [Fact]
    public async Task Eliminando_UnAltroDocumento_LAnteprimaRestaDovEra()
    {
        await PreparaAsync();
        await ApriCartellaAsync();
        _vm.FormCartella!.DocumentoSelezionato = _vm.FormCartella.Documenti.First(d => d.NomeFile == "uno.pdf");
        await _vm.Anteprima!.Completamento;

        await _vm.FormCartella.Documenti.First(d => d.NomeFile == "due.pdf").EliminaCommand.ExecuteAsync(null);

        Assert.Equal("uno.pdf", _vm.Anteprima.Titolo);
        Assert.Equal(StatoAnteprima.Pronta, _vm.Anteprima.Stato);
    }

    [Fact]
    public async Task UnaNuovaRicerca_SvuotaLAnteprimaDeiRisultatiVecchi()
    {
        await PreparaAsync();
        _vm.Radici.Single().Aree.Single().IsSelected = true;
        await _vm.CaricamentoElencoCompletato;
        _vm.ElencoDocumenti!.DocumentoSelezionato = _vm.ElencoDocumenti.Documenti.First();
        await _vm.Anteprima!.Completamento;

        var nuovo = new MainViewModel(
            _a.Servizio, _a.Files, _a.Dialog, _a.Shell, new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false },
            ricerca: _a.Ricerca, generatoreAnteprima: _generatore) { RitardoRicerca = TimeSpan.Zero };
        nuovo.Anteprima!.Ritardo = TimeSpan.Zero;
        await nuovo.InizializzaAsync();
        nuovo.Radici.Single().Aree.Single().IsSelected = true;
        await nuovo.CaricamentoElencoCompletato;
        nuovo.ElencoDocumenti!.DocumentoSelezionato = nuovo.ElencoDocumenti.Documenti.First();
        await nuovo.Anteprima.Completamento;

        nuovo.TestoRicerca = "uno";
        await nuovo.RicercaCompletata;

        Assert.Equal(StatoAnteprima.Vuota, nuovo.Anteprima.Stato);
    }

    [Fact]
    public async Task SenzaGeneratore_IlPannelloNonCompare_ESelezionareNonDaProblemi()
    {
        var vm = NuovoViewModel(null);
        await _a.CreaCartellaAsync("Fatture", "F", "a.pdf");
        await vm.InizializzaAsync();
        vm.Radici.Single().Aree.Single().Figli.Single().IsSelected = true;
        await vm.CaricamentoFormCompletato;

        vm.FormCartella!.DocumentoSelezionato = vm.FormCartella.Documenti.Single();

        Assert.Null(vm.Anteprima);
        Assert.False(vm.AnteprimaDisponibile);
    }

    [Fact]
    public async Task ConIlGeneratoreVero_UnPdfDellArchivio_SiVede()
    {
        var pdf = FileDiProva.Pdf(_a.Tmp.Combina("vero.pdf"), "Fattura vera");
        var areaId = await _a.Servizio.CreaAreaAsync("Fatture");
        await _a.Servizio.CreaCartellaConDatiAsync(areaId, new DocumentaleMarta.Core.Modelli.DatiCartella("F", null, null, false, null), [pdf]);
        var vm = NuovoViewModel(new GeneratoreAnteprima());
        vm.Anteprima!.Ritardo = TimeSpan.Zero;
        await vm.InizializzaAsync();
        vm.Radici.Single().Aree.Single().Figli.Single().IsSelected = true;
        await vm.CaricamentoFormCompletato;

        vm.FormCartella!.DocumentoSelezionato = vm.FormCartella.Documenti.Single();
        await vm.Anteprima.Completamento;

        Assert.Equal(StatoAnteprima.Pronta, vm.Anteprima.Stato);
        Assert.NotNull(vm.Anteprima.Immagine);

        // E il documento si può ancora eliminare mentre se ne vede l'anteprima.
        await vm.FormCartella.Documenti.Single().EliminaCommand.ExecuteAsync(null);
        Assert.Empty(_a.Dialog.Errori);
    }
}
