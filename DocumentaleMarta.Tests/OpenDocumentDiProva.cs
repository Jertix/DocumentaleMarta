using System.IO.Compression;
using System.Security;
using System.Text;

namespace DocumentaleMarta.Tests;

/// <summary>Crea al volo file OpenDocument veri (ZIP con content.xml, styles.xml...) come li scrivono OpenOffice e LibreOffice.</summary>
public static class OpenDocumentDiProva
{
    private const string Namespace = """
        xmlns:office="urn:oasis:names:tc:opendocument:xmlns:office:1.0"
        xmlns:text="urn:oasis:names:tc:opendocument:xmlns:text:1.0"
        xmlns:table="urn:oasis:names:tc:opendocument:xmlns:table:1.0"
        xmlns:draw="urn:oasis:names:tc:opendocument:xmlns:drawing:1.0"
        xmlns:style="urn:oasis:names:tc:opendocument:xmlns:style:1.0"
        xmlns:svg="urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0"
        xmlns:xlink="http://www.w3.org/1999/xlink"
        xmlns:dc="http://purl.org/dc/elements/1.1/"
        """;

    public static string Testo(string valore) => SecurityElement.Escape(valore);

    private static string P(string testo) => $"<text:p text:style-name=\"Standard\">{Testo(testo)}</text:p>";

    /// <param name="tipo">"text", "spreadsheet", "presentation" o "drawing".</param>
    /// <param name="corpoXml">Quello che sta dentro office:text, office:spreadsheet...</param>
    /// <param name="stiliXml">Quello che sta dentro office:master-styles (intestazioni e piè di pagina).</param>
    /// <param name="miniatura">I byte del PNG con la prima pagina; null = il documento non ne ha.</param>
    /// <param name="cifrato">Come un documento protetto da password: il manifesto dichiara dati di cifratura.</param>
    /// <param name="senzaContenuto">Uno ZIP qualsiasi, senza content.xml.</param>
    public static string Documento(
        string percorso, string tipo, string corpoXml, string? stiliXml = null, byte[]? miniatura = null,
        bool cifrato = false, bool senzaContenuto = false, string? mimetype = null)
    {
        using var flusso = new FileStream(percorso, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(flusso, ZipArchiveMode.Create);

        Scrivi(zip, "mimetype", mimetype ?? $"application/vnd.oasis.opendocument.{tipo}");

        var cifratura = cifrato
            ? "<manifest:encryption-data manifest:checksum-type=\"SHA1/1K\" manifest:checksum=\"abc\"><manifest:algorithm manifest:algorithm-name=\"Blowfish CFB\"/></manifest:encryption-data>"
            : "";
        Scrivi(zip, "META-INF/manifest.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>"
            + "<manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\" manifest:version=\"1.3\">"
            + $"<manifest:file-entry manifest:full-path=\"content.xml\" manifest:media-type=\"text/xml\">{cifratura}</manifest:file-entry>"
            + "</manifest:manifest>");

        if (!senzaContenuto)
        {
            Scrivi(zip, "content.xml",
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document-content {Namespace} office:version=\"1.3\">"
                + $"<office:automatic-styles/><office:body><office:{tipo}>{corpoXml}</office:{tipo}></office:body></office:document-content>");
            Scrivi(zip, "styles.xml",
                $"<?xml version=\"1.0\" encoding=\"UTF-8\"?><office:document-styles {Namespace} office:version=\"1.3\">"
                + $"<office:master-styles>{stiliXml}</office:master-styles></office:document-styles>");
        }

        if (miniatura is not null)
        {
            var voce = zip.CreateEntry("Thumbnails/thumbnail.png");
            using var uscita = voce.Open();
            uscita.Write(miniatura);
        }
        return percorso;
    }

    private static void Scrivi(ZipArchive zip, string nome, string contenuto)
    {
        var voce = zip.CreateEntry(nome);
        using var scrittore = new StreamWriter(voce.Open(), new UTF8Encoding(false));
        scrittore.Write(contenuto);
    }

    /// <summary>Un documento di testo con un paragrafo per ogni voce, e se indicati intestazione e piè di pagina.</summary>
    public static string Odt(
        string percorso, string[] paragrafi, string? intestazione = null, string? pieDiPagina = null, byte[]? miniatura = null)
    {
        var stili = "";
        if (intestazione is not null || pieDiPagina is not null)
            stili = "<style:master-page style:name=\"Standard\" style:page-layout-name=\"Mpm1\">"
                    + (intestazione is null ? "" : $"<style:header>{P(intestazione)}</style:header>")
                    + (pieDiPagina is null ? "" : $"<style:footer>{P(pieDiPagina)}</style:footer>")
                    + "</style:master-page>";
        return Documento(percorso, "text", string.Concat(paragrafi.Select(P)), stili, miniatura);
    }

    /// <summary>Fogli di calcolo: per ogni foglio il nome e le celle di testo.</summary>
    public static string Ods(string percorso, params (string Foglio, string[] Celle)[] fogli)
    {
        var corpo = string.Concat(fogli.Select(f =>
            $"<table:table table:name=\"{Testo(f.Foglio)}\"><table:table-column table:number-columns-repeated=\"1024\"/>"
            + "<table:table-row>"
            + string.Concat(f.Celle.Select(c =>
                $"<table:table-cell office:value-type=\"string\">{P(c)}</table:table-cell>"))
            + "<table:table-cell table:number-columns-repeated=\"1020\"/></table:table-row>"
            + "<table:table-row table:number-rows-repeated=\"1048570\"><table:table-cell table:number-columns-repeated=\"1024\"/></table:table-row>"
            + "</table:table>"));
        return Documento(percorso, "spreadsheet", corpo);
    }

    /// <summary>Una presentazione con una diapositiva per ogni testo.</summary>
    public static string Odp(string percorso, params string[] diapositive)
    {
        var corpo = string.Concat(diapositive.Select((testo, i) =>
            $"<draw:page draw:name=\"page{i + 1}\" draw:master-page-name=\"Default\">"
            + $"<draw:frame draw:style-name=\"gr1\" svg:width=\"20cm\" svg:height=\"3cm\"><draw:text-box>{P(testo)}</draw:text-box></draw:frame>"
            + "</draw:page>"));
        return Documento(percorso, "presentation", corpo);
    }

    /// <summary>I byte di un PNG di prova (un gradiente), da usare come miniatura.</summary>
    public static byte[] PngDiProva(int larghezza = 128, int altezza = 180)
    {
        var stride = larghezza * 3;
        var dati = new byte[stride * altezza];
        for (var y = 0; y < altezza; y++)
            for (var x = 0; x < larghezza; x++)
            {
                dati[y * stride + x * 3] = (byte)(x * 255 / larghezza);
                dati[y * stride + x * 3 + 1] = (byte)(y * 255 / altezza);
            }

        var sorgente = System.Windows.Media.Imaging.BitmapSource.Create(
            larghezza, altezza, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null, dati, stride);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(sorgente));
        using var memoria = new MemoryStream();
        encoder.Save(memoria);
        return memoria.ToArray();
    }
}
