using System.IO.Compression;
using System.Text;
using System.Xml;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Data.Testo;

/// <summary>
/// Legge il testo dei file di OpenOffice e LibreOffice: documenti (.odt), fogli di calcolo (.ods), presentazioni (.odp)
/// e disegni (.odg), anche i loro modelli. Sono archivi ZIP: il testo sta nei paragrafi (<c>text:p</c>) e nei titoli
/// (<c>text:h</c>) di <c>content.xml</c>, e le intestazioni e i piè di pagina in <c>styles.xml</c>.
/// </summary>
public class EstrattoreOpenDocument : IEstrattoreTesto
{
    private const string NsTesto = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private const string NsTabella = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";

    /// <summary>Un XML più grande di così (decompresso) non si legge: un file costruito apposta potrebbe bloccare il programma.</summary>
    private const long DimensioneMassimaXml = 400L * 1024 * 1024;

    private static readonly XmlReaderSettings ImpostazioniXml = new()
    {
        // Un file Office non ha bisogno di DTD ed entità esterne: vietarli evita attacchi tramite file costruiti ad arte.
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true
    };

    public bool Supporta(string estensione) => FormatiOpenDocument.Contiene(estensione);

    public Task<string> EstraiAsync(string percorsoFile, CancellationToken cancellation) =>
        Task.Run(() => Estrai(percorsoFile, cancellation), cancellation);

    private static string Estrai(string percorsoFile, CancellationToken cancellation)
    {
        // Condivisione in lettura e scrittura: il file può essere aperto in LibreOffice nello stesso momento.
        using var stream = new FileStream(percorsoFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var archivio = new ZipArchive(stream, ZipArchiveMode.Read);

        var contenuto = archivio.GetEntry("content.xml")
                        ?? throw new InvalidDataException("Il file non è un documento OpenDocument: manca content.xml.");

        // Un documento protetto da password ha il contenuto cifrato: non c'è testo da leggere.
        if (ECifrato(archivio))
            return "";

        var testo = new StringBuilder();

        // Prima intestazioni e piè di pagina, poi il corpo (come per i documenti Word).
        if (archivio.GetEntry("styles.xml") is { } stili)
            LeggiTesto(stili, testo, cancellation);
        LeggiTesto(contenuto, testo, cancellation);
        return testo.ToString();
    }

    private static bool ECifrato(ZipArchive archivio)
    {
        if (archivio.GetEntry("META-INF/manifest.xml") is not { } manifest || manifest.Length > 5 * 1024 * 1024)
            return false;

        using var lettore = new StreamReader(manifest.Open());
        return lettore.ReadToEnd().Contains("encryption-data", StringComparison.Ordinal);
    }

    private static void LeggiTesto(ZipArchiveEntry voce, StringBuilder uscita, CancellationToken cancellation)
    {
        if (voce.Length > DimensioneMassimaXml)
            throw new InvalidDataException($"Il file «{voce.FullName}» è troppo grande per essere letto.");

        using var flusso = voce.Open();
        using var lettore = XmlReader.Create(flusso, ImpostazioniXml);

        // Un paragrafo può contenerne altri (una cornice con del testo, una nota a piè di pagina): una pila
        // tiene il testo di ognuno separato.
        var paragrafi = new Stack<StringBuilder>();

        while (lettore.Read())
        {
            cancellation.ThrowIfCancellationRequested();

            switch (lettore.NodeType)
            {
                case XmlNodeType.Element when lettore.NamespaceURI == NsTesto:
                    InizioElementoTesto(lettore, paragrafi);
                    break;

                case XmlNodeType.Element when lettore.NamespaceURI == NsTabella && lettore.LocalName == "table":
                    // Il nome di un foglio di calcolo è testo utile da cercare (es. "Fatture 2026").
                    if (lettore.GetAttribute("name", NsTabella) is { Length: > 0 } nome)
                        uscita.AppendLine(nome);
                    break;

                case XmlNodeType.Text or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace or XmlNodeType.CDATA:
                    if (paragrafi.Count > 0)
                        paragrafi.Peek().Append(lettore.Value);
                    break;

                case XmlNodeType.EndElement when lettore.NamespaceURI == NsTesto && EParagrafo(lettore.LocalName):
                    var riga = paragrafi.Pop().ToString().Trim();
                    if (riga.Length > 0)
                        uscita.AppendLine(riga);
                    break;
            }
        }
    }

    private static void InizioElementoTesto(XmlReader lettore, Stack<StringBuilder> paragrafi)
    {
        var nome = lettore.LocalName;
        if (EParagrafo(nome))
        {
            // Un paragrafo vuoto (<text:p/>) non ha una fine: non c'è nulla da raccogliere.
            if (!lettore.IsEmptyElement)
                paragrafi.Push(new StringBuilder());
            return;
        }

        if (paragrafi.Count == 0)
            return;

        var corrente = paragrafi.Peek();
        switch (nome)
        {
            case "s": // più spazi di seguito: <text:s text:c="3"/>
                var quanti = int.TryParse(lettore.GetAttribute("c", NsTesto), out var n) ? Math.Clamp(n, 1, 1000) : 1;
                corrente.Append(' ', quanti);
                break;
            case "tab":
                corrente.Append('\t');
                break;
            case "line-break":
                corrente.Append('\n');
                break;
        }
    }

    private static bool EParagrafo(string nomeLocale) => nomeLocale is "p" or "h";
}
