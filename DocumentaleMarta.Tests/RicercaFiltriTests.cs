using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

public class CategorieFileTests
{
    [Theory]
    [InlineData(".pdf", CategoriaFile.Pdf)]
    [InlineData(".PDF", CategoriaFile.Pdf)]
    [InlineData(".doc", CategoriaFile.Word)]
    [InlineData(".docx", CategoriaFile.Word)]
    [InlineData(".odt", CategoriaFile.Word)]
    [InlineData(".rtf", CategoriaFile.Word)]
    [InlineData(".xls", CategoriaFile.Excel)]
    [InlineData(".xlsx", CategoriaFile.Excel)]
    [InlineData(".csv", CategoriaFile.Excel)]
    [InlineData(".jpg", CategoriaFile.Immagini)]
    [InlineData(".JPEG", CategoriaFile.Immagini)]
    [InlineData(".png", CategoriaFile.Immagini)]
    [InlineData(".tif", CategoriaFile.Immagini)]
    [InlineData(".tiff", CategoriaFile.Immagini)]
    [InlineData(".txt", CategoriaFile.Altro)]
    [InlineData(".pptx", CategoriaFile.Altro)]
    [InlineData(".dwg", CategoriaFile.Altro)]
    [InlineData("", CategoriaFile.Altro)]
    [InlineData(null, CategoriaFile.Altro)]
    public void Di_Estensione(string? estensione, CategoriaFile atteso) =>
        Assert.Equal(atteso, CategorieFile.Di(estensione));
}

public class FiltriRicercaModelloTests
{
    [Fact]
    public void SenzaScelte_NonCeNessunFiltro() =>
        Assert.False(new FiltriRicerca().HaFiltri);

    [Fact]
    public void LInterruttoreDelContenutoDaSoloNonEUnFiltro() =>
        Assert.False(new FiltriRicerca(CercaNelContenuto: false).HaFiltri);

    [Theory]
    [InlineData(CategoriaFile.Pdf)]
    [InlineData(CategoriaFile.Pdf | CategoriaFile.Word)]
    public void UnaCategoria_EUnFiltro(CategoriaFile categorie) =>
        Assert.True(new FiltriRicerca(Categorie: categorie).HaFiltri);

    [Fact]
    public void Area_Stato_EDate_SonoFiltri()
    {
        Assert.True(new FiltriRicerca(AreaId: 3).HaFiltri);
        Assert.True(new FiltriRicerca(Stato: StatoCartella.Aperta).HaFiltri);
        Assert.True(new FiltriRicerca(ScadenzaDal: new DateOnly(2026, 1, 1)).HaFiltri);
        Assert.True(new FiltriRicerca(ScadenzaAl: new DateOnly(2026, 1, 1)).HaFiltri);
    }
}

public class RicercaFiltriTests : IDisposable
{
    private readonly FintoEstrattore _estrattore = new(".pdf", ".docx", ".xlsx", ".jpg", ".txt");
    private readonly ArchivioDiProva _a;
    private int _fatture;
    private int _inps;

    public RicercaFiltriTests()
    {
        _estrattore.Logica = (p, _) => Task.FromResult("verbale del cantiere " + Path.GetFileNameWithoutExtension(p));
        _a = new ArchivioDiProva(_estrattore);
    }

    public void Dispose() => _a.Dispose();

