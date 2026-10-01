using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentaleMarta.App.ViewModels;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace DocumentaleMarta.App.Servizi;

public enum StatoAnteprima
{
    /// <summary>Nessun documento selezionato.</summary>
    Vuota,

    /// <summary>L'immagine si sta preparando.</summary>
    Caricamento,

    /// <summary>L'immagine c'è.</summary>
    Pronta,

    /// <summary>Per questo tipo di file non esiste l'anteprima (o è troppo grande): non è un errore.</summary>
    NonDisponibile,

    /// <summary>Il file non si riesce a leggere o a disegnare.</summary>
    Errore
}

/// <param name="Immagine">La pagina disegnata, già "congelata" (si può usare da qualsiasi thread).</param>
/// <param name="Pagine">Quante pagine ha il documento (1 per le immagini singole).</param>
public record RisultatoAnteprima(StatoAnteprima Stato, ImageSource? Immagine = null, int Pagine = 1, string Messaggio = "")
{
    public static RisultatoAnteprima NonDisponibile(string messaggio) => new(StatoAnteprima.NonDisponibile, Messaggio: messaggio);
    public static RisultatoAnteprima InErrore(string messaggio) => new(StatoAnteprima.Errore, Messaggio: messaggio);
}

/// <summary>Disegna l'anteprima di una pagina di un documento.</summary>
public interface IGeneratoreAnteprima
{
    /// <summary>Per questo tipo di file (es. ".pdf") si sa fare l'anteprima.</summary>
    bool Supporta(string estensione);

    /// <summary>L'anteprima di una pagina (la prima è la 0). Non tiene il file aperto: il documento si può spostare o eliminare.</summary>
    Task<RisultatoAnteprima> GeneraAsync(string percorsoAssoluto, int pagina, CancellationToken annullamento);
}

/// <summary>
/// Anteprima di PDF (disegnati con le funzioni integrate di Windows, le stesse della lettura del testo) e di immagini.
/// I file Office non hanno anteprima: si aprono con il loro programma.
/// </summary>
public class GeneratoreAnteprima : IGeneratoreAnteprima
{
    /// <summary>Larghezza in pixel a cui si disegna una pagina: abbastanza per leggere il testo in un pannello largo, senza sprecare memoria.</summary>
    public const int LarghezzaMassima = 1100;

    /// <summary>Oltre questa dimensione il file non si carica in memoria per l'anteprima (si apre con il suo programma).</summary>
    public const long DimensioneMassima = 150L * 1024 * 1024;

    private static readonly HashSet<string> Immagini =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

    public bool Supporta(string estensione) =>
        estensione.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || Immagini.Contains(estensione);

    public async Task<RisultatoAnteprima> GeneraAsync(string percorsoAssoluto, int pagina, CancellationToken annullamento)
    {
        var estensione = Path.GetExtension(percorsoAssoluto).ToLowerInvariant();
        if (!Supporta(estensione))
            return RisultatoAnteprima.NonDisponibile(
                $"Per i file {estensione.TrimStart('.').ToUpperInvariant()} non c'è l'anteprima. Aprilo con il suo programma usando il pulsante «Apri documento» della riga.");

        try
        {
            var info = new FileInfo(percorsoAssoluto);
            if (!info.Exists)
                return RisultatoAnteprima.InErrore("Il file non si trova più nell'archivio.");
            if (info.Length > DimensioneMassima)
                return RisultatoAnteprima.NonDisponibile(
                    $"Il file è troppo grande per l'anteprima ({FormatiTesto.Dimensione(info.Length)}). Aprilo con il pulsante «Apri documento» della riga.");

            // Il file si legge tutto in memoria (senza tenerlo aperto) e poi si disegna da lì.
            var byteFile = await LeggiAsync(percorsoAssoluto, annullamento);
            annullamento.ThrowIfCancellationRequested();

            return estensione == ".pdf"
                ? await DisegnaPdfAsync(byteFile, pagina, annullamento)
                : await Task.Run(() => DisegnaImmagine(byteFile, estensione, pagina), annullamento);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return RisultatoAnteprima.InErrore($"Non è possibile leggere il file: {ex.Message}");
        }
        catch (Exception)
        {
            // Formato rovinato, PDF protetto da password, immagine corrotta: dal sistema arrivano eccezioni di tanti tipi diversi.
            return RisultatoAnteprima.InErrore(estensione == ".pdf"
                ? "Non si riesce a mostrare questo PDF: potrebbe essere protetto da password o danneggiato. Prova ad aprirlo con il pulsante «Apri documento» della riga."
                : "Non si riesce a mostrare questa immagine: il file potrebbe essere danneggiato. Prova ad aprirla con il pulsante «Apri documento» della riga.");
        }
    }

