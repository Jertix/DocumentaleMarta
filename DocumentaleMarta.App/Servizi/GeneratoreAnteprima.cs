using System.IO;
using System.IO.Compression;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using Windows.Data.Pdf;
using Windows.Storage.Streams;

namespace DocumentaleMarta.App.Servizi;

/// <summary>Cosa mostra il pannello dell'anteprima.</summary>
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
/// <param name="Nota">Una precisazione da mostrare sotto l'immagine o il testo (es. che è solo una miniatura, o che il testo è parziale).</param>
/// <param name="Testo">Per i file di testo: le prime righe, da mostrare al posto dell'immagine.</param>
/// <param name="TestoACapo">Le righe lunghe vanno a capo (testo semplice) oppure si scorrono di lato (tabelle CSV).</param>
public record RisultatoAnteprima(
    StatoAnteprima Stato, ImageSource? Immagine = null, int Pagine = 1, string Messaggio = "", string Nota = "",
    string? Testo = null, bool TestoACapo = true)
{
    /// <summary>Un risultato con le prime righe di un file di testo, al posto dell'immagine.</summary>
    public static RisultatoAnteprima DiTesto(string testo, bool aCapo, string nota = "") =>
        new(StatoAnteprima.Pronta, Testo: testo, TestoACapo: aCapo, Nota: nota);

    /// <summary>
    /// Un risultato che dice «per questo file l'anteprima non c'è» (non è un errore) con il motivo da mostrare.
    /// </summary>
    public static RisultatoAnteprima NonDisponibile(string messaggio) => new(StatoAnteprima.NonDisponibile, Messaggio: messaggio);
    /// <summary>Un risultato che dice «il file non si riesce a leggere o disegnare» con il motivo da mostrare.</summary>
    public static RisultatoAnteprima InErrore(string messaggio) => new(StatoAnteprima.Errore, Messaggio: messaggio);
}

/// <summary>Disegna l'anteprima di una pagina di un documento.</summary>
public interface IGeneratoreAnteprima
{
    /// <summary>Per questo tipo di file (es. ".pdf") si sa fare l'anteprima.</summary>
    bool Supporta(string estensione);

    /// <summary>L'anteprima di una pagina (la prima è la 0). Non tiene il file aperto: il documento si può spostare o eliminare.</summary>
    /// <param name="larghezzaMassima">
    /// Larghezza in pixel a cui si disegna una pagina PDF (e oltre la quale un'immagine si rimpicciolisce): il pannello usa il valore
    /// predefinito, la finestra ingrandita uno più alto per vedere il testo nitido.
    /// </param>
    Task<RisultatoAnteprima> GeneraAsync(
        string percorsoAssoluto, int pagina, CancellationToken annullamento,
        int larghezzaMassima = GeneratoreAnteprima.LarghezzaMassima);
}

/// <summary>
/// Anteprima di PDF (disegnati con le funzioni integrate di Windows, le stesse della lettura del testo), di immagini, di documenti
/// OpenOffice/LibreOffice (la miniatura) e di file di testo (.txt, .csv, .xml: le prime righe).
/// I file Office non hanno anteprima: si aprono con il loro programma.
/// </summary>
/// <param name="righeTesto">
/// Quante righe mostra l'anteprima di un file di testo, chiesto a ogni anteprima (così una modifica alle impostazioni vale
/// subito); senza, 100.
/// </param>
public class GeneratoreAnteprima(Func<int>? righeTesto = null) : IGeneratoreAnteprima
{
    /// <summary>Larghezza in pixel a cui si disegna una pagina: abbastanza per leggere il testo in un pannello largo, senza sprecare memoria.</summary>
    public const int LarghezzaMassima = 1100;

    /// <summary>Larghezza a cui si disegna una pagina nella finestra ingrandita: il testo si legge anche ingrandendo.</summary>
    public const int LarghezzaIngrandita = 2600;

    /// <summary>Oltre questa dimensione il file non si carica in memoria per l'anteprima (si apre con il suo programma).</summary>
    public const long DimensioneMassima = 150L * 1024 * 1024;

    private static readonly HashSet<string> Immagini =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff" };

    private static readonly HashSet<string> FileDiTesto = new(StringComparer.OrdinalIgnoreCase) { ".txt", ".csv", ".xml" };

    /// <summary>I file di testo che non vanno a capo: le colonne dei CSV e i rientri degli XML si leggono solo se la riga resta intera.</summary>
    private static readonly HashSet<string> TestoSenzaACapo = new(StringComparer.OrdinalIgnoreCase) { ".csv", ".xml" };

