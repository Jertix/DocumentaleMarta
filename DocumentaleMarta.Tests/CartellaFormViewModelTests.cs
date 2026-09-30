using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Modelli;

namespace DocumentaleMarta.Tests;

public class CartellaFormViewModelTests : IDisposable
{
    private readonly ArchivioDiProva _a = new();

    public void Dispose() => _a.Dispose();

    private CartellaFormViewModel Form(CartellaDettaglio dettaglio) =>
        new(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, dettaglio);

    private Task<CartellaDettaglio?> Rileggi(CartellaFormViewModel form) => _a.Servizio.CaricaCartellaAsync(form.Id);

    // ---------- Caricamento ----------

    [Fact]
    public async Task Carica_CampiEDocumenti()
    {
        var areaId = await _a.Servizio.CreaAreaAsync("Fatture");
        var d = await _a.Servizio.CreaCartellaConDatiAsync(
            areaId,
            new DatiCartella("Fattura 1", "note", new DateOnly(2026, 12, 31), true, new DateOnly(2026, 10, 1)),
            [_a.Tmp.CreaFile("a.pdf", "abc")]);

        var form = Form(d);

        Assert.Equal("Fatture", form.NomeArea);
        Assert.Equal("Fattura 1", form.Titolo);
        Assert.Equal("note", form.Descrizione);
        Assert.Equal(new DateTime(2026, 12, 31), form.DataScadenza);
        Assert.True(form.Completato);
        Assert.Equal(new DateTime(2026, 10, 1), form.DataCompletamento);
        var riga = Assert.Single(form.Documenti);
        Assert.Equal("a.pdf", riga.NomeFile);
        Assert.Equal("PDF", riga.Tipo);
        Assert.Equal("3 B", riga.DimensioneTesto);
        Assert.False(riga.FileMancante);
        Assert.Equal("Documenti (1)", form.TitoloDocumenti);
    }

    [Fact]
    public async Task Carica_FileSparitoDalDisco_LaRigaLoSegnala()
    {
        var d = await _a.CreaCartellaAsync("Fatture", "P", "a.pdf");
        File.Delete(_a.Files.PercorsoAssoluto(d.Documenti[0].PercorsoRelativo));

        Assert.True(Form(d).Documenti.Single().FileMancante);
    }

    // ---------- Salvataggio automatico ----------

    [Fact]
    public async Task ModificandoLaDescrizione_SiSalvaDaSola()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P"));

        form.Descrizione = "nuova nota";
        await form.AttendiSalvataggioAsync();

