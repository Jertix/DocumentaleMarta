using System.Text;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Data;
using DocumentaleMarta.Data.Testo;

namespace DocumentaleMarta.Tests;

/// <summary>La lettura del testo dei file XML, per la ricerca.</summary>
public class EstrattoreXmlTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();
    private readonly EstrattoreXml _estrattore = new();

    public void Dispose() => _tmp.Dispose();

    private Task<string> Estrai(string percorso) => _estrattore.EstraiAsync(percorso, CancellationToken.None);

    [Theory]
    [InlineData(".xml", true)]
    [InlineData(".txt", false)]
    [InlineData(".xlsx", false)]
    [InlineData("", false)]
    public void Supporta_SoloXml(string estensione, bool atteso) => Assert.Equal(atteso, _estrattore.Supporta(estensione));

    [Fact]
    public async Task SiLeggonoIValori_DegliElementiEDegliAttributi_NonINomiDeiTag()
    {
        var percorso = _tmp.CreaFile("fattura.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<fattura numero=\"A12\"><cliente nome=\"Rossi\">Bulloni M8</cliente></fattura>");

        var testo = await Estrai(percorso);

        Assert.Contains("A12", testo);
        Assert.Contains("Rossi", testo);
        Assert.Contains("Bulloni M8", testo);
        Assert.DoesNotContain("fattura", testo);
        Assert.DoesNotContain("cliente", testo);
        Assert.DoesNotContain("<", testo);
    }

    [Fact]
    public async Task UnBloccoCdata_SiLegge_ComeUnTesto()
    {
        // Come il file di log che ha fatto scoprire la mancanza: il testo sta dentro un blocco CDATA.
        var percorso = _tmp.CreaFile("synclog.xml",
            "<LogEntries><SyncSession><LogEntry type=\"64\"><![CDATA[Calling Pre-Sync -]]></LogEntry>"
            + "<LogEntry type=\"64\"><![CDATA[&lt;Pre-sync site_id=&#34;261527941&#34; project_guid=&#34;5456&#34;]]></LogEntry></SyncSession></LogEntries>");

        var testo = await Estrai(percorso);

        Assert.Contains("Pre-sync site_id", testo);
        Assert.Contains("261527941", testo);
    }

    [Fact]
    public async Task LeEntitaSiDecodificano()
    {
        var percorso = _tmp.CreaFile("a.xml", "<a>Rossi &amp; Figli, perch&#233;</a>");

        Assert.Equal("Rossi & Figli, perché", await Estrai(percorso));
    }

    [Fact]
    public async Task IValoriSiSeparanoConUnoSpazio_ESiSaltanoIVuoti()
    {
        var percorso = _tmp.CreaFile("a.xml", "<a>\n  <b>uno</b>\n  <c/>\n  <d>due</d>\n</a>");

        Assert.Equal("uno due", await Estrai(percorso));
    }

    [Fact]
    public async Task GliIndirizziDeiNamespace_NonSonoTesto()
    {
        var percorso = _tmp.CreaFile("a.xml",
            "<p:a xmlns:p=\"http://www.fatturapa.gov.it/sdi/fatturapa/v1.2\" xmlns=\"urn:altro\"><p:b>valore</p:b></p:a>");

        var testo = await Estrai(percorso);

        Assert.Equal("valore", testo);
    }

    [Fact]
    public async Task UnXmlConCodificaIso88591_SiLegge_ConLeAccentate()
    {
        var percorso = _tmp.Combina("vecchio.xml");
        File.WriteAllBytes(percorso, [.. "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><a>societ"u8.ToArray(), 0xE0, .. "</a>"u8.ToArray()]);

        Assert.Equal("società", await Estrai(percorso));
    }

    [Fact]
    public async Task UnXmlUtf8ConBom_NonHaIlSegnoIniziale()
    {
        var percorso = _tmp.Combina("bom.xml");
        File.WriteAllBytes(percorso, [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("<a>è qui</a>")]);

        Assert.Equal("è qui", await Estrai(percorso));
    }

    [Fact]
    public async Task UnXmlRovinato_SiLegge_ComeTestoSemplice_ESiTrovaComunque()
    {
        var percorso = _tmp.CreaFile("rotto.xml", "<a>fattura numero 12 <b>");

        var testo = await Estrai(percorso);

        Assert.Contains("fattura numero 12", testo);
    }

    [Fact]
    public async Task UnFileVuoto_DaTestoVuoto()
    {
        Assert.Equal("", await Estrai(_tmp.CreaFile("vuoto.xml", "")));
    }

    [Fact]
    public async Task LeDefinizioniEsterne_NonSiSeguono()
    {
        var segreto = _tmp.CreaFile("segreto.txt", "CONTENUTO-RISERVATO");
        var percorso = _tmp.CreaFile("insidioso.xml",
            $"<!DOCTYPE a [<!ENTITY x SYSTEM \"{new Uri(segreto).AbsoluteUri}\">]><a>&x;</a>");

        var testo = await Estrai(percorso);

        Assert.DoesNotContain("CONTENUTO-RISERVATO", testo);
    }

    [Fact]
    public async Task UnXmlGrande_SiLegge_SenzaCaricarloTutto()
    {
        var percorso = _tmp.Combina("grande.xml");
        using (var scrittore = new StreamWriter(percorso))
        {
            scrittore.Write("<log>");
            for (var i = 0; i < 300_000; i++)
                scrittore.Write($"<riga n=\"{i}\">voce di prova numero {i}</riga>");
            scrittore.Write("</log>");
        }

        var testo = await Estrai(percorso);

        Assert.StartsWith("0 voce di prova numero 0 1 voce", testo);
        Assert.InRange(testo.Length, 2_000_000, 2_100_000); // si ferma poco dopo il limite
    }

    [Fact]
    public async Task Annullando_SiInterrompe()
    {
        var percorso = _tmp.CreaFile("a.xml", "<a>ciao</a>");
        using var annullamento = new CancellationTokenSource();
        await annullamento.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _estrattore.EstraiAsync(percorso, annullamento.Token));
    }
}

