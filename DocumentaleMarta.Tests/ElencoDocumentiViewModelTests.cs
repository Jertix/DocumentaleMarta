using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.Tests;

public class ElencoDocumentiViewModelTests : IDisposable
{
    private readonly ArchivioDiProva _a = new();
    private int _fatture;
    private int _inps;

    public void Dispose() => _a.Dispose();

    /// <summary>Fatture/Fattura 1 (2 documenti, scadenza 30/11/2026) e INPS/Contributi (1 documento).</summary>
    private async Task PreparaAsync()
    {
        _fatture = await _a.Servizio.CreaAreaAsync("Fatture");
        _inps = await _a.Servizio.CreaAreaAsync("INPS");
        await _a.CreaCartellaInAreaAsync(_fatture, "Fattura 1", ["uno.pdf", "due.jpg"], new DateOnly(2026, 11, 30));
        await _a.CreaCartellaInAreaAsync(_inps, "Contributi", ["tre.docx"]);
    }

    private async Task<ElencoDocumentiViewModel> ElencoAsync(int? areaId)
    {
        var elenco = new ElencoDocumentiViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, areaId);
        await elenco.CaricaAsync();
        return elenco;
    }

    // ---------- Contenuto ----------

    [Fact]
    public async Task Tutti_MostraTuttiIDocumenti_ConLaColonnaArea()
    {
        await PreparaAsync();

        var elenco = await ElencoAsync(null);

        Assert.Equal(3, elenco.Documenti.Count);
        Assert.True(elenco.MostraArea);
        Assert.Contains("archivio", elenco.TestoVuoto);
        var uno = elenco.Documenti.Single(d => d.NomeFile == "uno.pdf");
        Assert.Equal("PDF", uno.Tipo);
        Assert.Equal("Fattura 1", uno.TitoloCartella);
        Assert.Equal("Fatture", uno.NomeArea);
        Assert.Equal(new DateTime(2026, 11, 30), uno.Scadenza);
        Assert.False(uno.FileMancante);
        Assert.Null(elenco.Documenti.Single(d => d.NomeFile == "tre.docx").Scadenza);
    }

    [Fact]
    public async Task DiUnArea_MostraSoloQuelliDellArea_SenzaColonnaArea()
    {
        await PreparaAsync();

        var elenco = await ElencoAsync(_inps);

        Assert.Equal(["tre.docx"], elenco.Documenti.Select(d => d.NomeFile));
        Assert.False(elenco.MostraArea);
        Assert.Contains("area", elenco.TestoVuoto);
    }

    [Fact]
    public async Task ArchivioVuoto_ElencoVuoto()
    {
        Assert.Empty((await ElencoAsync(null)).Documenti);
    }

    [Fact]
    public async Task RicaricandoSiAggiorna_SenzaDuplicareLeRighe()
    {
        await PreparaAsync();
        var elenco = await ElencoAsync(null);
        await _a.Servizio.AllegaDocumentiAsync(
            (await _a.Servizio.CaricaDocumentiAsync(_fatture))[0].CartellaId, [_a.Tmp.CreaFile("s/nuovo.txt")]);

        await elenco.CaricaAsync();

        Assert.Equal(4, elenco.Documenti.Count);
    }

    [Fact]
    public async Task FileSparitoDalDisco_LaRigaLoSegnala()
    {
        await PreparaAsync();
        File.Delete(_a.Fisico("Fatture", "Fattura 1", "uno.pdf"));

        var elenco = await ElencoAsync(null);

        Assert.True(elenco.Documenti.Single(d => d.NomeFile == "uno.pdf").FileMancante);
        Assert.False(elenco.Documenti.Single(d => d.NomeFile == "due.jpg").FileMancante);
    }

    // ---------- Pulsanti ----------

    [Fact]
    public async Task Apri_UsaIlProgrammaPredefinito()
    {
        await PreparaAsync();
        var elenco = await ElencoAsync(null);

        elenco.Documenti.Single(d => d.NomeFile == "tre.docx").ApriCommand.Execute(null);

        Assert.Equal([_a.Fisico("INPS", "Contributi", "tre.docx")], _a.Shell.FileAperti);
    }

    [Fact]
    public async Task Apri_FileMancante_MostraErrore_SegnaLaRiga()
    {
        await PreparaAsync();
        var elenco = await ElencoAsync(null);
        File.Delete(_a.Fisico("INPS", "Contributi", "tre.docx"));
        var riga = elenco.Documenti.Single(d => d.NomeFile == "tre.docx");

        riga.ApriCommand.Execute(null);

        Assert.Empty(_a.Shell.FileAperti);
        Assert.True(riga.FileMancante);
        Assert.Contains("non si trova più", Assert.Single(_a.Dialog.Errori));
    }

    [Fact]
    public async Task ApriNellaCartella_MostraIlFileInEsplora()
    {
        await PreparaAsync();
        var elenco = await ElencoAsync(null);

        elenco.Documenti.Single(d => d.NomeFile == "due.jpg").ApriNellaCartellaCommand.Execute(null);

        Assert.Equal([_a.Fisico("Fatture", "Fattura 1", "due.jpg")], _a.Shell.FileMostrati);
    }

    [Fact]
    public async Task Elimina_ConConferma_RimuoveLaRiga_EAvvisaConLaCartella()
    {
        await PreparaAsync();
        var elenco = await ElencoAsync(null);
        var cartelleAvvisate = new List<int>();
        elenco.DocumentoEliminato += cartelleAvvisate.Add;
        var riga = elenco.Documenti.Single(d => d.NomeFile == "uno.pdf");

        await riga.EliminaCommand.ExecuteAsync(null);

        Assert.DoesNotContain(elenco.Documenti, d => d.NomeFile == "uno.pdf");
        Assert.Equal([riga.CartellaId], cartelleAvvisate);
        Assert.False(File.Exists(_a.Fisico("Fatture", "Fattura 1", "uno.pdf")));
        Assert.Contains("«uno.pdf»", Assert.Single(_a.Dialog.Conferme));
    }

    [Fact]
    public async Task Elimina_ConfermaNegata_NonEliminaNulla()
    {
        await PreparaAsync();
        var elenco = await ElencoAsync(null);
        _a.Dialog.RispostaConferma = false;

        await elenco.Documenti[0].EliminaCommand.ExecuteAsync(null);

        Assert.Equal(3, elenco.Documenti.Count);
    }

    [Fact]
    public async Task Elimina_FileAperto_MostraErrore_LaRigaResta()
    {
        await PreparaAsync();
        var elenco = await ElencoAsync(null);

        using (new FileStream(_a.Fisico("INPS", "Contributi", "tre.docx"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await elenco.Documenti.Single(d => d.NomeFile == "tre.docx").EliminaCommand.ExecuteAsync(null);
        }

        Assert.Equal(3, elenco.Documenti.Count);
        Assert.Contains("aperto in un altro programma", Assert.Single(_a.Dialog.Errori));
    }

    [Fact]
    public async Task Elimina_DocumentoGiaEliminatoAltrove_ChiedeDiRicaricare()
    {
        await PreparaAsync();
        var elenco = await ElencoAsync(null);
        var ricariche = 0;
        elenco.RicaricaRichiesta += () => ricariche++;
        var riga = elenco.Documenti.Single(d => d.NomeFile == "uno.pdf");
        await _a.Servizio.EliminaDocumentoAsync(riga.Id);

        await riga.EliminaCommand.ExecuteAsync(null);

        Assert.Equal(1, ricariche);
        Assert.Contains("non esiste più", Assert.Single(_a.Dialog.Errori));
    }

    [Fact]
    public async Task VaiAllaCartella_ChiedeLaCartellaDelDocumento()
    {
        await PreparaAsync();
        var elenco = await ElencoAsync(null);
        var richieste = new List<int>();
        elenco.VaiAllaCartellaRichiesto += richieste.Add;
        var riga = elenco.Documenti.Single(d => d.NomeFile == "tre.docx");

        riga.VaiAllaCartellaCommand.Execute(null);

        Assert.Equal([riga.CartellaId], richieste);
    }
}