    private static async Task<byte[]> LeggiAsync(string percorso, CancellationToken annullamento)
    {
        // Come per la copia: un file aperto in un altro programma si legge lo stesso.
        await using var flusso = new FileStream(
            percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
        var memoria = new MemoryStream((int)flusso.Length);
        await flusso.CopyToAsync(memoria, annullamento);
        return memoria.ToArray();
    }

    private static async Task<RisultatoAnteprima> DisegnaPdfAsync(byte[] byteFile, int pagina, CancellationToken annullamento)
    {
        using var flusso = new MemoryStream(byteFile);
        var documento = await PdfDocument.LoadFromStreamAsync(flusso.AsRandomAccessStream()).AsTask(annullamento);
        var pagine = (int)documento.PageCount;
        if (pagine == 0)
            return RisultatoAnteprima.InErrore("Il PDF non ha pagine.");

        using var paginaPdf = documento.GetPage((uint)Math.Clamp(pagina, 0, pagine - 1));

        // La pagina si "stampa" in un'immagine PNG (come fa la lettura del testo); i byte si copiano fuori dal flusso di Windows.
        using var uscita = new InMemoryRandomAccessStream();
        await paginaPdf.RenderToStreamAsync(uscita, new PdfPageRenderOptions { DestinationWidth = LarghezzaMassima })
            .AsTask(annullamento);
        uscita.Seek(0);
        using var disegno = new MemoryStream();
        await uscita.AsStreamForRead().CopyToAsync(disegno, annullamento);

        disegno.Position = 0;
        var immagine = new BitmapImage();
        immagine.BeginInit();
        immagine.CacheOption = BitmapCacheOption.OnLoad;
        immagine.StreamSource = disegno;
        immagine.EndInit();
        immagine.Freeze();
        return new RisultatoAnteprima(StatoAnteprima.Pronta, immagine, pagine);
    }

    private static RisultatoAnteprima DisegnaImmagine(byte[] byteFile, string estensione, int pagina)
    {
        using var flusso = new MemoryStream(byteFile);
        var decodificatore = BitmapDecoder.Create(flusso, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        // Un TIFF può avere più pagine (le scansioni); una GIF animata no: se ne mostra il primo fotogramma.
        var pagine = estensione == ".gif" ? 1 : decodificatore.Frames.Count;
        BitmapSource immagine = decodificatore.Frames[Math.Clamp(pagina, 0, pagine - 1)];

        immagine = Ruotata(immagine);
        if (immagine.PixelWidth > LarghezzaMassima)
        {
            var scala = (double)LarghezzaMassima / immagine.PixelWidth;
            var ridotta = new TransformedBitmap(immagine, new ScaleTransform(scala, scala));
            ridotta.Freeze();
            immagine = ridotta;
        }

        immagine.Freeze();
        return new RisultatoAnteprima(StatoAnteprima.Pronta, immagine, pagine);
    }

    /// <summary>Le foto del telefono hanno spesso l'orientamento nei dati EXIF invece che nei pixel: senza questo si vedrebbero di lato.</summary>
    private static BitmapSource Ruotata(BitmapSource immagine)
    {
        double angolo;
        try
        {
            angolo = (immagine as BitmapFrame)?.Metadata is BitmapMetadata metadati
                     && metadati.GetQuery("System.Photo.Orientation") is ushort orientamento
                ? orientamento switch { 3 => 180, 6 => 90, 8 => 270, _ => 0 }
                : 0;
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or ArgumentException)
        {
            return immagine; // PNG, BMP... non hanno questi dati
        }

        if (angolo == 0)
            return immagine;
        var ruotata = new TransformedBitmap(immagine, new RotateTransform(angolo));
        ruotata.Freeze();
        return ruotata;
    }
}
