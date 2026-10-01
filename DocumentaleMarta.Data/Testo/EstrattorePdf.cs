using System.Text;
using DocumentaleMarta.Core.Servizi;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace DocumentaleMarta.Data.Testo;

/// <summary>
/// PDF: si legge il testo vero e proprio; se il PDF è una scansione (pagine che sono solo immagini) si ricorre all'OCR.
/// </summary>
/// <param name="ocr">Senza OCR le scansioni restano da leggere; i PDF con testo si leggono comunque.</param>
public class EstrattorePdf(IOcr? ocr = null) : IEstrattoreTesto
{
    /// <summary>
    /// Sotto questa media di lettere e cifre per pagina il PDF si considera una scansione (o quasi):
    /// il testo trovato sarebbe solo un timbro o un numero di pagina.
    /// </summary>
    internal const int CaratteriMinimiPerPagina = 15;

    /// <summary>Vero solo per i file PDF.</summary>
    public bool Supporta(string estensione) => estensione == ".pdf";

    /// <summary>
    /// Legge il testo del PDF; se sembra una scansione (quasi senza testo) lo fa leggere dal riconoscimento del testo e
    /// tiene il risultato migliore.
    /// </summary>
    public async Task<string> EstraiAsync(string percorsoFile, CancellationToken cancellation)
    {
        var (testo, pagine) = await Task.Run(() => LeggiTesto(percorsoFile), cancellation);

        var caratteri = testo.Count(char.IsLetterOrDigit);
        if (pagine == 0 || caratteri >= CaratteriMinimiPerPagina * pagine)
            return testo;

        // Sembra una scansione.
        if (ocr is not { Disponibile: true })
        {
            // Se qualcosa di leggibile c'è, meglio indicizzare quello che c'è che lasciare il documento in sospeso.
            if (caratteri > 0)
                return testo;
            throw new OcrNonDisponibileException(ocr?.MotivoNonDisponibile ?? "Il riconoscimento del testo (OCR) non è disponibile.");
        }

        var riconosciuto = await ocr.RiconosciPdfAsync(percorsoFile, cancellation);
        return riconosciuto.Count(char.IsLetterOrDigit) > caratteri ? riconosciuto : testo;
    }

    /// <summary>Legge il testo di tutte le pagine e dice quante sono.</summary>
    private static (string Testo, int Pagine) LeggiTesto(string percorsoFile)
    {
        // Condivisione in lettura e scrittura: il file può essere aperto in un lettore PDF nello stesso momento.
        using var stream = new FileStream(percorsoFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var documento = PdfDocument.Open(stream);

        var testo = new StringBuilder();
        foreach (var pagina in documento.GetPages())
        {
            testo.AppendLine(TestoDellaPagina(pagina));
        }
        return (testo.ToString(), documento.NumberOfPages);
    }

    /// <summary>
    /// Il testo di una pagina, nell'ordine di lettura; se l'analisi fallisce si usano le parole nell'ordine grezzo.
    /// </summary>
    private static string TestoDellaPagina(UglyToad.PdfPig.Content.Page pagina)
    {
        try
        {
            return ContentOrderTextExtractor.GetText(pagina);
        }
        catch (Exception)
        {
            // L'analisi dell'ordine di lettura può fallire su pagine strane: le parole, nell'ordine grezzo, bastano per cercare.
            return string.Join(" ", pagina.GetWords().Select(p => p.Text));
        }
    }
}
