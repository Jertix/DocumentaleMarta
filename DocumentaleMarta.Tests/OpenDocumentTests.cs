using System.Diagnostics;
using System.IO.Compression;
using System.Xml;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;
using DocumentaleMarta.Data.Testo;

namespace DocumentaleMarta.Tests;

/// <summary>La lettura del testo dei file di OpenOffice e LibreOffice (.odt, .ods, .odp, .odg e i loro modelli).</summary>
public class EstrattoreOpenDocumentTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();
    private readonly EstrattoreOpenDocument _estrattore = new();

    public void Dispose() => _tmp.Dispose();

    private string Percorso(string nome) => _tmp.Combina(nome);

    private Task<string> Estrai(string percorso) => _estrattore.EstraiAsync(percorso, CancellationToken.None);

    /// <summary>Le righe di testo estratte, una per paragrafo.</summary>
    private static string[] Righe(string testo) =>
        testo.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

    // ---------- Quali file ----------

    [Theory]
    [InlineData(".odt", true)]
    [InlineData(".ott", true)]
    [InlineData(".ods", true)]
    [InlineData(".ots", true)]
    [InlineData(".odp", true)]
    [InlineData(".otp", true)]
    [InlineData(".odg", true)]
    [InlineData(".otg", true)]
    [InlineData(".ODT", true)]
    [InlineData(".doc", false)]
    [InlineData(".docx", false)]
    [InlineData(".sxw", false)] // il vecchio formato di OpenOffice 1.x non è OpenDocument
    [InlineData(".fodt", false)]
    [InlineData(".odf", false)] // formule
    [InlineData(".odb", false)] // database
    [InlineData("", false)]
    public void Supporta_SoloIFormatiOpenDocumentDiTestoCalcoloPresentazioneEDisegno(string estensione, bool atteso) =>
        Assert.Equal(atteso, _estrattore.Supporta(estensione));

    // ---------- Documenti di testo ----------

    [Fact]
    public async Task Odt_ParagrafiLetti_ConAccentiECaratteriSpeciali()
    {
        var percorso = OpenDocumentDiProva.Odt(Percorso("a.odt"),
            ["Preventivo n. 2026/14", "Società Rossi & Figli: cancello <zincato> «scorrevole»", "Totale € 4.200,00"]);

        var righe = Righe(await Estrai(percorso));

        Assert.Equal(["Preventivo n. 2026/14", "Società Rossi & Figli: cancello <zincato> «scorrevole»", "Totale € 4.200,00"], righe);
    }

    [Fact]
    public async Task Odt_IntestazioneEPieDiPagina_SiLeggono_IntestazionePrimaDelCorpo()
    {
        var percorso = OpenDocumentDiProva.Odt(Percorso("a.odt"), ["corpo del testo"],
            intestazione: "METAL PROJET - Fattura 2026/77", pieDiPagina: "Pagamento a 30 giorni");

        var righe = Righe(await Estrai(percorso));

        // Intestazione e piè di pagina stanno in styles.xml, che si legge prima del corpo.
        Assert.Equal(["METAL PROJET - Fattura 2026/77", "Pagamento a 30 giorni", "corpo del testo"], righe);
    }

    [Fact]
    public async Task TitoliSpanELink_SiRicompongonoInUnaRiga()
    {
        var percorso = OpenDocumentDiProva.Documento(Percorso("a.odt"), "text", """
            <text:h text:style-name="Heading_20_1" text:outline-level="1">Contratto di <text:span text:style-name="T1">locazione</text:span></text:h>
            <text:p>Vedi <text:a xlink:href="http://example.com/doc">allegato <text:span text:style-name="T2">B</text:span></text:a> e <text:span>nota</text:span>.</text:p>
            """);

        var righe = Righe(await Estrai(percorso));

        Assert.Equal(["Contratto di locazione", "Vedi allegato B e nota."], righe);
    }

    [Fact]
    public async Task SpaziTabEAccapo_Speciali_SiLeggono()
    {
        var percorso = OpenDocumentDiProva.Documento(Percorso("a.odt"), "text",
            "<text:p>a<text:s text:c=\"3\"/>b<text:tab/>c<text:line-break/>d<text:s/>e</text:p>");

        var testo = (await Estrai(percorso)).Trim();

        Assert.Equal("a   b\tc\nd e", testo);
    }

    [Fact]
    public async Task UnaTabellaDiUnDocumento_SiLeggeCellaPerCella()
    {
        var percorso = OpenDocumentDiProva.Documento(Percorso("a.odt"), "text", """
            <table:table table:name="Tabella1">
              <table:table-row>
                <table:table-cell office:value-type="string"><text:p>Voce</text:p></table:table-cell>
                <table:table-cell office:value-type="string"><text:p>Importo</text:p></table:table-cell>
              </table:table-row>
              <table:table-row>
                <table:table-cell office:value-type="string"><text:p>Cancello</text:p></table:table-cell>
                <table:table-cell office:value-type="float" office:value="4200"><text:p>4.200,00</text:p></table:table-cell>
              </table:table-row>
            </table:table>
            """);

        var righe = Righe(await Estrai(percorso));

        Assert.Equal(["Tabella1", "Voce", "Importo", "Cancello", "4.200,00"], righe);
    }

    [Fact]
    public async Task NoteACornicieDentroUnParagrafo_SiLeggono_EIlParagrafoRestaIntero()
    {
        var percorso = OpenDocumentDiProva.Documento(Percorso("a.odt"), "text", """
            <text:p>Il cancello<text:note text:id="ftn1" text:note-class="footnote"><text:note-citation>1</text:note-citation>
              <text:note-body><text:p text:style-name="Footnote">Zincatura a caldo secondo UNI EN ISO 1461</text:p></text:note-body></text:note> è pronto.</text:p>
            <text:p>Schema:<draw:frame draw:name="Cornice1"><draw:text-box><text:p>Disegno quotato</text:p></draw:text-box></draw:frame></text:p>
            """);

        var testo = await Estrai(percorso);

        Assert.Contains("Zincatura a caldo secondo UNI EN ISO 1461", testo);
        Assert.Contains("Disegno quotato", testo);
        Assert.Contains("Il cancello", testo);
        Assert.Contains("è pronto.", testo);
        Assert.Contains("Schema:", testo);
    }

    [Fact]
    public async Task UnDocumentoVuoto_DaTestoVuoto()
    {
        var percorso = OpenDocumentDiProva.Documento(Percorso("vuoto.odt"), "text", "<text:p/><text:p>   </text:p>");

        Assert.Equal("", (await Estrai(percorso)).Trim());
    }

    [Fact]
    public async Task IModelli_SiLeggonoComeIDocumenti()
    {
        var percorso = OpenDocumentDiProva.Odt(Percorso("modello.ott"), ["Spett.le cliente"]);

        Assert.Equal(["Spett.le cliente"], Righe(await Estrai(percorso)));
    }

    // ---------- Fogli di calcolo, presentazioni, disegni ----------

    [Fact]
    public async Task Ods_NomiDeiFogliETestoDelleCelle_ConNumeriComeSonoMostrati()
    {
        var percorso = OpenDocumentDiProva.Documento(Percorso("a.ods"), "spreadsheet", """
            <table:table table:name="Fatture 2026">
              <table:table-row>
                <table:table-cell office:value-type="string"><text:p>Cliente Bianchi</text:p></table:table-cell>
                <table:table-cell office:value-type="float" office:value="4200"><text:p>4.200,00</text:p></table:table-cell>
                <table:table-cell table:number-columns-repeated="1000"/>
              </table:table-row>
              <table:table-row table:number-rows-repeated="1048000"><table:table-cell table:number-columns-repeated="1024"/></table:table-row>
            </table:table>
            <table:table table:name="Note"><table:table-row>
              <table:table-cell office:value-type="string"><text:p>Nota interna</text:p></table:table-cell>
            </table:table-row></table:table>
            """);

        Assert.Equal(["Fatture 2026", "Cliente Bianchi", "4.200,00", "Note", "Nota interna"], Righe(await Estrai(percorso)));
    }

    [Fact]
    public async Task Ods_ConUnFoglioMoltoGrande_SiLeggeInFrettaSenzaCaricareTuttoInMemoria()
    {
        var corpo = new System.Text.StringBuilder("<table:table table:name=\"Grande\">");
        for (var riga = 0; riga < 30_000; riga++)
            corpo.Append("<table:table-row><table:table-cell office:value-type=\"string\"><text:p>Riga numero ")
                .Append(riga).Append("</text:p></table:table-cell><table:table-cell office:value-type=\"float\" office:value=\"")
                .Append(riga).Append("\"><text:p>").Append(riga).Append("</text:p></table:table-cell></table:table-row>");
        corpo.Append("</table:table>");
        var percorso = OpenDocumentDiProva.Documento(Percorso("grande.ods"), "spreadsheet", corpo.ToString());

        var cronometro = Stopwatch.StartNew();
        var testo = await Estrai(percorso);
        cronometro.Stop();

        Assert.Contains("Riga numero 29999", testo);
        Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(10), $"troppo lento: {cronometro.Elapsed}");
    }

    [Fact]
    public async Task Odp_TestoDelleDiapositive_ENoteDelRelatore()
    {
        var percorso = OpenDocumentDiProva.Documento(Percorso("a.odp"), "presentation", """
            <draw:page draw:name="page1"><draw:frame><draw:text-box><text:p>Offerta carpenteria</text:p></draw:text-box></draw:frame>
              <presentation:notes xmlns:presentation="urn:oasis:names:tc:opendocument:xmlns:presentation:1.0"><draw:frame><draw:text-box><text:p>Ricordare lo sconto</text:p></draw:text-box></draw:frame></presentation:notes>
            </draw:page>
            <draw:page draw:name="page2"><draw:frame><draw:text-box><text:p>Condizioni di pagamento</text:p></draw:text-box></draw:frame></draw:page>
            """);

        Assert.Equal(["Offerta carpenteria", "Ricordare lo sconto", "Condizioni di pagamento"], Righe(await Estrai(percorso)));
    }

    [Fact]
    public async Task Odp_CostruitaConLAiutante_SiLegge() =>
        Assert.Equal(["Prima", "Seconda"], Righe(await Estrai(OpenDocumentDiProva.Odp(Percorso("b.odp"), "Prima", "Seconda"))));

    [Fact]
    public async Task Odg_TestoDentroLeForme()
    {
        var percorso = OpenDocumentDiProva.Documento(Percorso("a.odg"), "drawing", """
            <draw:page draw:name="Pagina1">
              <draw:custom-shape draw:style-name="gr1"><text:p>Gruppo saldatura</text:p></draw:custom-shape>
              <draw:rect draw:style-name="gr2"><text:p>Flangia 120x120</text:p></draw:rect>
            </draw:page>
            """);

        Assert.Equal(["Gruppo saldatura", "Flangia 120x120"], Righe(await Estrai(percorso)));
    }

    // ---------- File non leggibili o pericolosi ----------

    [Fact]
    public async Task UnFileCheNonEUnoZip_LanciaUnErrore()
    {
        var percorso = _tmp.CreaFile("rotto.odt", "questo non è un documento OpenDocument");

        await Assert.ThrowsAsync<InvalidDataException>(() => Estrai(percorso));
    }

    [Fact]
    public async Task UnoZipSenzaContentXml_LanciaUnErrore()
    {
        var percorso = OpenDocumentDiProva.Documento(Percorso("vuoto.odt"), "text", "", senzaContenuto: true);

        var errore = await Assert.ThrowsAsync<InvalidDataException>(() => Estrai(percorso));

        Assert.Contains("content.xml", errore.Message);
    }

    [Fact]
    public async Task UnDocumentoProtettoDaPassword_NonHaTestoDaLeggere_NonLanciaErrori()
    {
        var percorso = OpenDocumentDiProva.Documento(Percorso("segreto.odt"), "text", "<text:p>contenuto cifrato</text:p>", cifrato: true);

        Assert.Equal("", await Estrai(percorso));
    }

    [Fact]
    public async Task UnXmlConEntitaEsterne_SiRifiuta_SenzaLeggereIFileDelPc()
    {
        var segreto = _tmp.CreaFile("segreto.txt", "PAROLA-RISERVATA-DEL-PC");
        var percorso = Percorso("attacco.odt");
        using (var flusso = new FileStream(percorso, FileMode.Create))
        using (var zip = new ZipArchive(flusso, ZipArchiveMode.Create))
        using (var scrittore = new StreamWriter(zip.CreateEntry("content.xml").Open()))
            scrittore.Write(
                $"<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///{segreto.Replace('\\', '/')}\">]>"
                + "<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" "
                + "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\"><office:body><office:text><text:p>&e;</text:p></office:text></office:body></office:document-content>");

        await Assert.ThrowsAsync<XmlException>(() => Estrai(percorso));
    }

    [Fact]
    public async Task UnFileAperto_InLibreOffice_SiLegge()
    {
        var percorso = OpenDocumentDiProva.Odt(Percorso("aperto.odt"), ["ancora leggibile"]);
        using var altroProgramma = new FileStream(percorso, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal(["ancora leggibile"], Righe(await Estrai(percorso)));
    }

    [Fact]
    public async Task Annullando_SiInterrompe()
    {
        var percorso = OpenDocumentDiProva.Odt(Percorso("a.odt"), ["x"]);
        using var annullamento = new CancellationTokenSource();
        await annullamento.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _estrattore.EstraiAsync(percorso, annullamento.Token));
    }

    // ---------- Categorie della ricerca avanzata ----------

    [Theory]
    [InlineData(".odt", CategoriaFile.Word)]
    [InlineData(".ott", CategoriaFile.Word)]
    [InlineData(".ods", CategoriaFile.Excel)]
    [InlineData(".ots", CategoriaFile.Excel)]
    [InlineData(".odp", CategoriaFile.Altro)]
    [InlineData(".otp", CategoriaFile.Altro)]
    [InlineData(".odg", CategoriaFile.Altro)]
    [InlineData(".ODT", CategoriaFile.Word)]
    public void LeCategorie_PongonoIFormatiOpenDocumentConIlLoroEquivalente(string estensione, CategoriaFile atteso) =>
        Assert.Equal(atteso, CategorieFile.Di(estensione));

    [Fact]
    public void LaListaDeiFormati_EQuellaDiTutti()
    {
        Assert.Equal(8, FormatiOpenDocument.Tutti.Count);
        Assert.All(FormatiOpenDocument.Tutti, e => Assert.True(_estrattore.Supporta(e)));
    }
}

