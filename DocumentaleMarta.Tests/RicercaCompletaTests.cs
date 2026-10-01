using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;
using DocumentaleMarta.Data.Testo;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentaleMarta.Tests;

public class SegmentiEvidenziatiTests
{
    private const char I = RisultatoRicerca.InizioEvidenza;
    private const char F = RisultatoRicerca.FineEvidenza;

    [Fact]
    public void TestoSenzaEvidenze_UnSoloPezzoNormale() =>
        Assert.Equal([("solo testo", false)], SegmentiEvidenziati.Dividi("solo testo"));

    [Fact]
    public void UnaParolaEvidenziata_InMezzo() =>
        Assert.Equal([("cancello ", false), ("zincato", true), (" in acciaio", false)],
            SegmentiEvidenziati.Dividi($"cancello {I}zincato{F} in acciaio"));

    [Fact]
    public void PiuParole_EEvidenzaAllInizioEAllaFine() =>
        Assert.Equal([("uno", true), (" e ", false), ("due", true)],
            SegmentiEvidenziati.Dividi($"{I}uno{F} e {I}due{F}"));

    [Fact]
    public void PuntiDiSospensioneDellEstratto_RestanoNelTestoNormale() =>
        Assert.Equal([("… il ", false), ("preventivo", true), (" per …", false)],
            SegmentiEvidenziati.Dividi($"… il {I}preventivo{F} per …"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Vuoto_NessunPezzo(string? testo) =>
        Assert.Empty(SegmentiEvidenziati.Dividi(testo));

    [Fact]
    public void EvidenzaNonChiusa_SiEvidenziaFinoAllaFine() =>
        Assert.Equal([("a ", false), ("bcd", true)], SegmentiEvidenziati.Dividi($"a {I}bcd"));

    [Fact]
    public void FineSenzaInizio_NonEvidenzia_ENonCreaPezziVuoti() =>
        Assert.Equal([("a", false), ("b", false)], SegmentiEvidenziati.Dividi($"a{F}b"));
}

/// <summary>La catena intera con i componenti veri: allegare un file, leggerlo in background, cercarlo.</summary>
public class RicercaCompletaTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private string Sorgente(string nome) => _tmp.Combina("sorgenti", nome);

    private async Task<(ArchivioDiProva Archivio, int CartellaId)> ConFileAsync(IOcr? ocr, Action<string> creaFile, string[] nomi)
    {
        // Gli estrattori veri, ma con l'OCR indicato (quello vero di Windows, uno finto o nessuno).
        var archivio = new ArchivioDiProva(
            new EstrattoreTestoSemplice(), new EstrattoreOfficeOpenXml(), new EstrattorePdf(ocr), new EstrattoreImmagine(ocr));
        Directory.CreateDirectory(_tmp.Combina("sorgenti"));
        foreach (var nome in nomi)
            creaFile(nome);

        var area = await archivio.Servizio.CreaAreaAsync("Archivio");
        var cartella = await archivio.Servizio.CreaCartellaConDatiAsync(
            area, new DatiCartella("Pratiche", null, null, false, null), nomi.Select(Sorgente).ToList());
        await archivio.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(60));
        return (archivio, cartella.Id);
    }