    /// <summary>
    /// Fatture / "Aperta" (scade il 10/11/2026): a.pdf, b.docx.
    /// Fatture / "Chiusa" (completata, scadenza 01/08/2026): c.xlsx, d.jpg.
    /// INPS / "Senza scadenza": e.txt, F.PDF.
    /// </summary>
    private async Task PreparaAsync()
    {
        _fatture = await _a.Servizio.CreaAreaAsync("Fatture");
        _inps = await _a.Servizio.CreaAreaAsync("INPS");
        await _a.CreaCartellaInAreaAsync(_fatture, "Aperta", ["a.pdf", "b.docx"], new DateOnly(2026, 11, 10));
        var chiusa = await _a.CreaCartellaInAreaAsync(_fatture, "Chiusa", ["c.xlsx", "d.jpg"], new DateOnly(2026, 8, 1));
        await _a.Servizio.AggiornaCartellaAsync(chiusa.Id, chiusa.Dati with { Completato = true, DataCompletamento = new DateOnly(2026, 7, 30) });
        await _a.CreaCartellaInAreaAsync(_inps, "Senza scadenza", ["e.txt", "F.PDF"]);
        await _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));
    }

    private async Task<string[]> NomiAsync(string testo, FiltriRicerca? filtri) =>
        (await _a.Ricerca.CercaAsync(testo, filtri)).Risultati.Select(r => r.Documento.NomeFile).OrderBy(n => n, StringComparer.Ordinal).ToArray();

    // ---------- Tipo di file ----------

    [Fact]
    public async Task SoloPdf_ComprendeAncheLEstensioneInMaiuscolo()
    {
        await PreparaAsync();

        Assert.Equal(["F.PDF", "a.pdf"], await NomiAsync("", new FiltriRicerca(Categorie: CategoriaFile.Pdf)));
    }

    [Fact]
    public async Task PiuTipiInsieme_SiSommano()
    {
        await PreparaAsync();

        Assert.Equal(["F.PDF", "a.pdf", "b.docx"],
            await NomiAsync("", new FiltriRicerca(Categorie: CategoriaFile.Pdf | CategoriaFile.Word)));
    }

    [Theory]
    [InlineData(CategoriaFile.Word, new[] { "b.docx" })]
    [InlineData(CategoriaFile.Excel, new[] { "c.xlsx" })]
    [InlineData(CategoriaFile.Immagini, new[] { "d.jpg" })]
    [InlineData(CategoriaFile.Altro, new[] { "e.txt" })]
    public async Task UnTipoAlLaVolta(CategoriaFile categoria, string[] attesi)
    {
        await PreparaAsync();

        Assert.Equal(attesi, await NomiAsync("", new FiltriRicerca(Categorie: categoria)));
    }

    // ---------- Area e stato ----------

    [Fact]
    public async Task SoloUnArea()
    {
        await PreparaAsync();

        Assert.Equal(["e.txt", "F.PDF"], (await NomiAsync("", new FiltriRicerca(AreaId: _inps))).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CartelleAperteOCompletate()
    {
        await PreparaAsync();

        Assert.Equal(["F.PDF", "a.pdf", "b.docx", "e.txt"], await NomiAsync("", new FiltriRicerca(Stato: StatoCartella.Aperta)));
        Assert.Equal(["c.xlsx", "d.jpg"], await NomiAsync("", new FiltriRicerca(Stato: StatoCartella.Completata)));
    }

    // ---------- Scadenza ----------

    [Fact]
    public async Task IntervalloDiScadenza_SoloLeCartelleCheLaHannoEcadonoDentro()
    {
        await PreparaAsync();

        // 10/11 cade in ottobre-dicembre; 01/08 no; "Senza scadenza" è esclusa perché non ce l'ha.
        var filtri = new FiltriRicerca(ScadenzaDal: new DateOnly(2026, 10, 1), ScadenzaAl: new DateOnly(2026, 12, 31));
        Assert.Equal(["a.pdf", "b.docx"], await NomiAsync("", filtri));
    }

    [Fact]
    public async Task UnSoloEstremoDelIntervallo()
    {
        await PreparaAsync();

        Assert.Equal(["a.pdf", "b.docx"], await NomiAsync("", new FiltriRicerca(ScadenzaDal: new DateOnly(2026, 9, 1))));
        Assert.Equal(["c.xlsx", "d.jpg"], await NomiAsync("", new FiltriRicerca(ScadenzaAl: new DateOnly(2026, 9, 1))));
    }

    [Fact]
    public async Task GliEstremiDellIntervalloSonoInclusi()
    {
        await PreparaAsync();

        var esatto = new FiltriRicerca(ScadenzaDal: new DateOnly(2026, 11, 10), ScadenzaAl: new DateOnly(2026, 11, 10));
        Assert.Equal(["a.pdf", "b.docx"], await NomiAsync("", esatto));
        Assert.Empty(await NomiAsync("", new FiltriRicerca(ScadenzaDal: new DateOnly(2026, 11, 11))));
    }

    [Fact]
    public async Task AperteEInScadenza_SiCombinano()
    {
        await PreparaAsync();

        // "Scadute e non completate": la cartella chiusa ha scadenza passata ma non conta.
        var filtri = new FiltriRicerca(Stato: StatoCartella.Aperta, ScadenzaAl: new DateOnly(2026, 9, 1));
        Assert.Empty(await NomiAsync("", filtri));
    }

    // ---------- Filtri e parole insieme ----------

    [Fact]
    public async Task ParoleEFiltri_DevonoValereInsieme()
    {
        await PreparaAsync();

        // "verbale" è nel testo di tutti; il filtro lascia solo i PDF.
        Assert.Equal(6, (await NomiAsync("verbale", null)).Length);
        Assert.Equal(["F.PDF", "a.pdf"], await NomiAsync("verbale", new FiltriRicerca(Categorie: CategoriaFile.Pdf)));
        Assert.Equal(["a.pdf"], await NomiAsync("verbale", new FiltriRicerca(Categorie: CategoriaFile.Pdf, AreaId: _fatture)));
        Assert.Empty(await NomiAsync("verbale", new FiltriRicerca(Categorie: CategoriaFile.Excel, AreaId: _inps)));
    }

    [Fact]
    public async Task SoloFiltri_SenzaParole_LEstrattoEVuoto_EInRisultatiSonoTutti()
    {
        await PreparaAsync();

        var esito = await _a.Ricerca.CercaAsync("", new FiltriRicerca(Categorie: CategoriaFile.Pdf));

        Assert.Equal(2, esito.Risultati.Count);
        Assert.All(esito.Risultati, r => Assert.Equal("", r.Trovato));
        Assert.False(esito.Troncato);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("!!!")]
    public async Task SenzaParoleESenzaFiltri_NonTrovaNulla(string testo)
    {
        await PreparaAsync();

        Assert.Empty((await _a.Ricerca.CercaAsync(testo, new FiltriRicerca())).Risultati);
        Assert.Empty((await _a.Ricerca.CercaAsync(testo, null)).Risultati);
        Assert.Empty((await _a.Ricerca.CercaAsync(testo, new FiltriRicerca(CercaNelContenuto: false))).Risultati);
    }

    // ---------- Cercare anche nel contenuto ----------

    [Fact]
    public async Task SenzaCercareNelContenuto_SiTrovaSoloDaiNomi()
    {
        await PreparaAsync();

        // "cantiere" sta solo dentro i documenti, non nei nomi.
        Assert.Equal(6, (await NomiAsync("cantiere", new FiltriRicerca())).Length);
        Assert.Empty(await NomiAsync("cantiere", new FiltriRicerca(CercaNelContenuto: false)));

        // Un nome si trova comunque.
        Assert.Equal(["b.docx"], await NomiAsync("docx", new FiltriRicerca(CercaNelContenuto: false)));
        Assert.Equal(["a.pdf", "b.docx"], await NomiAsync("aperta", new FiltriRicerca(CercaNelContenuto: false)));
    }

    [Fact]
    public async Task ConIlContenuto_LEstrattoEvidenzia_SenzaIlContenuto_DiceDoveLHaTrovato()
    {
        await PreparaAsync();

        var conContenuto = (await _a.Ricerca.CercaAsync("aperta", new FiltriRicerca())).Risultati;
        var senzaContenuto = (await _a.Ricerca.CercaAsync("aperta", new FiltriRicerca(CercaNelContenuto: false))).Risultati;

        Assert.All(senzaContenuto, r => Assert.Equal("Nel titolo della cartella", r.Trovato));
        Assert.Equal(conContenuto.Count, senzaContenuto.Count);
    }
}