/// <summary>Dalla lettura del file alla ricerca: i documenti OpenOffice si trovano per il loro contenuto.</summary>
public class OpenDocumentIndicizzazioneTests : IDisposable
{
    private readonly ArchivioDiProva _a = new(new EstrattoreOpenDocument());

    public void Dispose() => _a.Dispose();

    private async Task<int> AllegaAsync(string nome, Func<string, string> crea)
    {
        var areaId = (await _a.Servizio.CaricaAlberoAsync()).FirstOrDefault()?.Id ?? await _a.Servizio.CreaAreaAsync("Fatture");
        var sorgente = crea(_a.Tmp.Combina(Path.Combine("src", nome)));
        var cartella = await _a.Servizio.CreaCartellaConDatiAsync(areaId, new DatiCartella(nome, null, null, false, null), [sorgente]);
        return cartella.Documenti.Single().Id;
    }

    [Fact]
    public async Task UnDocumentoOdt_SiTrovaPerIlSuoContenuto_ELoStatoEIndicizzato()
    {
        Directory.CreateDirectory(_a.Tmp.Combina("src"));
        var id = await AllegaAsync("preventivo.odt", p => OpenDocumentDiProva.Odt(p, ["Preventivo carpenteria zincata per il cancello Rossi"]));
        await _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));

        var trovati = await _a.Ricerca.CercaAsync("zincata");

        Assert.Equal(StatoIndicizzazione.Indicizzato, _a.StatoDi(id));
        Assert.Equal(["preventivo.odt"], trovati.Risultati.Select(r => r.Documento.NomeFile));
    }

    [Fact]
    public async Task FogliEPresentazioni_SiTrovanoPerIlContenuto()
    {
        Directory.CreateDirectory(_a.Tmp.Combina("src"));
        await AllegaAsync("conti.ods", p => OpenDocumentDiProva.Ods(p, ("Bilancio", ["Acquisto lamiere inox"])));
        await AllegaAsync("offerta.odp", p => OpenDocumentDiProva.Odp(p, "Ringhiere su misura"));
        await _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(["conti.ods"], (await _a.Ricerca.CercaAsync("lamiere")).Risultati.Select(r => r.Documento.NomeFile));
        Assert.Equal(["conti.ods"], (await _a.Ricerca.CercaAsync("Bilancio")).Risultati.Select(r => r.Documento.NomeFile)); // anche il nome del foglio
        Assert.Equal(["offerta.odp"], (await _a.Ricerca.CercaAsync("ringhiere")).Risultati.Select(r => r.Documento.NomeFile));
    }

    [Fact]
    public async Task ILFiltroPerTipo_TrovaIDocumentiOpenOfficeComeWordEExcel()
    {
        Directory.CreateDirectory(_a.Tmp.Combina("src"));
        await AllegaAsync("lettera.odt", p => OpenDocumentDiProva.Odt(p, ["comune parola"]));
        await AllegaAsync("conti.ods", p => OpenDocumentDiProva.Ods(p, ("F", ["comune parola"])));
        await AllegaAsync("slide.odp", p => OpenDocumentDiProva.Odp(p, "comune parola"));
        await _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(["lettera.odt"], (await _a.Ricerca.CercaAsync("comune", new FiltriRicerca(Categorie: CategoriaFile.Word))).Risultati.Select(r => r.Documento.NomeFile));
        Assert.Equal(["conti.ods"], (await _a.Ricerca.CercaAsync("comune", new FiltriRicerca(Categorie: CategoriaFile.Excel))).Risultati.Select(r => r.Documento.NomeFile));
        Assert.Equal(["slide.odp"], (await _a.Ricerca.CercaAsync("comune", new FiltriRicerca(Categorie: CategoriaFile.Altro))).Risultati.Select(r => r.Documento.NomeFile));
    }

    [Fact]
    public async Task UnOdtRovinato_AvevaStatoErrore_ENonFermaGliAltri()
    {
        Directory.CreateDirectory(_a.Tmp.Combina("src"));
        var rotto = await AllegaAsync("rotto.odt", p => { File.WriteAllText(p, "non è uno zip"); return p; });
        var buono = await AllegaAsync("buono.odt", p => OpenDocumentDiProva.Odt(p, ["testo leggibile"]));
        await _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(StatoIndicizzazione.Errore, _a.StatoDi(rotto));
        Assert.Equal(StatoIndicizzazione.Indicizzato, _a.StatoDi(buono));
    }
}