    [Fact]
    public async Task FileVeri_PdfWordExcelPowerPointETesto_SiTrovanoDalContenuto()
    {
        var (a, _) = await ConFileAsync(null, nome =>
        {
            switch (Path.GetExtension(nome))
            {
                case ".pdf": FileDiProva.Pdf(Sorgente(nome), "Relazione tecnica sulla saldatura delle travi in acciaio strutturale"); break;
                case ".docx": FileDiProva.Docx(Sorgente(nome), ["Offerta per la fornitura di ringhiere zincate a caldo"]); break;
                case ".xlsx": FileDiProva.Xlsx(Sorgente(nome), "Listino", "Lamiera striata spessore tre millimetri", "Nota", "120"); break;
                case ".pptx": FileDiProva.Pptx(Sorgente(nome), "Presentazione della carpenteria pesante"); break;
                default: File.WriteAllText(Sorgente(nome), "Appunti sul sopralluogo in cantiere del martedì"); break;
            }
        }, ["relazione.pdf", "offerta.docx", "listino.xlsx", "slide.pptx", "appunti.txt"]);
        using var _ = a;

        async Task<string[]> Cerca(string t) =>
            (await a.Ricerca.CercaAsync(t)).Risultati.Select(r => r.Documento.NomeFile).ToArray();

        Assert.Equal(["relazione.pdf"], await Cerca("saldatura"));
        Assert.Equal(["offerta.docx"], await Cerca("ringhiere"));
        Assert.Equal(["listino.xlsx"], await Cerca("lamiera"));
        Assert.Equal(["slide.pptx"], await Cerca("carpenteria"));
        Assert.Equal(["appunti.txt"], await Cerca("martedi")); // senza accento trova "martedì"
        Assert.Equal(5, (await Cerca("pratiche")).Length);      // dal titolo della cartella
    }

    [Fact]
    public async Task UnFileRovinatoNonFermaGliAltri_ERestaTrovabileDalNome()
    {
        var (a, _) = await ConFileAsync(null, nome =>
        {
            if (nome == "rotto.pdf")
                File.WriteAllText(Sorgente(nome), "%PDF-1.4 non proprio un pdf");
            else
                File.WriteAllText(Sorgente(nome), "Verbale della riunione di giovedì");
        }, ["rotto.pdf", "verbale.txt"]);
        using var _ = a;

        var rotto = (await a.Servizio.CaricaDocumentiAsync(null)).Single(d => d.NomeFile == "rotto.pdf");
        Assert.Equal(StatoIndicizzazione.Errore, a.StatoDi(rotto.Id));
        Assert.Equal(["verbale.txt"], (await a.Ricerca.CercaAsync("riunione")).Risultati.Select(r => r.Documento.NomeFile));
        Assert.Equal(["rotto.pdf"], (await a.Ricerca.CercaAsync("rotto")).Risultati.Select(r => r.Documento.NomeFile));
    }

    [Fact]
    public async Task ScansioneSenzaOcr_ResteInSospeso_EConLOcrSiSblocca()
    {
        // 1. Senza OCR: il PDF scansionato resta "da leggere".
        var (a, _) = await ConFileAsync(new FintoOcr(disponibile: false),
            nome => FileDiProva.PdfSenzaTesto(Sorgente(nome)), ["scansione.pdf"]);
        using var _ = a;
        var id = (await a.Servizio.CaricaDocumentiAsync(null)).Single().Id;

        Assert.Equal(StatoIndicizzazione.DaIndicizzare, a.StatoDi(id));
        Assert.Equal(1, a.Indicizzazione!.Stato.InAttesaOcr);
        Assert.Empty((await a.Ricerca.CercaAsync("fattura")).Risultati);

        // 2. "Al prossimo avvio" l'OCR c'è: il documento si sblocca e si trova dal suo contenuto.
        using var conOcr = new IndicizzazioneService(
            a.Factory, a.Files, [new EstrattorePdf(new FintoOcr(testo: "Fattura scansionata numero 777"))]);
        conOcr.Avvia();
        await conOcr.AccodaPendentiAsync();
        await conOcr.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(StatoIndicizzazione.Indicizzato, a.StatoDi(id));
        Assert.Equal(["scansione.pdf"], (await a.Ricerca.CercaAsync("fattura 777")).Risultati.Select(r => r.Documento.NomeFile));
    }

