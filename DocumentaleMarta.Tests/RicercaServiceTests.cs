using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;

namespace DocumentaleMarta.Tests;

public class RicercaServiceTests : IDisposable
{
    private const char Inizio = RisultatoRicerca.InizioEvidenza;
    private const char Fine = RisultatoRicerca.FineEvidenza;

    private readonly FintoEstrattore _estrattore = new(".txt");
    private readonly ArchivioDiProva _a;

    private readonly Dictionary<string, string> _testi = new()
    {
        ["preventivo.txt"] = "Preventivo carpenteria metallica per la società Rossi, cancello scorrevole in acciaio zincato. Totale 4.200 euro.",
        ["note.txt"] = "Appuntamento sabato mattina con il cliente per il sopralluogo.",
        ["F24 settembre.txt"] = "Versamento contributi previdenziali INPS trimestre",
        ["modello.txt"] = "Società Metalli è più grande"
    };

    public RicercaServiceTests()
    {
        _estrattore.Logica = (p, _) => Task.FromResult(_testi.GetValueOrDefault(Path.GetFileName(p), ""));
        _a = new ArchivioDiProva(_estrattore);
    }

    public void Dispose() => _a.Dispose();

    /// <summary>
    /// Fatture / "Fattura Rossi" (descrizione "Materiale per cancello"): preventivo.txt, note.txt.
    /// INPS / "Contributi 2026": F24 settembre.txt, ricevuta.xyz (tipo non leggibile: si trova solo dal nome).
    /// Agenzia Entrate / "Dichiarazione": modello.txt.
    /// </summary>
    private async Task PreparaAsync()
    {
        var fatture = await _a.Servizio.CreaAreaAsync("Fatture");
        var inps = await _a.Servizio.CreaAreaAsync("INPS");
        var agenzia = await _a.Servizio.CreaAreaAsync("Agenzia Entrate");
        await _a.CreaCartellaInAreaAsync(fatture, "Fattura Rossi", ["preventivo.txt", "note.txt"], descrizione: "Materiale per cancello");
        await _a.CreaCartellaInAreaAsync(inps, "Contributi 2026", ["F24 settembre.txt", "ricevuta.xyz"]);
        await _a.CreaCartellaInAreaAsync(agenzia, "Dichiarazione", ["modello.txt"]);
        await _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));
    }

    private async Task<string[]> NomiAsync(string testo) =>
        (await _a.Ricerca.CercaAsync(testo)).Risultati.Select(r => r.Documento.NomeFile).OrderBy(n => n).ToArray();

    // ---------- Cosa si trova ----------

    [Fact]
    public async Task ParolaNelTesto_TrovaIlDocumento_ConLEstrattoEvidenziato()
    {
        await PreparaAsync();

        var esito = await _a.Ricerca.CercaAsync("zincato");

        var r = Assert.Single(esito.Risultati);
        Assert.Equal("preventivo.txt", r.Documento.NomeFile);
        Assert.Contains($"{Inizio}zincato{Fine}", r.Trovato);
        Assert.False(esito.Troncato);
    }

    [Fact]
    public async Task InizioDiParola_TrovaLaParolaCompleta()
    {
        await PreparaAsync();

        Assert.Equal(["preventivo.txt"], await NomiAsync("carpent"));
    }

    [Theory]
    [InlineData("societa")]
    [InlineData("SOCIETA")]
    [InlineData("Società")]
    [InlineData("SOCIETÀ")]
    public async Task MaiuscoleEAccentiNonContano(string ricerca)
    {
        await PreparaAsync();

        Assert.Equal(["modello.txt", "preventivo.txt"], await NomiAsync(ricerca));
    }

    [Fact]
    public async Task ParolaNelNomeDelFile_TrovaAncheUnDocumentoNonLeggibile()
    {
        await PreparaAsync();

        var esito = await _a.Ricerca.CercaAsync("ricevuta");

        var r = Assert.Single(esito.Risultati);
        Assert.Equal("ricevuta.xyz", r.Documento.NomeFile);
        Assert.Equal("Nel nome del file", r.Trovato);
    }

    [Fact]
    public async Task ParteDelNomeDelFile_ContaComeSottostringa()
    {
        await PreparaAsync();

        Assert.Equal(["F24 settembre.txt"], await NomiAsync("24 sett"));
    }

    [Fact]
    public async Task ParolaNelTitoloDellaCartella()
    {
        await PreparaAsync();

        var esito = await _a.Ricerca.CercaAsync("dichiarazione");

        var r = Assert.Single(esito.Risultati);
        Assert.Equal("modello.txt", r.Documento.NomeFile);
        Assert.Equal("Nel titolo della cartella", r.Trovato);
    }

    [Fact]
    public async Task ParolaNellaDescrizioneDellaCartella_TrovaTuttiIDocumentiDellaCartella()
    {
        await PreparaAsync();

        var esito = await _a.Ricerca.CercaAsync("materiale");

        Assert.Equal(["note.txt", "preventivo.txt"], esito.Risultati.Select(r => r.Documento.NomeFile).OrderBy(n => n));
        Assert.All(esito.Risultati, r => Assert.Equal("Nella descrizione della cartella", r.Trovato));
    }

    [Fact]
    public async Task ParolaNelNomeDellArea_TrovaTuttiIDocumentiDellArea()
    {
        await PreparaAsync();

        var esito = await _a.Ricerca.CercaAsync("inps");

        // "INPS" è anche dentro il testo di F24 settembre.txt: il documento mostra l'estratto, l'altro il nome dell'area.
        Assert.Equal(["F24 settembre.txt", "ricevuta.xyz"], esito.Risultati.Select(r => r.Documento.NomeFile).OrderBy(n => n));
        Assert.Equal("Nel nome dell'area", esito.Risultati.Single(r => r.Documento.NomeFile == "ricevuta.xyz").Trovato);
        Assert.Contains($"{Inizio}INPS{Fine}", esito.Risultati.Single(r => r.Documento.NomeFile == "F24 settembre.txt").Trovato);
    }

    // ---------- Più parole ----------

    [Fact]
    public async Task PiuParole_DevonoEssereTuttePresenti_ciascunaDoveSiTrova()
    {
        await PreparaAsync();

        // "rossi" sta nel titolo della cartella (note.txt e preventivo.txt), "sabato" solo nel testo di note.txt.
        Assert.Equal(["note.txt"], await NomiAsync("rossi sabato"));
        Assert.Equal(["note.txt", "preventivo.txt"], await NomiAsync("rossi fattura"));
        Assert.Empty(await NomiAsync("rossi contributi"));
    }

    [Fact]
    public async Task LOrdineDelleParoleNonConta()
    {
        await PreparaAsync();

        Assert.Equal(await NomiAsync("sabato rossi"), await NomiAsync("rossi sabato"));
    }

    [Fact]
    public async Task LaPunteggiaturaSepara_leParole()
    {
        await PreparaAsync();

        Assert.Equal(["note.txt"], await NomiAsync("rossi, sabato!"));
        Assert.Equal(["F24 settembre.txt"], await NomiAsync("F24-settembre"));
    }

    [Fact]
    public async Task ParoleRipetute_NonCambianoIlRisultato()
    {
        await PreparaAsync();

        Assert.Equal(await NomiAsync("sabato"), await NomiAsync("sabato SABATO sabato"));
    }

    // ---------- Niente da cercare / niente trovato ----------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!! ???")]
    [InlineData("-*\"()")]
    public async Task TestoSenzaParole_NonTrovaNulla_SenzaErrori(string ricerca)
    {
        await PreparaAsync();

        var esito = await _a.Ricerca.CercaAsync(ricerca);

        Assert.Empty(esito.Risultati);
        Assert.False(esito.Troncato);
    }

    [Fact]
    public async Task NienteDiCorrispondente_ListaVuota()
    {
        await PreparaAsync();

        Assert.Empty(await NomiAsync("xyzzyx"));
    }

    [Fact]
    public async Task ArchivioVuoto_ListaVuota() =>
        Assert.Empty((await _a.Ricerca.CercaAsync("qualcosa")).Risultati);

    [Theory]
    [InlineData("rossi\" OR \"")]
    [InlineData("NEAR(rossi sabato)")]
    [InlineData("rossi AND OR NOT")]
    [InlineData("col:rossi")]
    [InlineData("rossi*")]
    [InlineData("'; DROP TABLE Documenti; --")]
    public async Task UnTestoConOperatoriDiRicerca_NonCausaErrori_EVieneTrattatoComeParole(string ricerca)
    {
        await PreparaAsync();

        var esito = await _a.Ricerca.CercaAsync(ricerca);

        Assert.NotNull(esito);
        Assert.Equal(5, (await _a.Servizio.CaricaDocumentiAsync(null)).Count); // le tabelle sono intatte
    }

    // ---------- Risultati ----------

    [Fact]
    public async Task IRisultati_PortanoCartellaAreaEScadenza()
    {
        var area = await _a.Servizio.CreaAreaAsync("Fatture");
        await _a.CreaCartellaInAreaAsync(area, "Pratica", ["preventivo.txt"], new DateOnly(2026, 12, 31));
        await _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));

        var d = (await _a.Ricerca.CercaAsync("cancello")).Risultati.Single().Documento;

        Assert.Equal("preventivo.txt", d.NomeFile);
        Assert.Equal(".txt", d.Estensione);
        Assert.Equal("Pratica", d.TitoloCartella);
        Assert.Equal("Fatture", d.NomeArea);
        Assert.Equal(area, d.AreaId);
        Assert.Equal(new DateOnly(2026, 12, 31), d.ScadenzaCartella);
        Assert.False(d.CartellaCompletata);
        Assert.True(d.Id > 0 && d.CartellaId > 0);
    }

    [Fact]
    public async Task IRisultati_DalPiuRecenteAlPiuVecchio()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        await _a.CreaCartellaInAreaAsync(area, "C", ["sabato uno.txt", "sabato due.txt", "sabato tre.txt"]);

        var nomi = (await _a.Ricerca.CercaAsync("sabato")).Risultati.Select(r => r.Documento.NomeFile);

        Assert.Equal(["sabato tre.txt", "sabato due.txt", "sabato uno.txt"], nomi);
    }

    [Fact]
    public async Task ConTroppiRisultati_SiFermaAlMassimo_EloSegnala()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        await _a.CreaCartellaInAreaAsync(area, "C", ["uno.txt", "due.txt", "tre.txt", "quattro.txt"]);

        var esito = await _a.Ricerca.CercaAsync("txt", massimo: 3);

        Assert.Equal(3, esito.Risultati.Count);
        Assert.True(esito.Troncato);
        Assert.False((await _a.Ricerca.CercaAsync("txt", massimo: 4)).Troncato);
    }

    [Fact]
    public async Task UnDocumentoEliminato_NonSiTrovaPiu()
    {
        await PreparaAsync();
        var preventivo = (await _a.Ricerca.CercaAsync("zincato")).Risultati.Single().Documento;

        await _a.Servizio.EliminaDocumentoAsync(preventivo.Id);

        Assert.Empty(await NomiAsync("zincato"));
    }

    [Fact]
    public async Task UnDocumentoSpostatoInUnAltraCartella_RestaTrovabileDalTesto_ConLaNuovaCartella()
    {
        await PreparaAsync();
        var preventivo = (await _a.Ricerca.CercaAsync("zincato")).Risultati.Single().Documento;
        var contributi = (await _a.Ricerca.CercaAsync("F24")).Risultati.Single().Documento.CartellaId;

        await _a.Servizio.SpostaDocumentoAsync(preventivo.Id, contributi);

        var trovato = (await _a.Ricerca.CercaAsync("zincato")).Risultati.Single();
        Assert.Equal("Contributi 2026", trovato.Documento.TitoloCartella);
        Assert.Equal("INPS", trovato.Documento.NomeArea);
        Assert.Contains($"{Inizio}zincato{Fine}", trovato.Trovato);
    }

    [Fact]
    public async Task UnaCartellaRinominata_SiTrovaDalNuovoTitolo_EIlTestoRestaCercabile()
    {
        await PreparaAsync();
        var cartella = (await _a.Ricerca.CercaAsync("zincato")).Risultati.Single().Documento.CartellaId;

        await _a.Servizio.RinominaCartellaAsync(cartella, "Cancello Bianchi");

        Assert.Equal(["note.txt", "preventivo.txt"], await NomiAsync("bianchi"));
        Assert.Equal(["preventivo.txt"], await NomiAsync("zincato"));
        Assert.Empty(await NomiAsync("rossi sabato"));
    }

    // ---------- Le parole cercate ----------

    [Theory]
    [InlineData("fattura 123", new[] { "fattura", "123" })]
    [InlineData("  Società   è più ", new[] { "Società", "è", "più" })]
    [InlineData("a,b;c", new[] { "a", "b", "c" })]
    [InlineData("rossi ROSSI Rossi", new[] { "rossi" })]
    [InlineData("perché PERCHE", new[] { "perché" })] // stessa parola senza accento
    [InlineData("", new string[0])]
    [InlineData("---", new string[0])]
    public void Termini_SeparaPerPunteggiatura_SenzaDoppioni(string testo, string[] attesi) =>
        Assert.Equal(attesi, RicercaService.Termini(testo));

    [Fact]
    public void Termini_Al_MassimoDieci() =>
        Assert.Equal(10, RicercaService.Termini("a1 b2 c3 d4 e5 f6 g7 h8 i9 j10 k11 l12").Count);
}