/// <summary>I documenti archiviati prima che esistesse il lettore (marcati "non supportati") si leggono al riavvio.</summary>
public class OpenDocumentDocumentiGiaArchiviatiTests : IDisposable
{
    // All'inizio c'è solo un lettore di testo semplice: i file OpenOffice finiscono "non supportati".
    private readonly ArchivioDiProva _a = new(new EstrattoreTestoSemplice());

    public void Dispose() => _a.Dispose();

    [Fact]
    public async Task AlRiavvio_ConIlNuovoLettore_SiRileggonoISoliFormatiOraSupportati()
    {
        Directory.CreateDirectory(_a.Tmp.Combina("src"));
        var areaId = await _a.Servizio.CreaAreaAsync("Fatture");
        var odt = OpenDocumentDiProva.Odt(_a.Tmp.Combina("src/vecchio.odt"), ["Contratto di fornitura acciaio"]);
        var sconosciuto = _a.Tmp.CreaFile("src/strano.xyz", "contenuto");
        var cartella = await _a.Servizio.CreaCartellaConDatiAsync(
            areaId, new DatiCartella("Vecchi", null, null, false, null), [odt, sconosciuto]);
        await _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var idOdt = cartella.Documenti.Single(d => d.NomeFile == "vecchio.odt").Id;
        var idXyz = cartella.Documenti.Single(d => d.NomeFile == "strano.xyz").Id;
        Assert.Equal(StatoIndicizzazione.NonSupportato, _a.StatoDi(idOdt));
        Assert.Empty((await _a.Ricerca.CercaAsync("fornitura")).Risultati);

        // L'app si riavvia con la versione che legge anche OpenDocument.
        using var nuovo = new IndicizzazioneService(_a.Factory, _a.Files, [new EstrattoreTestoSemplice(), new EstrattoreOpenDocument()]);
        nuovo.Avvia();
        await nuovo.AccodaPendentiAsync();
        await nuovo.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(StatoIndicizzazione.Indicizzato, _a.StatoDi(idOdt));
        Assert.Equal(StatoIndicizzazione.NonSupportato, _a.StatoDi(idXyz)); // per lui non c'è ancora un lettore
        Assert.Equal(["vecchio.odt"], (await _a.Ricerca.CercaAsync("fornitura")).Risultati.Select(r => r.Documento.NomeFile));
    }