    /// <summary>
    /// Vero se per questo tipo di file (PDF, immagini, documenti di OpenOffice/LibreOffice, testo semplice) si può fare l'anteprima.
    /// </summary>
    public bool Supporta(string estensione) =>
        estensione.Equals(".pdf", StringComparison.OrdinalIgnoreCase) || Immagini.Contains(estensione)
        || FormatiOpenDocument.Contiene(estensione) || FileDiTesto.Contains(estensione);

    /// <summary>Quante righe mostrare per un file di testo: quelle scelte nelle impostazioni, tenute tra il minimo e il massimo ammessi.</summary>
    private int RigheDiTesto() =>
        Math.Clamp(righeTesto?.Invoke() ?? ImpostazioniApp.RigheAnteprimaPredefinite,
            ImpostazioniApp.RigheAnteprimaMinime, ImpostazioniApp.RigheAnteprimaMassime);

    /// <summary>
    /// Prepara l'anteprima di una pagina: sceglie come disegnare il file in base al tipo, controlla che esista e non sia
    /// enorme, e trasforma gli errori in messaggi comprensibili.
    /// </summary>
    public async Task<RisultatoAnteprima> GeneraAsync(
        string percorsoAssoluto, int pagina, CancellationToken annullamento, int larghezzaMassima = LarghezzaMassima)
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

            // Un file di OpenOffice o LibreOffice porta con sé la miniatura della prima pagina: si legge solo quella,
            // senza caricare il documento (che può essere grande).
            if (FormatiOpenDocument.Contiene(estensione))
                return await Task.Run(() => DisegnaMiniaturaOpenDocument(percorsoAssoluto, larghezzaMassima), annullamento);

            // Un file di testo si legge solo all'inizio, quindi anche uno enorme si mostra subito.
            if (FileDiTesto.Contains(estensione))
                return await LeggiTestoAsync(percorsoAssoluto, estensione, annullamento);

            if (info.Length > DimensioneMassima)
                return RisultatoAnteprima.NonDisponibile(
                    $"Il file è troppo grande per l'anteprima ({FormatiTesto.Dimensione(info.Length)}). Aprilo con il pulsante «Apri documento» della riga.");

            // Il file si legge tutto in memoria (senza tenerlo aperto) e poi si disegna da lì.
            var byteFile = await LeggiAsync(percorsoAssoluto, annullamento);
            annullamento.ThrowIfCancellationRequested();

