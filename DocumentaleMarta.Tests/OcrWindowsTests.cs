using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data.Testo;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Writer;

namespace DocumentaleMarta.Tests;

/// <summary>Il test gira solo se questo PC ha l'OCR di Windows: altrimenti risulta "saltato", non "riuscito".</summary>
public sealed class OcrFactAttribute : FactAttribute
{
    public OcrFactAttribute()
    {
        if (!new OcrWindows().Disponibile)
            Skip = "L'OCR di Windows non è disponibile su questo PC.";
    }
}

/// <summary>Prove con l'OCR vero di Windows su immagini con testo disegnato al volo.</summary>
public class OcrWindowsTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();
    private readonly OcrWindows _ocr = new();

    public void Dispose() => _tmp.Dispose();

    // ---------- Immagini con testo ----------

    /// <summary>Disegna un testo grande e nitido, nero su bianco, come una buona scansione.</summary>
    private static RenderTargetBitmap Disegna(string testo, int larghezza = 1400, int altezza = 260)
    {
        RenderTargetBitmap? risultato = null;
        var thread = new Thread(() =>
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, larghezza, altezza));
                var formattato = new FormattedText(
                    testo, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Arial"), 64, Brushes.Black, 1.0);
                dc.DrawText(formattato, new Point(40, 60));
            }
            var bitmap = new RenderTargetBitmap(larghezza, altezza, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            risultato = bitmap;
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return risultato!;
    }

    private static byte[] Codifica(BitmapEncoder encoder, params BitmapSource[] frame)
    {
        foreach (var f in frame)
            encoder.Frames.Add(BitmapFrame.Create(f));
        using var memoria = new MemoryStream();
        encoder.Save(memoria);
        return memoria.ToArray();
    }

    private string Png(string nome, string testo)
    {
        var percorso = _tmp.Combina(nome);
        File.WriteAllBytes(percorso, Codifica(new PngBitmapEncoder(), Disegna(testo)));
        return percorso;
    }

    [OcrFact]
    public async Task Immagine_Png_LeggeIlTesto()
    {
        var percorso = Png("fattura.png", "FATTURA NUMERO 12345");

        var testo = await _ocr.RiconosciImmagineAsync(percorso, CancellationToken.None);

        Assert.Contains("FATTURA", testo, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("12345", testo);
    }

    [OcrFact]
    public async Task Immagine_Jpeg_LeggeIlTesto()
    {
        var percorso = _tmp.Combina("foto.jpg");
        File.WriteAllBytes(percorso, Codifica(new JpegBitmapEncoder { QualityLevel = 90 }, Disegna("CONTRATTO 98765")));

        var testo = await _ocr.RiconosciImmagineAsync(percorso, CancellationToken.None);

        Assert.Contains("98765", testo);
    }

    [OcrFact]
    public async Task Immagine_ConTestoItaliano_LeggeLeParole()
    {
        var percorso = Png("italiano.png", "Preventivo carpenteria metallica");

        var testo = await _ocr.RiconosciImmagineAsync(percorso, CancellationToken.None);

        Assert.Contains("carpenteria", testo, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("metallica", testo, StringComparison.OrdinalIgnoreCase);
    }

    [OcrFact]
    public async Task Immagine_SenzaTesto_TestoVuoto_SenzaErrori()
    {
        var bianca = Codifica(new PngBitmapEncoder(), Disegna(""));
        var percorso = _tmp.Combina("bianca.png");
        File.WriteAllBytes(percorso, bianca);

        Assert.True(string.IsNullOrWhiteSpace(await _ocr.RiconosciImmagineAsync(percorso, CancellationToken.None)));
    }

    [OcrFact]
    public async Task Tiff_ConDuePagine_LeggeTutteEDue()
    {
        var percorso = _tmp.Combina("scansione.tif");
        // Come uno scanner vero, le pagine sono opache (senza canale di trasparenza).
        BitmapSource Opaca(BitmapSource s) => new FormatConvertedBitmap(s, PixelFormats.Bgr24, null, 0);
        File.WriteAllBytes(percorso, Codifica(new TiffBitmapEncoder(),
            Opaca(Disegna("PRIMA PAGINA 3574")), Opaca(Disegna("SECONDA PAGINA 2222"))));

        var testo = await _ocr.RiconosciImmagineAsync(percorso, CancellationToken.None);

        Assert.Contains("3574", testo);
        Assert.Contains("2222", testo);
    }

    [OcrFact]
    public async Task Immagine_Rovinata_DaErrore_NonDiceCheLOcrManca()
    {
        var percorso = _tmp.CreaFile("rotta.png", "questo non è un PNG");

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => _ocr.RiconosciImmagineAsync(percorso, CancellationToken.None));

        Assert.IsNotType<OcrNonDisponibileException>(ex);
    }

    [OcrFact]
    public async Task Immagine_ConLeggendaGiaAnnullata_SiFermaSubito()
    {
        var percorso = Png("x.png", "TESTO 1");
        using var annullamento = new CancellationTokenSource();
        annullamento.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _ocr.RiconosciImmagineAsync(percorso, annullamento.Token));
    }

    [OcrFact]
    public void Disponibile_ConIlMotivoVuoto()
    {
        Assert.True(_ocr.Disponibile);
        Assert.Null(_ocr.MotivoNonDisponibile);
    }

    // ---------- PDF scansionati ----------

    /// <summary>Un PDF fatto solo di immagini (nessun testo vero), come esce da uno scanner.</summary>
    private string PdfScansionato(string nome, params string[] testiDellePagine)
    {
        var costruttore = new PdfDocumentBuilder();
        foreach (var testo in testiDellePagine)
        {
            var pagina = costruttore.AddPage(PageSize.A4);
            var jpeg = Codifica(new JpegBitmapEncoder { QualityLevel = 90 }, Disegna(testo, larghezza: 1400, altezza: 1000));
            pagina.AddJpeg(jpeg, new PdfRectangle(20, 280, 575, 820));
        }
        var percorso = _tmp.Combina(nome);
        File.WriteAllBytes(percorso, costruttore.Build());
        return percorso;
    }

    [OcrFact]
    public async Task Pdf_Scansionato_LeggeLePagineComeImmagini()
    {
        var percorso = PdfScansionato("scan.pdf", "BOLLETTA NUMERO 55501", "SECONDA PAGINA 66602");

        var testo = await _ocr.RiconosciPdfAsync(percorso, CancellationToken.None);

        Assert.Contains("55501", testo);
        Assert.Contains("66602", testo);
    }

    [OcrFact]
    public async Task EstrattorePdf_ConOcrVero_LeggeUnaScansione_CheSenzaOcrResterebbeInSospeso()
    {
        var percorso = PdfScansionato("scan.pdf", "FATTURA NUMERO 77703");

        var testo = await new EstrattorePdf(_ocr).EstraiAsync(percorso, CancellationToken.None);

        Assert.Contains("77703", testo);
    }

    [OcrFact]
    public async Task EstrattoreImmagine_ConOcrVero_LeggeUnaFoto()
    {
        var percorso = Png("foto.png", "OFFERTA 31415");

        var testo = await new EstrattoreImmagine(_ocr).EstraiAsync(percorso, CancellationToken.None);

        Assert.Contains("31415", testo);
    }

    [OcrFact]
    public async Task Pdf_Rovinato_DaErrore()
    {
        var percorso = _tmp.CreaFile("rotto.pdf", "%PDF-1.4 non proprio un pdf");

        await Assert.ThrowsAnyAsync<Exception>(() => _ocr.RiconosciPdfAsync(percorso, CancellationToken.None));
    }
}