        Assert.Equal("nuova nota", (await Rileggi(form))!.Dati.Descrizione);
        Assert.Equal("", form.Errore);
    }

    [Fact]
    public async Task ImpostandoLaScadenza_SiSalva_ERimuovendolaSiCancella()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P"));

        form.DataScadenza = new DateTime(2027, 3, 15, 10, 0, 0);
        await form.AttendiSalvataggioAsync();
        Assert.Equal(new DateOnly(2027, 3, 15), (await Rileggi(form))!.Dati.DataScadenza);

        form.CancellaScadenzaCommand.Execute(null);
        await form.AttendiSalvataggioAsync();
        Assert.Null((await Rileggi(form))!.Dati.DataScadenza);
    }

    [Fact]
    public async Task SpuntandoCompletato_LaDataDiventaOggi_EsiSalvaUnaVolta()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P"));

        form.Completato = true;
        await form.AttendiSalvataggioAsync();

        var salvata = (await Rileggi(form))!.Dati;
        Assert.True(salvata.Completato);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), salvata.DataCompletamento);
    }

    [Fact]
    public async Task TogliendoCompletato_LaDataSparisceDalDatabase()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P"));
        form.Completato = true;
        await form.AttendiSalvataggioAsync();

        form.Completato = false;
        await form.AttendiSalvataggioAsync();

        var salvata = (await Rileggi(form))!.Dati;
        Assert.False(salvata.Completato);
        Assert.Null(salvata.DataCompletamento);
        Assert.Null(form.DataCompletamento);
    }

    [Fact]
    public async Task ModificandoLaDataDiCompletamento_SiSalva()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P"));
        form.Completato = true;

        form.DataCompletamento = new DateTime(2026, 6, 15);
        await form.AttendiSalvataggioAsync();

        Assert.Equal(new DateOnly(2026, 6, 15), (await Rileggi(form))!.Dati.DataCompletamento);
    }

    [Fact]
    public async Task CambiandoIlTitolo_RinominaLaCartellaFisica_AggiornaIPercorsi_EAvvisaLAlbero()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "Vecchio", "a.pdf"));
        var avvisi = new List<(string Titolo, string Percorso)>();
        form.TitoloSalvato += (t, p) => avvisi.Add((t, p));

        form.Titolo = "Nuovo";
        await form.AttendiSalvataggioAsync();

        Assert.Equal(Path.Combine("Fatture", "Nuovo"), form.PercorsoRelativo);
        Assert.Equal([("Nuovo", Path.Combine("Fatture", "Nuovo"))], avvisi);
        Assert.Equal(Path.Combine("Fatture", "Nuovo", "a.pdf"), form.Documenti.Single().PercorsoRelativo);
        Assert.False(form.Documenti.Single().FileMancante);
        Assert.True(File.Exists(_a.Fisico("Fatture", "Nuovo", "a.pdf")));
        Assert.False(Directory.Exists(_a.Fisico("Fatture", "Vecchio")));
    }

    [Fact]
    public async Task ModificandoSoloLaDescrizione_NonAvvisaLAlberoDelTitolo()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P"));
        var avvisi = 0;
        form.TitoloSalvato += (_, _) => avvisi++;

        form.Descrizione = "x";
        await form.AttendiSalvataggioAsync();

        Assert.Equal(0, avvisi);
    }

    [Fact]
    public async Task TitoloVuoto_DaErrore_RimetteIlVecchioTitolo_NonSalvaNulla()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "Pratica"));

        form.Titolo = "   ";
        await form.AttendiSalvataggioAsync();

        Assert.Equal("Pratica", form.Titolo);
        Assert.Contains("titolo", form.Errore);
        Assert.Equal("Pratica", (await Rileggi(form))!.Dati.Titolo);
        Assert.Empty(_a.Dialog.Errori);
    }

    [Fact]
    public async Task DopoUnErrore_LaProssimaModificaValidaLoCancella()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "Pratica"));
        form.Titolo = "";
        await form.AttendiSalvataggioAsync();
        Assert.NotEqual("", form.Errore);

        form.Descrizione = "ok";
        await form.AttendiSalvataggioAsync();

        Assert.Equal("", form.Errore);
    }

    [Fact]
    public async Task TitoloCambiatoConUnDocumentoAperto_MostraErrore_RimetteIlTitolo_ESalvaGliAltriCampi()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "Vecchio", "a.pdf"));
        var fisico = _a.Fisico("Fatture", "Vecchio", "a.pdf");

        using (new FileStream(fisico, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            form.Titolo = "Nuovo";
            await form.AttendiSalvataggioAsync();
        }

        Assert.Contains("aperti in altri programmi", Assert.Single(_a.Dialog.Errori));
        Assert.Equal("Vecchio", form.Titolo);
        Assert.Equal("Vecchio", (await Rileggi(form))!.Dati.Titolo);
        Assert.True(File.Exists(fisico));
    }

    [Fact]
    public async Task CartellaEliminataAltrove_LaModificaDaErroreEChiedeDiRicaricare()
    {
        var d = await _a.CreaCartellaAsync("Fatture", "P");
        var form = Form(d);
        var ricariche = 0;
        form.RicaricaRichiesta += () => ricariche++;
        await _a.Servizio.EliminaCartellaAsync(d.Id);

        form.Descrizione = "x";
        await form.AttendiSalvataggioAsync();

        Assert.Contains("non esiste più", form.Errore);
        Assert.Equal(1, ricariche);
    }

    // ---------- Allegati ----------

    [Fact]
    public async Task Allega_AggiungeLeRighe_ECopiaIFile_EAvvisaIlNumero()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P"));
        var numeri = new List<int>();
        form.NumeroDocumentiCambiato += numeri.Add;
        var f1 = _a.Tmp.CreaFile("s/uno.pdf");
        var f2 = _a.Tmp.CreaFile("s/due.docx");

        _a.Dialog.RispondiFile(f1, f2);
        await form.AllegaCommand.ExecuteAsync(null);

        Assert.Equal(["uno.pdf", "due.docx"], form.Documenti.Select(d => d.NomeFile));
        Assert.Equal([2], numeri);
        Assert.Equal("Documenti (2)", form.TitoloDocumenti);
        Assert.True(File.Exists(_a.Fisico("Fatture", "P", "uno.pdf")));
        Assert.True(File.Exists(f1)); // l'originale resta
        Assert.Equal("P", form.Titolo);  // il titolo di una cartella esistente non cambia
    }

    [Fact]
    public async Task Allega_Annullato_NonFaNulla()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P"));

        _a.Dialog.RispondiFile();
        await form.AllegaCommand.ExecuteAsync(null);

        Assert.Empty(form.Documenti);
        Assert.Empty(_a.Dialog.Errori);
    }

    [Fact]
    public async Task Allega_FileInesistente_MostraErroreENonAllegaNulla()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P"));

        _a.Dialog.RispondiFile(_a.Tmp.CreaFile("s/ok.pdf"), _a.Tmp.Combina("manca.pdf"));
        await form.AllegaCommand.ExecuteAsync(null);

        Assert.Contains("Non è stato allegato nessun file", Assert.Single(_a.Dialog.Errori));
        Assert.Empty(form.Documenti);
        Assert.Empty(Directory.GetFiles(_a.Fisico("Fatture", "P")));
    }

    // ---------- Pulsanti della griglia ----------

    [Fact]
    public async Task Apri_UsaIlProgrammaPredefinitoSulFileDellArchivio()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P", "a.pdf"));

        form.Documenti[0].ApriCommand.Execute(null);

        Assert.Equal([_a.Fisico("Fatture", "P", "a.pdf")], _a.Shell.FileAperti);
    }

    [Fact]
    public async Task Apri_FileMancante_MostraErrore_SegnaLaRiga_NonApreNulla()
    {
        var d = await _a.CreaCartellaAsync("Fatture", "P", "a.pdf");
        var form = Form(d);
        File.Delete(_a.Fisico("Fatture", "P", "a.pdf"));

        form.Documenti[0].ApriCommand.Execute(null);

        Assert.Empty(_a.Shell.FileAperti);
        Assert.True(form.Documenti[0].FileMancante);
        Assert.Contains("non si trova più", Assert.Single(_a.Dialog.Errori));
    }

    [Fact]
    public async Task Apri_SenzaProgrammaAssociato_SpiegaCosaFare()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P", "a.xyz"));
        _a.Shell.NessunProgrammaAssociato = true;

        form.Documenti[0].ApriCommand.Execute(null);

        Assert.Contains("XYZ", Assert.Single(_a.Dialog.Errori));
    }

    [Fact]
    public async Task ApriNellaCartella_MostraIlFileInEsplora()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P", "a.pdf"));

        form.Documenti[0].ApriNellaCartellaCommand.Execute(null);

        Assert.Equal([_a.Fisico("Fatture", "P", "a.pdf")], _a.Shell.FileMostrati);
    }

    [Fact]
    public async Task ApriNellaCartella_FileMancante_MostraErrore()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P", "a.pdf"));
        File.Delete(_a.Fisico("Fatture", "P", "a.pdf"));

        form.Documenti[0].ApriNellaCartellaCommand.Execute(null);

        Assert.Empty(_a.Shell.FileMostrati);
        Assert.Single(_a.Dialog.Errori);
    }

    [Fact]
    public async Task Elimina_ConConferma_RimuoveRigaEFile_EAvvisaIlNumero()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P", "a.pdf", "b.pdf"));
        var numeri = new List<int>();
        form.NumeroDocumentiCambiato += numeri.Add;

        await form.Documenti[0].EliminaCommand.ExecuteAsync(null);

        var messaggio = Assert.Single(_a.Dialog.Conferme);
        Assert.Contains("«a.pdf»", messaggio);
        Assert.Contains("Cestino", messaggio);
        Assert.Equal(["b.pdf"], form.Documenti.Select(d => d.NomeFile));
        Assert.Equal([1], numeri);
        Assert.False(File.Exists(_a.Fisico("Fatture", "P", "a.pdf")));
        Assert.Single((await Rileggi(form))!.Documenti);
    }

    [Fact]
    public async Task Elimina_ConfermaNegata_NonEliminaNulla()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P", "a.pdf"));
        _a.Dialog.RispostaConferma = false;

        await form.Documenti[0].EliminaCommand.ExecuteAsync(null);

        Assert.Single(form.Documenti);
        Assert.True(File.Exists(_a.Fisico("Fatture", "P", "a.pdf")));
    }

    [Fact]
    public async Task Elimina_FileAperto_MostraErrore_LaRigaResta()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P", "a.pdf"));

        using (new FileStream(_a.Fisico("Fatture", "P", "a.pdf"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await form.Documenti[0].EliminaCommand.ExecuteAsync(null);
        }

        Assert.Contains("aperto in un altro programma", Assert.Single(_a.Dialog.Errori));
        Assert.Single(form.Documenti);
        Assert.Single((await Rileggi(form))!.Documenti);
    }

    [Fact]
    public async Task ApriCartella_ApreEsploraFileSullaCartellaDellaPratica()
    {
        var form = Form(await _a.CreaCartellaAsync("Fatture", "P"));

        form.ApriCartellaCommand.Execute(null);

        Assert.Equal([_a.Fisico("Fatture", "P")], _a.Shell.CartelleAperte);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1 KB")]
    [InlineData(1536, "1,5 KB")]
    [InlineData(5 * 1024 * 1024, "5 MB")]
    [InlineData(3L * 1024 * 1024 * 1024, "3 GB")]
    public void Dimensione_InUnitaLeggibili(long byte_, string atteso)
    {
        var cultura = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("it-IT");
            Assert.Equal(atteso, FormatiTesto.Dimensione(byte_));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = cultura;
        }
    }
}
