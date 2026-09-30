using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using A = DocumentFormat.OpenXml.Drawing;
using P = DocumentFormat.OpenXml.Presentation;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace DocumentaleMarta.Tests;

/// <summary>Crea al volo file veri (Word, Excel, PowerPoint, PDF) con un contenuto noto, per provare gli estrattori.</summary>
public static class FileDiProva
{
    public static string Docx(string percorso, string[] paragrafi, string? intestazione = null, string? pieDiPagina = null)
    {
        using var documento = WordprocessingDocument.Create(percorso, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var principale = documento.AddMainDocumentPart();
        principale.Document = new W.Document(new W.Body(paragrafi.Select(p => new W.Paragraph(new W.Run(new W.Text(p))))));

        if (intestazione is not null)
            principale.AddNewPart<HeaderPart>().Header = new W.Header(new W.Paragraph(new W.Run(new W.Text(intestazione))));
        if (pieDiPagina is not null)
            principale.AddNewPart<FooterPart>().Footer = new W.Footer(new W.Paragraph(new W.Run(new W.Text(pieDiPagina))));
        return percorso;
    }

    /// <summary>Un foglio con: una cella di testo condiviso, una di testo "inline" e un numero.</summary>
    public static string Xlsx(string percorso, string nomeFoglio, string testoCondiviso, string testoInline, string numero)
    {
        using var documento = SpreadsheetDocument.Create(percorso, DocumentFormat.OpenXml.SpreadsheetDocumentType.Workbook);
        var cartella = documento.AddWorkbookPart();
        cartella.Workbook = new S.Workbook();

        var condivise = cartella.AddNewPart<SharedStringTablePart>();
        condivise.SharedStringTable = new S.SharedStringTable(new S.SharedStringItem(new S.Text(testoCondiviso)));

        var foglio = cartella.AddNewPart<WorksheetPart>();
        foglio.Worksheet = new S.Worksheet(new S.SheetData(new S.Row(
            new S.Cell { DataType = S.CellValues.SharedString, CellValue = new S.CellValue("0") },
            new S.Cell { DataType = S.CellValues.InlineString, InlineString = new S.InlineString(new S.Text(testoInline)) },
            new S.Cell { CellValue = new S.CellValue(numero) })));

        cartella.Workbook.AppendChild(new S.Sheets(new S.Sheet { Id = cartella.GetIdOfPart(foglio), SheetId = 1, Name = nomeFoglio }));
        return percorso;
    }

    public static string Pptx(string percorso, params string[] testiDelleDiapositive)
    {
        using var documento = PresentationDocument.Create(percorso, DocumentFormat.OpenXml.PresentationDocumentType.Presentation);
        var presentazione = documento.AddPresentationPart();
        presentazione.Presentation = new P.Presentation();

        foreach (var testo in testiDelleDiapositive)
        {
            var diapositiva = presentazione.AddNewPart<SlidePart>();
            diapositiva.Slide = new P.Slide(new P.CommonSlideData(new P.ShapeTree(new P.Shape(
                new P.TextBody(new A.BodyProperties(), new A.ListStyle(), new A.Paragraph(new A.Run(new A.Text(testo))))))));
        }
        return percorso;
    }

    /// <summary>Un PDF con testo vero: una pagina per ogni testo.</summary>
    public static string Pdf(string percorso, params string[] testiDelleDiapositive)
    {
        var costruttore = new PdfDocumentBuilder();
        var carattere = costruttore.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var testo in testiDelleDiapositive)
        {
            var pagina = costruttore.AddPage(PageSize.A4);
            pagina.AddText(testo, 14, new PdfPoint(40, 780), carattere);
        }
        File.WriteAllBytes(percorso, costruttore.Build());
        return percorso;
    }

    /// <summary>Un PDF con pagine vuote (nessun testo vero), come una scansione vista dal punto di vista del testo.</summary>
    public static string PdfSenzaTesto(string percorso, int pagine = 1)
    {
        var costruttore = new PdfDocumentBuilder();
        for (var i = 0; i < pagine; i++)
            costruttore.AddPage(PageSize.A4);
        File.WriteAllBytes(percorso, costruttore.Build());
        return percorso;
    }
}