            return estensione == ".pdf"
                ? await DisegnaPdfAsync(byteFile, pagina, larghezzaMassima, annullamento)
                : await Task.Run(() => DisegnaImmagine(byteFile, estensione, pagina, larghezzaMassima), annullamento);
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
            return RisultatoAnteprima.InErrore(estensione switch
            {
                ".pdf" => "Non si riesce a mostrare questo PDF: potrebbe essere protetto da password o danneggiato. Prova ad aprirlo con il pulsante «Apri documento» della riga.",
                _ when FormatiOpenDocument.Contiene(estensione) => "Non si riesce a mostrare questo documento: il file potrebbe essere danneggiato. Prova ad aprirlo con il pulsante «Apri documento» della riga.",
                _ when FileDiTesto.Contains(estensione) => "Non si riesce a leggere questo file di testo. Prova ad aprirlo con il pulsante «Apri documento» della riga.",
                _ => "Non si riesce a mostrare questa immagine: il file potrebbe essere danneggiato. Prova ad aprirla con il pulsante «Apri documento» della riga."
            });
        }
    }

    public const string NotaMiniatura = "Miniatura della prima pagina, salvata nel documento.";

    /// <summary>La precisazione sotto un testo mostrato solo in parte: dice quante righe si vedono.</summary>
    public static string NotaTestoParziale(int righe) =>
        $"Si vedono le prime {righe} righe. Per leggere il resto apri il file con il pulsante «Apri documento» della riga.";

    /// <summary>
    /// Mostra le prime righe di un file di testo. I file CSV e XML non vanno a capo (le colonne e i rientri resterebbero spezzati): si
    /// scorrono di lato. Un file vuoto o che non è testo (un binario rinominato .txt) non ha anteprima.
    /// </summary>
    private async Task<RisultatoAnteprima> LeggiTestoAsync(string percorso, string estensione, CancellationToken annullamento)
    {
        var letto = await LettoreTestoAnteprima.LeggiAsync(percorso, RigheDiTesto(), annullamento, xml: estensione == ".xml");
        if (letto is null)
            return RisultatoAnteprima.NonDisponibile(
                "Questo file non sembra di testo. Aprilo con il suo programma usando il pulsante «Apri documento» della riga.");
        if (letto.Righe == 0)
            return RisultatoAnteprima.NonDisponibile("Il file è vuoto.");

        return RisultatoAnteprima.DiTesto(
            letto.Testo, aCapo: !TestoSenzaACapo.Contains(estensione), nota: letto.Troncato ? NotaTestoParziale(letto.Righe) : "");
    }

    /// <summary>Oltre questa dimensione una "miniatura" non è credibile: si ignora.</summary>
    private const long DimensioneMassimaMiniatura = 20L * 1024 * 1024;

    /// <summary>
    /// Per i documenti di OpenOffice/LibreOffice legge la miniatura della prima pagina che il file porta con sé, senza
    /// caricare il documento.
    /// </summary>
    private static RisultatoAnteprima DisegnaMiniaturaOpenDocument(string percorso, int larghezzaMassima)
    {
        using var flusso = new FileStream(percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var archivio = new ZipArchive(flusso, ZipArchiveMode.Read);

        var voce = archivio.GetEntry(FormatiOpenDocument.PercorsoMiniatura);
        if (voce is null || voce.Length is 0 or > DimensioneMassimaMiniatura)
            return RisultatoAnteprima.NonDisponibile(
                "Questo documento non contiene l'anteprima. Aprilo con il suo programma usando il pulsante «Apri documento» della riga.");

        using var miniatura = new MemoryStream();
        using (var origine = voce.Open())
            origine.CopyTo(miniatura);

        // È un'immagine PNG della prima pagina: si mostra come qualsiasi altra immagine, dicendo però che è solo una miniatura.
        return DisegnaImmagine(miniatura.ToArray(), ".png", 0, larghezzaMassima) with { Nota = NotaMiniatura };
    }

    /// <summary>
    /// Legge tutto il file in memoria senza tenerlo aperto (si legge anche se un altro programma lo sta usando).
    /// </summary>
    private static async Task<byte[]> LeggiAsync(string percorso, CancellationToken annullamento)
    {
        // Come per la copia: un file aperto in un altro programma si legge lo stesso.
        await using var flusso = new FileStream(
            percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
        var memoria = new MemoryStream((int)flusso.Length);
        await flusso.CopyToAsync(memoria, annullamento);
        return memoria.ToArray();
    }

    /// <summary>
    /// Disegna una pagina di un PDF con le funzioni di Windows e la restituisce come immagine, insieme al numero di pagine
    /// del documento.
    /// </summary>
    private static async Task<RisultatoAnteprima> DisegnaPdfAsync(
        byte[] byteFile, int pagina, int larghezzaMassima, CancellationToken annullamento)
    {
        using var flusso = new MemoryStream(byteFile);
        var documento = await PdfDocument.LoadFromStreamAsync(flusso.AsRandomAccessStream()).AsTask(annullamento);
        var pagine = (int)documento.PageCount;
        if (pagine == 0)
            return RisultatoAnteprima.InErrore("Il PDF non ha pagine.");

        using var paginaPdf = documento.GetPage((uint)Math.Clamp(pagina, 0, pagine - 1));

        // La pagina si "stampa" in un'immagine PNG (come fa la lettura del testo); i byte si copiano fuori dal flusso di Windows.
        using var uscita = new InMemoryRandomAccessStream();
        await paginaPdf.RenderToStreamAsync(uscita, new PdfPageRenderOptions { DestinationWidth = (uint)larghezzaMassima })
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

    /// <summary>
    /// Prepara l'immagine da mostrare: sceglie la pagina (per i TIFF a più pagine), la raddrizza secondo i dati della
    /// fotocamera e la rimpicciolisce se è più larga del necessario.
    /// </summary>
    private static RisultatoAnteprima DisegnaImmagine(byte[] byteFile, string estensione, int pagina, int larghezzaMassima)
    {
        using var flusso = new MemoryStream(byteFile);
        var decodificatore = BitmapDecoder.Create(flusso, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);

        // Un TIFF può avere più pagine (le scansioni); una GIF animata no: se ne mostra il primo fotogramma.
        var pagine = estensione == ".gif" ? 1 : decodificatore.Frames.Count;
        BitmapSource immagine = decodificatore.Frames[Math.Clamp(pagina, 0, pagine - 1)];

        immagine = Ruotata(immagine);
        if (immagine.PixelWidth > larghezzaMassima)
        {
            var scala = (double)larghezzaMassima / immagine.PixelWidth;
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