    [Fact]
    public async Task SeNonCambiaNulla_UnFormatoNonSupportatoNonSiRiaccodaOgniVolta()
    {
        Directory.CreateDirectory(_a.Tmp.Combina("src"));
        var areaId = await _a.Servizio.CreaAreaAsync("Fatture");
        await _a.Servizio.CreaCartellaConDatiAsync(
            areaId, new DatiCartella("Strani", null, null, false, null), [_a.Tmp.CreaFile("src/strano.xyz", "contenuto")]);
        await _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));

        await _a.Indicizzazione.AccodaPendentiAsync();

        Assert.Equal(0, _a.Indicizzazione.Stato.InCoda);
    }
}

/// <summary>File creati da LibreOffice vero (cartella FileVeri), non costruiti a mano: il lettore deve capirli come sono.</summary>
[Collection("WPF")]
public class OpenDocumentFileVeriTests : IDisposable
{
    private readonly ArchivioDiProva _a = new(new EstrattoreOpenDocument());

    public void Dispose() => _a.Dispose();

    private static string FileVero(string nome)
    {
        var percorso = Path.Combine(AppContext.BaseDirectory, "FileVeri", nome);
        Assert.True(File.Exists(percorso), $"manca il file di prova {percorso}");
        return percorso;
    }

    private static Task<string> Estrai(string nome) =>
        new EstrattoreOpenDocument().EstraiAsync(FileVero(nome), CancellationToken.None);

