using System.IO;
using System.Text;
using DocumentaleMarta.Core.Servizi;
using Windows.Data.Pdf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace DocumentaleMarta.App.Servizi;

/// <summary>
/// Il riconoscimento del testo integrato in Windows 10 e 11 (gratuito e senza collegamento a internet):
/// usa l'italiano se installato, altrimenti una delle lingue dell'utente.
/// </summary>
public sealed class OcrWindows : IOcr
{
    /// <summary>Di un documento lunghissimo (es. un libro scansionato) si leggono le prime pagine.</summary>
    internal const int PagineMassime = 100;

    /// <summary>Larghezza in pixel a cui si disegna una pagina PDF prima di leggerla: circa 200 punti per pollice su un A4.</summary>
    private const double LarghezzaPaginaPdf = 1700;

    private readonly Lazy<OcrEngine?> _motore = new(() =>
        OcrEngine.TryCreateFromLanguage(new Language("it-IT")) ?? OcrEngine.TryCreateFromUserProfileLanguages());

    public bool Disponibile => _motore.Value is not null;

    public string? MotivoNonDisponibile => Disponibile
        ? null
        : "Windows non ha il riconoscimento del testo (OCR) per l'italiano. Per attivarlo: Impostazioni → Ora e lingua → " +
          "Lingua e area geografica → Italiano → Opzioni lingua → Riconoscimento del testo.";

    public async Task<string> RiconosciImmagineAsync(string percorsoFile, CancellationToken cancellation)
    {
        var motore = Motore();

        await using var file = new FileStream(percorsoFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var stream = file.AsRandomAccessStream();
        var decodificatore = await BitmapDecoder.CreateAsync(stream).AsTask(cancellation);

        // Un TIFF può contenere più pagine (tipico delle scansioni multipagina): le si legge tutte.
        var testo = new StringBuilder();
        var pagine = (int)Math.Min(decodificatore.FrameCount, PagineMassime);
        for (var i = 0; i < pagine; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var frame = await decodificatore.GetFrameAsync((uint)i).AsTask(cancellation);
            using var immagine = await ComeImmagineLeggibileAsync(frame, motore, cancellation);
            testo.AppendLine((await motore.RecognizeAsync(immagine).AsTask(cancellation)).Text);
        }
        return testo.ToString();
    }

    public async Task<string> RiconosciPdfAsync(string percorsoFile, CancellationToken cancellation)
    {
        var motore = Motore();

        await using var file = new FileStream(percorsoFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var stream = file.AsRandomAccessStream();
        var documento = await PdfDocument.LoadFromStreamAsync(stream).AsTask(cancellation);

        var testo = new StringBuilder();
        var pagine = (int)Math.Min(documento.PageCount, PagineMassime);
        for (var i = 0; i < pagine; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            using var pagina = documento.GetPage((uint)i);

            // La pagina si "stampa" in un'immagine (su sfondo bianco) e l'immagine si legge come una scansione.
            using var memoria = new InMemoryRandomAccessStream();
            var opzioni = new PdfPageRenderOptions { DestinationWidth = (uint)LarghezzaPaginaPdf };
            await pagina.RenderToStreamAsync(memoria, opzioni).AsTask(cancellation);

            var decodificatore = await BitmapDecoder.CreateAsync(memoria).AsTask(cancellation);
            using var immagine = await ComeImmagineLeggibileAsync(decodificatore, motore, cancellation);
            testo.AppendLine((await motore.RecognizeAsync(immagine).AsTask(cancellation)).Text);
        }
        return testo.ToString();
    }

    private OcrEngine Motore() =>
        _motore.Value ?? throw new OcrNonDisponibileException(MotivoNonDisponibile!);

    /// <summary>L'immagine nel formato che l'OCR vuole, rimpicciolita se supera la dimensione massima che il motore accetta.</summary>
    private static async Task<SoftwareBitmap> ComeImmagineLeggibileAsync<T>(T frame, OcrEngine motore, CancellationToken cancellation)
        where T : IBitmapFrame, IBitmapFrameWithSoftwareBitmap
    {
        var lato = Math.Max(frame.PixelWidth, frame.PixelHeight);
        var trasformazione = new BitmapTransform();
        if (lato > OcrEngine.MaxImageDimension)
        {
            var scala = (double)OcrEngine.MaxImageDimension / lato;
            trasformazione.ScaledWidth = (uint)(frame.PixelWidth * scala);
            trasformazione.ScaledHeight = (uint)(frame.PixelHeight * scala);
        }

        return await frame.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, trasformazione,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage)
            .AsTask(cancellation);
    }
}
