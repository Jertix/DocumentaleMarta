using System.Text;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data.Testo;

namespace DocumentaleMarta.Tests;

/// <summary>Un OCR finto: risponde quello che gli si dice e ricorda quante volte è stato usato.</summary>
public class FintoOcr(bool disponibile = true, string testo = "testo riconosciuto") : IOcr
{
    public bool Disponibile { get; } = disponibile;
    public string? MotivoNonDisponibile { get; } = disponibile ? null : "Manca il pacchetto della lingua italiana.";
    public string? Lingua { get; } = disponibile ? "italiano" : null;
    public List<string> Immagini { get; } = [];
    public List<string> Pdf { get; } = [];

    public Task<string> RiconosciImmagineAsync(string percorsoFile, CancellationToken cancellation)
    {
        Immagini.Add(percorsoFile);
        return Task.FromResult(testo);
    }

    public Task<string> RiconosciPdfAsync(string percorsoFile, CancellationToken cancellation)
    {
        Pdf.Add(percorsoFile);
        return Task.FromResult(testo);
    }
}

public class EstrattoriTestoTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private string Percorso(string nome) => _tmp.Combina(nome);

    private static Task<string> Estrai(IEstrattoreTesto estrattore, string percorso) =>
        estrattore.EstraiAsync(percorso, CancellationToken.None);

    // ---------- Testo semplice ----------

    [Fact]
    public async Task Testo_Utf8_ConAccenti()
    {
        var percorso = _tmp.CreaFile("a.txt", "Società Metalli è più");

        Assert.Equal("Società Metalli è più", await Estrai(new EstrattoreTestoSemplice(), percorso));
    }

    [Fact]
    public async Task Testo_Utf8ConBom_ILBomNonEntraNelTesto()
    {
        var percorso = Percorso("a.txt");
        File.WriteAllBytes(percorso, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("ciao")]);

        Assert.Equal("ciao", await Estrai(new EstrattoreTestoSemplice(), percorso));
    }

    [Fact]
    public async Task Testo_Windows1252_SiLeggeComeTestoItaliano()
    {
        var percorso = Percorso("vecchio.txt");
        // "è" in Windows-1252 è il byte 0xE8, che in UTF-8 non sarebbe valido.
        File.WriteAllBytes(percorso, [.. "societ"u8.ToArray(), 0xE0, 0x20, .. "pi"u8.ToArray(), 0xF9]);

        Assert.Equal("società più", await Estrai(new EstrattoreTestoSemplice(), percorso));
    }

    [Fact]
    public async Task Testo_Csv()
    {
        var percorso = _tmp.CreaFile("clienti.csv", "nome;importo\nRossi;1200\nBianchi;830");

        var testo = await Estrai(new EstrattoreTestoSemplice(), percorso);

        Assert.Contains("Bianchi", testo);
    }

    [Fact]
    public async Task Testo_FileVuoto_TestoVuoto()
    {
        Assert.Equal("", await Estrai(new EstrattoreTestoSemplice(), _tmp.CreaFile("vuoto.txt", "")));
    }

    [Fact]
    public async Task Testo_FileEnorme_SiLeggeSoloLInizio()
    {
        var percorso = Percorso("enorme.txt");
        File.WriteAllText(percorso, new string('a', 6 * 1024 * 1024));

        var testo = await Estrai(new EstrattoreTestoSemplice(), percorso);

        Assert.Equal(5 * 1024 * 1024, testo.Length);
    }

    [Fact]
    public async Task Testo_FileApertoInAltroProgramma_SiLeggeComunque()
    {
        var percorso = _tmp.CreaFile("aperto.txt", "contenuto");
        using var aperto = new FileStream(percorso, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal("contenuto", await Estrai(new EstrattoreTestoSemplice(), percorso));
    }

    [Theory]
    [InlineData(".txt", true)]
    [InlineData(".csv", true)]
    [InlineData(".pdf", false)]
    [InlineData(".docx", false)]
    public void Testo_Supporta(string estensione, bool atteso) =>
        Assert.Equal(atteso, new EstrattoreTestoSemplice().Supporta(estensione));

    // ---------- Word ----------

    [Fact]
    public async Task Word_LeggeParagrafi_Intestazione_EPieDiPagina()
    {
        var percorso = FileDiProva.Docx(Percorso("a.docx"),
            ["Fattura numero 12345", "Cliente Rossi Mario"], intestazione: "METAL PROJET", pieDiPagina: "Pagina riservata");

        var testo = await Estrai(new EstrattoreOfficeOpenXml(), percorso);

        Assert.Contains("Fattura numero 12345", testo);
        Assert.Contains("Cliente Rossi Mario", testo);
        Assert.Contains("METAL PROJET", testo);
        Assert.Contains("Pagina riservata", testo);
    }

    [Fact]
    public async Task Word_ConAccenti()
    {
        var percorso = FileDiProva.Docx(Percorso("a.docx"), ["Società è più"]);

        Assert.Contains("Società è più", await Estrai(new EstrattoreOfficeOpenXml(), percorso));
    }

    [Fact]
    public async Task Word_FileApertoInWord_SiLeggeComunque()
    {
        var percorso = FileDiProva.Docx(Percorso("a.docx"), ["contenuto importante"]);
        using var aperto = new FileStream(percorso, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Contains("contenuto importante", await Estrai(new EstrattoreOfficeOpenXml(), percorso));
    }

    // ---------- Excel ----------

    [Fact]
    public async Task Excel_LeggeNomeFoglio_TestoCondiviso_TestoInline_ENumeri()
    {
        var percorso = FileDiProva.Xlsx(Percorso("a.xlsx"), "Fatture 2026", "Cliente Bianchi", "Nota interna", "4200");

        var testo = await Estrai(new EstrattoreOfficeOpenXml(), percorso);

        Assert.Contains("Fatture 2026", testo);
        Assert.Contains("Cliente Bianchi", testo);
        Assert.Contains("Nota interna", testo);
        Assert.Contains("4200", testo);
    }

    // ---------- PowerPoint ----------

    [Fact]
    public async Task PowerPoint_LeggeTuttiITestiDelleDiapositive()
    {
        var percorso = FileDiProva.Pptx(Percorso("a.pptx"), "Offerta carpenteria", "Condizioni di pagamento");

        var testo = await Estrai(new EstrattoreOfficeOpenXml(), percorso);

        Assert.Contains("Offerta carpenteria", testo);
        Assert.Contains("Condizioni di pagamento", testo);
    }

    [Fact]
    public async Task Office_FileRovinato_DaErrore()
    {
        var percorso = _tmp.CreaFile("rotto.docx", "questo non è un documento Word");

        await Assert.ThrowsAnyAsync<Exception>(() => Estrai(new EstrattoreOfficeOpenXml(), percorso));
    }

    [Theory]
    [InlineData(".docx", true)]
    [InlineData(".xlsx", true)]
    [InlineData(".pptx", true)]
    [InlineData(".doc", false)] // i vecchi formati non si leggono
    [InlineData(".xls", false)]
    [InlineData(".ppt", false)]
    [InlineData(".pdf", false)]
    public void Office_Supporta(string estensione, bool atteso) =>
        Assert.Equal(atteso, new EstrattoreOfficeOpenXml().Supporta(estensione));

    // ---------- PDF ----------

    [Fact]
    public async Task Pdf_LeggeIlTesto_SenzaUsareLOcr()
    {
        var ocr = new FintoOcr();
        var percorso = FileDiProva.Pdf(Percorso("a.pdf"),
            "Fattura numero 12345 del cliente Rossi, importo complessivo milleduecento euro");

        var testo = await Estrai(new EstrattorePdf(ocr), percorso);

        Assert.Contains("Fattura numero 12345", testo);
        Assert.Empty(ocr.Pdf);
    }

    [Fact]
    public async Task Pdf_PiuPagine_LeggeTutte()
    {
        var percorso = FileDiProva.Pdf(Percorso("a.pdf"),
            "Prima pagina con abbastanza testo per non sembrare una scansione",
            "Seconda pagina con abbastanza testo per non sembrare una scansione");

        var testo = await Estrai(new EstrattorePdf(), percorso);

        Assert.Contains("Prima pagina", testo);
        Assert.Contains("Seconda pagina", testo);
    }

    [Fact]
    public async Task Pdf_Scansione_UsaLOcr()
    {
        var ocr = new FintoOcr(testo: "Fattura scansionata numero 777");
        var percorso = FileDiProva.PdfSenzaTesto(Percorso("scan.pdf"), pagine: 2);

        var testo = await Estrai(new EstrattorePdf(ocr), percorso);

        Assert.Equal("Fattura scansionata numero 777", testo);
        Assert.Equal([percorso], ocr.Pdf);
    }

    [Fact]
    public async Task Pdf_Scansione_SenzaOcrDisponibile_ResteInSospeso_ConIlMotivo()
    {
        var percorso = FileDiProva.PdfSenzaTesto(Percorso("scan.pdf"));

        var ex = await Assert.ThrowsAsync<OcrNonDisponibileException>(() => Estrai(new EstrattorePdf(new FintoOcr(disponibile: false)), percorso));

        Assert.Contains("lingua italiana", ex.Message);
    }

    [Fact]
    public async Task Pdf_Scansione_SenzaNessunOcr_ResteInSospeso()
    {
        var percorso = FileDiProva.PdfSenzaTesto(Percorso("scan.pdf"));

        await Assert.ThrowsAsync<OcrNonDisponibileException>(() => Estrai(new EstrattorePdf(), percorso));
    }

    [Fact]
    public async Task Pdf_ConPocoTestoESenzaOcr_IndicizzaQuelloCheC_E()
    {
        var percorso = FileDiProva.Pdf(Percorso("timbro.pdf"), "Protocollo 99");

        // Meno di 15 caratteri utili per pagina farebbe pensare a una scansione, ma senza OCR meglio il poco che c'è.
        var testo = await Estrai(new EstrattorePdf(new FintoOcr(disponibile: false)), percorso);

        Assert.Contains("Protocollo 99", testo);
    }

    [Fact]
    public async Task Pdf_ConPocoTestoEConOcr_SiPreferisceIlRisultatoPiuRicco()
    {
        var percorso = FileDiProva.Pdf(Percorso("timbro.pdf"), "Timbro");

        var ricco = await Estrai(new EstrattorePdf(new FintoOcr(testo: "Contratto di fornitura numero 4521 stipulato tra le parti")), percorso);
        var povero = await Estrai(new EstrattorePdf(new FintoOcr(testo: "")), percorso);

        Assert.Contains("Contratto di fornitura", ricco);
        Assert.Contains("Timbro", povero); // l'OCR non ha trovato di meglio: si tiene il testo del PDF
    }

    [Fact]
    public async Task Pdf_FileRovinato_DaErrore()
    {
        var percorso = _tmp.CreaFile("rotto.pdf", "%PDF-1.4 non proprio un pdf");

        await Assert.ThrowsAnyAsync<Exception>(() => Estrai(new EstrattorePdf(new FintoOcr()), percorso));
    }

    [Fact]
    public async Task Pdf_FileApertoInUnLettore_SiLeggeComunque()
    {
        var percorso = FileDiProva.Pdf(Percorso("a.pdf"), "Documento con abbastanza testo per essere letto correttamente");
        using var aperto = new FileStream(percorso, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Contains("Documento con abbastanza testo", await Estrai(new EstrattorePdf(), percorso));
    }

    // ---------- Immagini ----------

    [Fact]
    public async Task Immagine_UsaLOcr()
    {
        var ocr = new FintoOcr(testo: "FATTURA 12345");
        var percorso = _tmp.CreaFile("foto.jpg", "finta immagine");

        Assert.Equal("FATTURA 12345", await Estrai(new EstrattoreImmagine(ocr), percorso));
        Assert.Equal([percorso], ocr.Immagini);
    }

    [Fact]
    public async Task Immagine_SenzaOcr_ResteInSospeso()
    {
        var percorso = _tmp.CreaFile("foto.jpg", "finta immagine");

        await Assert.ThrowsAsync<OcrNonDisponibileException>(() => Estrai(new EstrattoreImmagine(new FintoOcr(disponibile: false)), percorso));
        await Assert.ThrowsAsync<OcrNonDisponibileException>(() => Estrai(new EstrattoreImmagine(), percorso));
    }

    [Theory]
    [InlineData(".jpg", true)]
    [InlineData(".jpeg", true)]
    [InlineData(".png", true)]
    [InlineData(".bmp", true)]
    [InlineData(".tif", true)]
    [InlineData(".tiff", true)]
    [InlineData(".gif", true)]
    [InlineData(".pdf", false)]
    [InlineData(".docx", false)]
    public void Immagine_Supporta(string estensione, bool atteso) =>
        Assert.Equal(atteso, new EstrattoreImmagine().Supporta(estensione));
}