    private static IEnumerable<T> Tutti<T>(System.Windows.DependencyObject? radice) where T : System.Windows.DependencyObject
    {
        if (radice is null)
            yield break;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(radice); i++)
        {
            var figlio = System.Windows.Media.VisualTreeHelper.GetChild(radice, i);
            if (figlio is T trovato)
                yield return trovato;
            foreach (var discendente in Tutti<T>(figlio))
                yield return discendente;
        }
    }

    [Fact]
    public async Task UnOdtDiLibreOffice_SiLegge_ConTitoliTabelleEStili()
    {
        var testo = await Estrai("preventivo.odt");

        Assert.Contains("Preventivo n. 2026/14", testo);
        Assert.Contains("Società Rossi & Figli", testo); // accenti e & come si vedono nel documento
        Assert.Contains("cancello scorrevole in acciaio zincato", testo); // il grassetto e il corsivo non spezzano le parole
        Assert.Contains("Voce", testo);
        Assert.Contains("Cancello", testo);
        Assert.Contains("4200", testo);
        Assert.Contains("Consegna entro trenta giorni.", testo);
    }

    [Fact]
    public async Task UnOdsDiLibreOffice_SiLegge_NomeDelFoglioETestoDelleCelle()
    {
        var testo = await Estrai("conti.ods");

        Assert.Contains("conti", testo); // il nome del foglio
        Assert.Contains("Cliente", testo);
        Assert.Contains("Bianchi Carpenteria", testo);
        Assert.Contains("Verdi Ringhiere", testo);
        Assert.Contains("4200", testo);
        Assert.Contains("1800", testo);
    }

    [Fact]
    public async Task UnOdpDiLibreOffice_SiLegge_TestoDiTutteLeDiapositive()
    {
        var testo = await Estrai("offerta.odp");

        Assert.Contains("Offerta carpenteria metallica", testo);
        Assert.Contains("Condizioni di pagamento", testo);
    }

    [Theory]
    [InlineData("preventivo.odt")]
    [InlineData("conti.ods")]
    [InlineData("offerta.odp")]
    public async Task LAnteprima_DeiFileDiLibreOffice_ELaMiniaturaDellaPrimaPagina(string nome)
    {
        var risultato = await Task.Run(() => new GeneratoreAnteprima().GeneraAsync(FileVero(nome), 0, CancellationToken.None));

        Assert.Equal(StatoAnteprima.Pronta, risultato.Stato);
        var immagine = Assert.IsAssignableFrom<System.Windows.Media.Imaging.BitmapSource>(risultato.Immagine);
        Assert.True(immagine.PixelWidth > 50 && immagine.PixelHeight > 50);
    }

    [Fact]
    public async Task FinestraPrincipale_ConUnOdtSelezionato_MostraLaMiniaturaNelPannello()
    {
        Directory.CreateDirectory(_a.Tmp.Combina("src"));
        var copia = _a.Tmp.Combina("src", "preventivo.odt");
        File.Copy(FileVero("preventivo.odt"), copia);
        var areaId = await _a.Servizio.CreaAreaAsync("Preventivi");
        await _a.Servizio.CreaCartellaConDatiAsync(areaId, new DatiCartella("Cancello Rossi", null, null, false, null), [copia]);
        var vm = new MainViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell,
            new DocumentaleMarta.Core.Impostazioni.ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false },
            generatoreAnteprima: new GeneratoreAnteprima());
        vm.Anteprima!.Ritardo = TimeSpan.Zero;
        await vm.InizializzaAsync();
        vm.Radici.Single().Aree.Single().Figli.Single().IsSelected = true;
        await vm.CaricamentoFormCompletato;
        vm.FormCartella!.DocumentoSelezionato = vm.FormCartella.Documenti.Single();
        await vm.Anteprima.Completamento;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new DocumentaleMarta.App.Viste.MainWindow(vm);
            var contenuto = (System.Windows.FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1180, 680, "finestra-anteprima-odt");

            var nota = Tutti<System.Windows.Controls.TextBlock>(contenuto).Single(t => t.Text == GeneratoreAnteprima.NotaMiniatura);
            Assert.Equal(System.Windows.Visibility.Visible, nota.Visibility);
        });

        Assert.Empty(errori);
        Assert.Equal(StatoAnteprima.Pronta, vm.Anteprima.Stato);
        Assert.Equal("preventivo.odt", vm.Anteprima.Titolo);
        Assert.Equal(GeneratoreAnteprima.NotaMiniatura, vm.Anteprima.Nota);
    }

    [Fact]
    public async Task ADocumentiDiLibreOffice_SiTrovanoNellaRicerca_ConIFiltriPerTipo()
    {
        Directory.CreateDirectory(_a.Tmp.Combina("src"));
        var sorgenti = new[] { "preventivo.odt", "conti.ods", "offerta.odp" }
            .Select(n => { var d = _a.Tmp.Combina("src", n); File.Copy(FileVero(n), d); return d; }).ToList();
        var areaId = await _a.Servizio.CreaAreaAsync("Fatture");
        await _a.Servizio.CreaCartellaConDatiAsync(areaId, new DatiCartella("Documenti", null, null, false, null), sorgenti);
        await _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(["preventivo.odt"], (await _a.Ricerca.CercaAsync("scorrevole")).Risultati.Select(r => r.Documento.NomeFile));
        Assert.Equal(["conti.ods"], (await _a.Ricerca.CercaAsync("ringhiere")).Risultati.Select(r => r.Documento.NomeFile));
        Assert.Equal(["offerta.odp"], (await _a.Ricerca.CercaAsync("pagamento")).Risultati.Select(r => r.Documento.NomeFile));
        Assert.Equal(["preventivo.odt"],
            (await _a.Ricerca.CercaAsync("", new FiltriRicerca(Categorie: CategoriaFile.Word))).Risultati.Select(r => r.Documento.NomeFile));
    }
}
