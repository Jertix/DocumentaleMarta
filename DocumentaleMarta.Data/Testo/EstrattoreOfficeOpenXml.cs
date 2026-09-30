using System.Text;
using DocumentaleMarta.Core.Servizi;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;

namespace DocumentaleMarta.Data.Testo;

/// <summary>Word (.docx), Excel (.xlsx) e PowerPoint (.pptx). I vecchi formati .doc, .xls e .ppt non sono leggibili.</summary>
public class EstrattoreOfficeOpenXml : IEstrattoreTesto
{
    private static readonly HashSet<string> Estensioni = [".docx", ".xlsx", ".pptx"];

    public bool Supporta(string estensione) => Estensioni.Contains(estensione);

    public Task<string> EstraiAsync(string percorsoFile, CancellationToken cancellation) =>
        Task.Run(() => Estrai(percorsoFile), cancellation);

    private static string Estrai(string percorsoFile)
    {
        // Condivisione in lettura e scrittura: il file può essere aperto in Office nello stesso momento.
        using var stream = new FileStream(percorsoFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        return Path.GetExtension(percorsoFile).ToLowerInvariant() switch
        {
            ".docx" => EstraiWord(stream),
            ".xlsx" => EstraiExcel(stream),
            ".pptx" => EstraiPowerPoint(stream),
            var altra => throw new NotSupportedException($"Formato non supportato: {altra}")
        };
    }

    private static string EstraiWord(Stream stream)
    {
        using var documento = WordprocessingDocument.Open(stream, isEditable: false);
        var testo = new StringBuilder();

        // Corpo, intestazioni e piè di pagina: il testo utile (es. il numero di una fattura) sta spesso anche lì.
        if (documento.MainDocumentPart is { } principale)
        {
            foreach (var parte in principale.HeaderParts)
                AggiungiParagrafi(testo, parte.Header?.Descendants<Paragraph>());
            AggiungiParagrafi(testo, principale.Document?.Body?.Descendants<Paragraph>());
            foreach (var parte in principale.FooterParts)
                AggiungiParagrafi(testo, parte.Footer?.Descendants<Paragraph>());
        }
        return testo.ToString();
    }

    private static void AggiungiParagrafi(StringBuilder testo, IEnumerable<Paragraph>? paragrafi)
    {
        if (paragrafi is null)
            return;
        foreach (var paragrafo in paragrafi)
        {
            var riga = paragrafo.InnerText;
            if (riga.Length > 0)
                testo.AppendLine(riga);
        }
    }

    private static string EstraiExcel(Stream stream)
    {
        using var documento = SpreadsheetDocument.Open(stream, isEditable: false);
        var cartella = documento.WorkbookPart;
        if (cartella is null)
            return "";

        var testo = new StringBuilder();
        var condivise = cartella.SharedStringTablePart?.SharedStringTable?
            .Elements<SharedStringItem>().Select(i => i.InnerText).ToList() ?? [];

        foreach (var foglio in cartella.Workbook?.Sheets?.Elements<Sheet>() ?? [])
            testo.AppendLine(foglio.Name?.Value);

        foreach (var parte in cartella.WorksheetParts)
        {
            foreach (var cella in parte.Worksheet?.Descendants<Cell>() ?? [])
            {
                var valore = ValoreCella(cella, condivise);
                if (!string.IsNullOrWhiteSpace(valore))
                    testo.AppendLine(valore);
            }
        }
        return testo.ToString();
    }

    private static string? ValoreCella(Cell cella, List<string> condivise)
    {
        if (cella.DataType?.Value == CellValues.SharedString
            && int.TryParse(cella.CellValue?.Text, out var indice)
            && indice >= 0 && indice < condivise.Count)
            return condivise[indice];
        if (cella.DataType?.Value == CellValues.InlineString)
            return cella.InlineString?.InnerText;
        return cella.CellValue?.Text;
    }

    private static string EstraiPowerPoint(Stream stream)
    {
        using var documento = PresentationDocument.Open(stream, isEditable: false);
        var testo = new StringBuilder();

        foreach (var diapositiva in documento.PresentationPart?.SlideParts ?? [])
        {
            foreach (var paragrafo in diapositiva.Slide?.Descendants<A.Paragraph>() ?? [])
            {
                var riga = paragrafo.InnerText;
                if (riga.Length > 0)
                    testo.AppendLine(riga);
            }
        }
        return testo.ToString();
    }
}