/// <summary>Un XML allegato si legge in background e si trova con la ricerca, anche se prima era «non supportato».</summary>
public class IndicizzazioneXmlTests : IDisposable
{
    private const string TestoDelLog =
        "<LogEntries><SyncSession><LogEntry type=\"64\"><![CDATA[Calling Pre-Sync -]]></LogEntry><LogEntry type=\"64\">"
        + "<![CDATA[&lt;Pre-sync site_id=&#34;261527941&#34; project_guid=&#34;5456&#34;]]></LogEntry></SyncSession></LogEntries>";

    private readonly ArchivioDiProva _a;

    public IndicizzazioneXmlTests() => _a = new ArchivioDiProva(new EstrattoreXml());

    public void Dispose() => _a.Dispose();

    private async Task<CartellaDettaglio> CartellaConIlLogAsync(ArchivioDiProva archivio)
    {
        var area = await archivio.Servizio.CreaAreaAsync("Inps");
        return await archivio.Servizio.CreaCartellaConDatiAsync(
            area, new DatiCartella("Test cartella inps", null, null, false, null), [archivio.Tmp.CreaFile("sorgenti/synclog.xml", TestoDelLog)]);
    }

    [Fact]
    public async Task UnXmlAllegato_SiTrovaCercandoIlSuoTesto()
    {
        var cartella = await CartellaConIlLogAsync(_a);
        await _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));

        var documento = cartella.Documenti.Single();
        Assert.Equal(StatoIndicizzazione.Indicizzato, _a.StatoDi(documento.Id));

        var esito = await _a.Ricerca.CercaAsync("Pre-sync site_id");

        var trovato = Assert.Single(esito.Risultati);
        Assert.Equal("synclog.xml", trovato.Documento.NomeFile);
        Assert.Contains("Pre", trovato.Trovato); // l'estratto mostra dove è stato trovato
    }

    [Fact]
    public async Task UnXmlGiaSegnatoNonSupportato_AlProssimoAvvio_SiLegge_ESiTrova()
    {
        // L'archivio parte senza lettore per gli XML: il documento resta «non supportato», come prima della novità.
        using var vecchio = new ArchivioDiProva(new FintoEstrattore(".txt"));
        var cartella = await CartellaConIlLogAsync(vecchio);
        await vecchio.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var id = cartella.Documenti.Single().Id;
        Assert.Equal(StatoIndicizzazione.NonSupportato, vecchio.StatoDi(id));
        Assert.Empty((await vecchio.Ricerca.CercaAsync("Pre-sync site_id")).Risultati);

        // Il programma si riavvia con il nuovo lettore.
        using var nuovo = new IndicizzazioneService(vecchio.Factory, vecchio.Files, [new EstrattoreXml()]);
        nuovo.Avvia();
        await nuovo.AccodaPendentiAsync();
        await nuovo.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(StatoIndicizzazione.Indicizzato, vecchio.StatoDi(id));
        Assert.Single((await vecchio.Ricerca.CercaAsync("Pre-sync site_id")).Risultati);
    }
}
