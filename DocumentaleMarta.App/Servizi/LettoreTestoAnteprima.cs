using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace DocumentaleMarta.App.Servizi;

/// <summary>Le prime righe di un file di testo, pronte da mostrare nell'anteprima.</summary>
/// <param name="Testo">Le righe lette, una per riga.</param>
/// <param name="Righe">Quante righe contiene <paramref name="Testo"/>.</param>
/// <param name="Troncato">Il file continua oltre le righe mostrate.</param>
public record TestoLetto(string Testo, int Righe, bool Troncato);

/// <summary>
/// Legge l'inizio di un file di testo (.txt, .csv, .xml) per l'anteprima: solo le prime righe, e solo i primi kilobyte del file,
/// così anche un testo enorme si mostra subito.
/// </summary>
public static class LettoreTestoAnteprima
{
    /// <summary>Del file non si leggono più di questi byte, qualunque sia il numero di righe richiesto.</summary>
    public const int ByteMassimi = 512 * 1024;

    /// <summary>Una riga più lunga di così (per esempio un CSV su un'unica riga) si taglia e finisce con «…».</summary>
    public const int CaratteriMassimiPerRiga = 1000;

    /// <summary>Quanti byte iniziali si guardano per capire se il file è testo o altro (un file binario rinominato .txt).</summary>
    private const int ByteDaControllare = 4096;

    private static readonly Encoding Utf8Rigido = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Un XML che sta in così poche righe è scritto «tutto di seguito»: lo si rimette in colonna per leggerlo.</summary>
    public const int RigheXmlDaSistemare = 3;

    /// <summary>
    /// Legge le prime righe del file. Restituisce null se il file non sembra di testo (contiene byte nulli, come un file
    /// binario). Un file vuoto dà zero righe.
    /// </summary>
    /// <param name="xml">
    /// Il file è un XML: se sta tutto su poche righe e si legge per intero, lo si mostra con i rientri (un XML scritto su una
    /// riga sola sarebbe illeggibile). Un XML già a capo, o rovinato, si lascia com'è.
    /// </param>
    public static async Task<TestoLetto?> LeggiAsync(
        string percorso, int righeMassime, CancellationToken annullamento, bool xml = false)
    {
        // Come per le altre anteprime: un file aperto in un altro programma si legge lo stesso.
        await using var flusso = new FileStream(
            percorso, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);

        var buffer = new byte[(int)Math.Min(flusso.Length, ByteMassimi)];
        var letti = 0;
        while (letti < buffer.Length)
        {
            var n = await flusso.ReadAsync(buffer.AsMemory(letti), annullamento);
            if (n == 0)
                break;
            letti += n;
        }

        var tagliatoPerDimensione = flusso.Length > letti;
        var testo = Decodifica(buffer.AsSpan(0, letti));
        if (testo is null)
            return null;

        // Un XML tagliato a metà non si può rimettere in colonna: serve il file intero.
        if (xml && !tagliatoPerDimensione)
            testo = XmlConRientri(testo) ?? testo;

        return PrimeRighe(testo, righeMassime, tagliatoPerDimensione);
    }

    /// <summary>
    /// Rimette in colonna, con i rientri, un XML scritto su poche righe (con la dichiarazione iniziale, se c'è). Null se non serve
    /// (ha già tante righe) o se non è un XML valido: il testo si mostra allora com'è.
    /// </summary>
    public static string? XmlConRientri(string testo)
    {
        if (testo.AsSpan().Count('\n') >= RigheXmlDaSistemare)
            return null;

        try
        {
            // Le definizioni di tipo (DTD) si ignorano e non si va a cercare nulla fuori dal file: è solo un'anteprima.
            using var lettore = XmlReader.Create(
                new StringReader(testo), new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null });
            var documento = XDocument.Load(lettore);
            return (documento.Declaration is { } dichiarazione ? dichiarazione + Environment.NewLine : "") + documento;
        }
        catch (XmlException)
        {
            return null;
        }
    }

    /// <summary>
    /// Prende le prime righe del testo (tagliando quelle troppo lunghe) e dice se ne restano altre dopo.
    /// </summary>
    /// <param name="giaTagliato">Il testo è già solo l'inizio del file: dopo le righe lette il file continua comunque.</param>
    public static TestoLetto PrimeRighe(string testo, int righeMassime, bool giaTagliato = false)
    {
        using var lettore = new StringReader(testo);
        var righe = new List<string>();
        while (righe.Count < righeMassime && lettore.ReadLine() is { } riga)
            righe.Add(riga.Length > CaratteriMassimiPerRiga ? riga[..CaratteriMassimiPerRiga] + "…" : riga);

        var altre = lettore.ReadLine() is not null;
        return new TestoLetto(string.Join(Environment.NewLine, righe), righe.Count, altre || giaTagliato);
    }

    /// <summary>
    /// Trasforma i byte in testo: usa la codifica indicata dall'inizio del file (BOM) se c'è; altrimenti UTF-8 se il contenuto lo è
    /// davvero, e Windows-1252 (i vecchi file di testo italiani) se no. Null se è un file binario.
    /// </summary>
    private static string? Decodifica(ReadOnlySpan<byte> byte_)
    {
        var (codifica, inizio) = Riconosci(byte_);
        var contenuto = byte_[inizio..];

        // In un testo UTF-16 i byte nulli sono normali; in tutti gli altri un byte nullo vuol dire che il file non è testo.
        if (codifica.CodePage is not (1200 or 1201) && contenuto[..Math.Min(contenuto.Length, ByteDaControllare)].Contains((byte)0))
            return null;

        try
        {
            return Converti(codifica, contenuto);
        }
        catch (DecoderFallbackException)
        {
            return Converti(CodificaAnsi(), contenuto);
        }
    }

    /// <summary>Guarda i primi byte per capire la codifica dichiarata (BOM) e quanti byte saltare; senza BOM prova UTF-8.</summary>
    private static (Encoding Codifica, int Inizio) Riconosci(ReadOnlySpan<byte> byte_)
    {
        if (byte_.Length >= 3 && byte_[0] == 0xEF && byte_[1] == 0xBB && byte_[2] == 0xBF)
            return (Utf8Rigido, 3);
        if (byte_.Length >= 2 && byte_[0] == 0xFF && byte_[1] == 0xFE)
            return (Encoding.Unicode, 2);
        if (byte_.Length >= 2 && byte_[0] == 0xFE && byte_[1] == 0xFF)
            return (Encoding.BigEndianUnicode, 2);
        return (Utf8Rigido, 0);
    }

    /// <summary>
    /// Decodifica senza considerare un errore l'ultimo carattere spezzato a metà (succede quando si legge solo l'inizio di un
    /// file lungo): un byte non valido in mezzo al testo invece lancia l'eccezione.
    /// </summary>
    private static string Converti(Encoding codifica, ReadOnlySpan<byte> byte_)
    {
        var decodificatore = codifica.GetDecoder();
        var caratteri = new char[codifica.GetMaxCharCount(byte_.Length)];
        var quanti = decodificatore.GetChars(byte_, caratteri, flush: false);
        return new string(caratteri, 0, quanti);
    }

    /// <summary>Windows-1252, la codifica dei vecchi file di testo italiani (con «€» e le virgolette ricurve).</summary>
    private static Encoding CodificaAnsi()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252);
    }
}