    [OcrFact]
    public async Task ConLOcrVero_UnaFotoEUnPdfScansionato_SiTrovanoDalTestoCheContengono()
    {
        var ocr = new OcrWindows();
        var (a, _) = await ConFileAsync(ocr, nome =>
        {
            var immagine = DisegnaTesto(nome == "foto.png" ? "CONTRATTO NUMERO 48291" : "BOLLETTA NUMERO 73650");
            if (nome == "foto.png")
                File.WriteAllBytes(Sorgente(nome), immagine.Png);
            else
            {
                var costruttore = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
                var pagina = costruttore.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
                pagina.AddJpeg(immagine.Jpeg, new UglyToad.PdfPig.Core.PdfRectangle(20, 280, 575, 820));
                File.WriteAllBytes(Sorgente(nome), costruttore.Build());
            }
        }, ["foto.png", "scansione.pdf"]);
        using var _ = a;

        Assert.Equal(["foto.png"], (await a.Ricerca.CercaAsync("48291")).Risultati.Select(r => r.Documento.NomeFile));
        Assert.Equal(["scansione.pdf"], (await a.Ricerca.CercaAsync("73650")).Risultati.Select(r => r.Documento.NomeFile));
        Assert.Equal(2, (await a.Ricerca.CercaAsync("numero")).Risultati.Count);
    }

    private static (byte[] Png, byte[] Jpeg) DisegnaTesto(string testo)
    {
        (byte[], byte[])? risultato = null;
        var thread = new Thread(() =>
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 1400, 1000));
                dc.DrawText(new FormattedText(testo, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    new Typeface("Arial"), 64, Brushes.Black, 1.0), new Point(40, 60));
            }
            var bitmap = new RenderTargetBitmap(1400, 1000, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            byte[] Codifica(BitmapEncoder encoder)
            {
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var memoria = new MemoryStream();
                encoder.Save(memoria);
                return memoria.ToArray();
            }
            risultato = (Codifica(new PngBitmapEncoder()), Codifica(new JpegBitmapEncoder { QualityLevel = 90 }));
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return risultato!.Value;
    }
}

/// <summary>Il collegamento dei servizi dell'app: se manca un pezzo, qui lo si scopre invece che all'avvio.</summary>
public class ComposizioneTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public async Task IServiziSiCostruiscono_ELeTreInterfacceDellIndicizzazioneSonoLoStessoOggetto()
    {
        var radice = _tmp.Combina("Documentale");
        var impostazioni = new ImpostazioniApp { PercorsoRadice = radice };
        using var servizi = Composizione.Crea(impostazioni, ArchivioDatabase.Inizializza(radice));

        var indicizzazione = servizi.GetRequiredService<IndicizzazioneService>();
        Assert.Same(indicizzazione, servizi.GetRequiredService<IIndicizzatore>());
        Assert.Same(indicizzazione, servizi.GetRequiredService<IMonitorIndicizzazione>());
        var estrattori = servizi.GetServices<IEstrattoreTesto>().ToList();
        Assert.Equal(5, estrattori.Count);
        // Ogni formato di OpenOffice e LibreOffice ha un lettore tra quelli collegati.
        Assert.All(FormatiOpenDocument.Tutti, estensione => Assert.Contains(estrattori, e => e.Supporta(estensione)));
        Assert.NotNull(servizi.GetRequiredService<IRicercaService>());
        Assert.NotNull(servizi.GetRequiredService<IOcr>());

        var vm = servizi.GetRequiredService<MainViewModel>();
        Assert.True(vm.RicercaDisponibile);

        // E funziona davvero: un documento allegato si legge e si trova.
        indicizzazione.Avvia();
        var archivio = servizi.GetRequiredService<IArchivioService>();
        var area = await archivio.CreaAreaAsync("A");
        await archivio.CreaCartellaConDatiAsync(area, new DatiCartella("C", null, null, false, null),
            [_tmp.CreaFile("s/nota.txt", "Consegna dei pannelli entro venerdì")]);
        await indicizzazione.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(30));

        var esito = await servizi.GetRequiredService<IRicercaService>().CercaAsync("pannelli venerdi");
        Assert.Equal(["nota.txt"], esito.Risultati.Select(r => r.Documento.NomeFile));
    }
}
