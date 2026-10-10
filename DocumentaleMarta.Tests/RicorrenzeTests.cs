using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

public class CalcoloRicorrenzaTests
{
    [Theory]
    [InlineData(Ricorrenza.Mensile, "2026-10-16", "2026-11-16")]
    [InlineData(Ricorrenza.Mensile, "2026-12-16", "2027-01-16")]
    [InlineData(Ricorrenza.Bimestrale, "2026-10-16", "2026-12-16")]
    [InlineData(Ricorrenza.Bimestrale, "2026-12-31", "2027-02-28")]
    [InlineData(Ricorrenza.Trimestrale, "2026-10-16", "2027-01-16")]
    [InlineData(Ricorrenza.Trimestrale, "2026-11-30", "2027-02-28")]
    [InlineData(Ricorrenza.Annuale, "2026-10-16", "2027-10-16")]
    public void LaScadenzaSuccessiva_ESpostataInAvanti(Ricorrenza ricorrenza, string da, string a) =>
        Assert.Equal(DateOnly.Parse(a), CalcoloRicorrenza.Prossima(DateOnly.Parse(da), ricorrenza));

    [Theory]
    [InlineData("2026-01-31", "2026-02-28")] // il giorno 31 non esiste in febbraio: ultimo giorno del mese
    [InlineData("2028-01-31", "2028-02-29")] // anno bisestile
    [InlineData("2026-04-30", "2026-05-30")]
    public void Mensile_ConGiorniAFineMese_UsaLUltimoGiornoDisponibile(string da, string a) =>
        Assert.Equal(DateOnly.Parse(a), CalcoloRicorrenza.Prossima(DateOnly.Parse(da), Ricorrenza.Mensile));

    [Fact]
    public void Annuale_DalVentinoveFebbraio_VaAlVentottoDellAnnoNonBisestile() =>
        Assert.Equal(new DateOnly(2029, 2, 28), CalcoloRicorrenza.Prossima(new DateOnly(2028, 2, 29), Ricorrenza.Annuale));

    [Fact]
    public void ConUnaCartellaCheNonSiRipete_Lancia() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CalcoloRicorrenza.Prossima(new DateOnly(2026, 1, 1), Ricorrenza.Nessuna));

    [Theory]
    [InlineData(Ricorrenza.Nessuna, "")]
    [InlineData(Ricorrenza.Mensile, "ogni mese")]
    [InlineData(Ricorrenza.Bimestrale, "ogni 2 mesi")]
    [InlineData(Ricorrenza.Trimestrale, "ogni 3 mesi")]
    [InlineData(Ricorrenza.Annuale, "ogni anno")]
    public void LaDescrizione_ESempreInItaliano(Ricorrenza ricorrenza, string atteso) =>
        Assert.Equal(atteso, CalcoloRicorrenza.Descrizione(ricorrenza));
}

public class RicorrenzaArchivioTests : IDisposable
{
    private readonly ArchivioDiProva _a = new();

    public void Dispose() => _a.Dispose();

    private async Task<CartellaDettaglio> CreaAsync(DatiCartella dati)
    {
        var areaId = await _a.Servizio.CreaAreaAsync("Fatture");
        return await _a.Servizio.CreaCartellaConDatiAsync(areaId, dati, []);
    }

    [Fact]
    public async Task LaRicorrenza_SiSalvaESiRilegge()
    {
        var creata = await CreaAsync(new DatiCartella("F24", null, new DateOnly(2026, 10, 16), false, null, Ricorrenza.Mensile));

        Assert.Equal(Ricorrenza.Mensile, creata.Dati.Ricorrenza);
        Assert.Equal(Ricorrenza.Mensile, (await _a.Servizio.CaricaCartellaAsync(creata.Id))!.Dati.Ricorrenza);
    }

    [Fact]
    public async Task SenzaScadenza_LaRicorrenzaSiAzzera()
    {
        var creata = await CreaAsync(new DatiCartella("F24", null, null, false, null, Ricorrenza.Annuale));

        Assert.Equal(Ricorrenza.Nessuna, creata.Dati.Ricorrenza);
    }

    [Fact]
    public async Task TogliendoLaScadenza_LaRicorrenzaSiAzzera()
    {
        var creata = await CreaAsync(new DatiCartella("F24", null, new DateOnly(2026, 10, 16), false, null, Ricorrenza.Trimestrale));

        var aggiornata = await _a.Servizio.AggiornaCartellaAsync(creata.Id, creata.Dati with { DataScadenza = null });

        Assert.Equal(Ricorrenza.Nessuna, aggiornata.Dati.Ricorrenza);
    }

    [Fact]
    public async Task ModificandoSoloLaRicorrenza_SiSalva()
    {
        var creata = await CreaAsync(new DatiCartella("F24", null, new DateOnly(2026, 10, 16), false, null));

        var aggiornata = await _a.Servizio.AggiornaCartellaAsync(creata.Id, creata.Dati with { Ricorrenza = Ricorrenza.Annuale });

        Assert.Equal(Ricorrenza.Annuale, aggiornata.Dati.Ricorrenza);
    }

    [Fact]
    public async Task UnaCartellaSenzaRicorrenzaIndicata_NonSiRipete()
    {
        var creata = await CreaAsync(new DatiCartella("F24", null, new DateOnly(2026, 10, 16), false, null));

        Assert.Equal(Ricorrenza.Nessuna, creata.Dati.Ricorrenza);
    }
}

public class RicorrenzaViewModelTests : IDisposable
{
    private static readonly DateOnly Scadenza = new(2026, 10, 16);

    private readonly ArchivioDiProva _a = new();

    public void Dispose() => _a.Dispose();

    private async Task<CartellaDettaglio> CreaAsync(
        Ricorrenza ricorrenza, string titolo = "F24", DateOnly? scadenza = null, string? descrizione = "Versamenti", params string[] file)
    {
        var areaId = (await _a.Servizio.CaricaAlberoAsync()).FirstOrDefault()?.Id ?? await _a.Servizio.CreaAreaAsync("Fiscale");
        var sorgenti = file.Select(n => _a.Tmp.CreaFile(Path.Combine("sorg", n), "contenuto " + n)).ToList();
        return await _a.Servizio.CreaCartellaConDatiAsync(
            areaId, new DatiCartella(titolo, descrizione, scadenza ?? Scadenza, false, null, ricorrenza), sorgenti);
    }

    private CartellaFormViewModel Form(CartellaDettaglio d) => new(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, d);

    private async Task<IReadOnlyList<CartellaNodo>> CartelleAsync() => (await _a.Servizio.CaricaAlberoAsync()).Single().Cartelle;

    // ---------- I campi ----------

    [Fact]
    public async Task Il_FormMostraLaRicorrenzaSalvata()
    {
        var form = Form(await CreaAsync(Ricorrenza.Trimestrale));

        Assert.Equal(Ricorrenza.Trimestrale, form.Ricorrenza);
        Assert.True(form.SiRipete);
        Assert.True(form.HaScadenza);
        Assert.Equal(["Mai", "Ogni mese", "Ogni 2 mesi", "Ogni 3 mesi", "Ogni anno"], form.OpzioniRicorrenza.Select(o => o.Testo));
    }

    [Fact]
    public async Task CambiandoLaRicorrenza_SiSalvaDaSola()
    {
        var form = Form(await CreaAsync(Ricorrenza.Nessuna));

        form.Ricorrenza = Ricorrenza.Annuale;
        await form.AttendiSalvataggioAsync();

        Assert.Equal(Ricorrenza.Annuale, (await _a.Servizio.CaricaCartellaAsync(form.Id))!.Dati.Ricorrenza);
    }

    [Fact]
    public async Task TogliendoLaScadenza_LaRicorrenzaTornaAMai_ESalvaTutto()
    {
        var form = Form(await CreaAsync(Ricorrenza.Mensile));
        var cambiate = new List<string?>();
        form.PropertyChanged += (_, e) => cambiate.Add(e.PropertyName);

        form.CancellaScadenzaCommand.Execute(null);
        await form.AttendiSalvataggioAsync();

        Assert.Equal(Ricorrenza.Nessuna, form.Ricorrenza);
        Assert.False(form.HaScadenza);
        Assert.Contains(nameof(CartellaCampiViewModel.HaScadenza), cambiate);
        Assert.Contains(nameof(CartellaCampiViewModel.SiRipete), cambiate);
        var salvata = (await _a.Servizio.CaricaCartellaAsync(form.Id))!.Dati;
        Assert.Null(salvata.DataScadenza);
        Assert.Equal(Ricorrenza.Nessuna, salvata.Ricorrenza);
    }

    [Fact]
    public void LaFinestraNuovaCartella_PortaLaRicorrenzaNeiDati()
    {
        var vm = new NuovaCartellaViewModel(_a.Dialog, "Fiscale")
        {
            Titolo = "F24",
            DataScadenza = new DateTime(2026, 10, 16),
            Ricorrenza = Ricorrenza.Mensile
        };

        Assert.Equal(Ricorrenza.Mensile, vm.Dati.Ricorrenza);
    }

    [Fact]
    public void LaFinestraNuovaCartella_SenzaScadenza_NonPortaLaRicorrenza()
    {
        var vm = new NuovaCartellaViewModel(_a.Dialog, "Fiscale") { Titolo = "F24", Ricorrenza = Ricorrenza.Mensile };

        Assert.Equal(Ricorrenza.Nessuna, vm.Dati.Ricorrenza);
    }

    // ---------- La proposta della cartella successiva ----------

    [Fact]
    public async Task CompletandoUnaCartellaCheSiRipete_ProponeLaSuccessiva_ECreandolaCopiaIDati()
    {
        var originale = await CreaAsync(Ricorrenza.Mensile, file: ["modello.pdf"]);
        var form = Form(originale);
        var create = 0;
        form.CartellaSuccessivaCreata += () => create++;
        _a.Dialog.RispondiDomande(true, false); // sì alla cartella, no ai documenti

        form.Completato = true;
        await form.AttendiSalvataggioAsync();

        Assert.Equal(2, _a.Dialog.Domande.Count);
        var domanda = _a.Dialog.Domande[0];
        Assert.Contains("«F24»", domanda);
        Assert.Contains("ogni mese", domanda);
        Assert.Contains("16/11/2026", domanda);
        Assert.Contains("documento", _a.Dialog.Domande[1]);

        var cartelle = await CartelleAsync();
        Assert.Equal(2, cartelle.Count);
        var nuova = await _a.Servizio.CaricaCartellaAsync(cartelle.Single(c => c.Id != originale.Id).Id);
        Assert.Equal("F24", nuova!.Dati.Titolo);
        Assert.Equal("Versamenti", nuova.Dati.Descrizione);
        Assert.Equal(new DateOnly(2026, 11, 16), nuova.Dati.DataScadenza);
        Assert.Equal(Ricorrenza.Mensile, nuova.Dati.Ricorrenza);
        Assert.False(nuova.Dati.Completato);
        Assert.Null(nuova.Dati.DataCompletamento);
        Assert.Empty(nuova.Documenti); // i documenti restano alla cartella completata
        Assert.Equal("Fiscale", nuova.NomeArea);
        Assert.Equal(1, create);

        // L'originale è completata e ha ancora i suoi documenti.
        var vecchia = (await _a.Servizio.CaricaCartellaAsync(originale.Id))!;
        Assert.True(vecchia.Dati.Completato);
        Assert.Single(vecchia.Documenti);
    }

    [Fact]
    public async Task LaDomandaSuiDocumenti_HaIlNoPreselezionato_ELaPrimaIlSi()
    {
        var form = Form(await CreaAsync(Ricorrenza.Mensile, file: ["modello.pdf", "ricevuta.pdf"]));
        _a.Dialog.RispondiDomande(true, false);

        form.Completato = true;
        await form.AttendiSalvataggioAsync();

        Assert.Equal([true, false], _a.Dialog.DomandePredefinitoSi);
        Assert.Contains("chiedo subito dopo", _a.Dialog.Domande[0]);
        Assert.Contains("i 2 documenti", _a.Dialog.Domande[1]);
    }

    [Fact]
    public async Task RispondendoSiAllaSecondaDomanda_SiCopianoAncheIDocumenti()
    {
        var originale = await CreaAsync(Ricorrenza.Mensile, file: ["modello.pdf", "ricevuta.pdf"]);
        var form = Form(originale);
        _a.Dialog.RispondiDomande(true, true);

        form.Completato = true;
        await form.AttendiSalvataggioAsync();

        var nuova = (await _a.Servizio.CaricaCartellaAsync((await CartelleAsync()).Single(c => c.Id != originale.Id).Id))!;
        Assert.Equal(["modello.pdf", "ricevuta.pdf"], nuova.Documenti.Select(d => d.NomeFile).Order());
        Assert.All(nuova.Documenti, d =>
        {
            Assert.StartsWith(nuova.PercorsoRelativo, d.PercorsoRelativo);
            Assert.True(_a.Files.Esiste(d.PercorsoRelativo));
        });
        Assert.Equal(2, (await _a.Servizio.CaricaCartellaAsync(originale.Id))!.Documenti.Count); // l'originale li conserva
    }

    [Fact]
    public async Task UnaCartellaSenzaDocumenti_NonChiedeDiCopiarli()
    {
        var form = Form(await CreaAsync(Ricorrenza.Mensile));

        form.Completato = true;
        await form.AttendiSalvataggioAsync();

        var domanda = Assert.Single(_a.Dialog.Domande);
        Assert.Contains("i documenti no", domanda);
        Assert.Equal(2, (await CartelleAsync()).Count);
    }

    [Fact]
    public async Task RifiutandoLaCartella_NonSiChiedeDeiDocumenti()
    {
        var form = Form(await CreaAsync(Ricorrenza.Mensile, file: ["modello.pdf"]));
        _a.Dialog.RispostaDomanda = false;

        form.Completato = true;
        await form.AttendiSalvataggioAsync();

        Assert.Single(_a.Dialog.Domande);
        Assert.Single(await CartelleAsync());
    }

    [Fact]
    public async Task UnDocumentoSparitoDalDisco_NonSiCopia_ELoSiDice()
    {
        var originale = await CreaAsync(Ricorrenza.Mensile, file: ["modello.pdf", "sparito.pdf"]);
        File.Delete(_a.Files.PercorsoAssoluto(originale.Documenti.Single(d => d.NomeFile == "sparito.pdf").PercorsoRelativo));
        var form = Form(originale);
        _a.Dialog.RispondiDomande(true, true);

        form.Completato = true;
        await form.AttendiSalvataggioAsync();

        Assert.Contains("anche il documento?", _a.Dialog.Domande[1]);
        Assert.Contains("Un documento non si trova più", _a.Dialog.Domande[1]);
        var nuova = (await _a.Servizio.CaricaCartellaAsync((await CartelleAsync()).Single(c => c.Id != originale.Id).Id))!;
        Assert.Equal("modello.pdf", Assert.Single(nuova.Documenti).NomeFile);
    }

    [Theory]
    [InlineData(Ricorrenza.Trimestrale, "2027-01-16", "ogni 3 mesi")]
    [InlineData(Ricorrenza.Annuale, "2027-10-16", "ogni anno")]
    public async Task LaScadenzaProposta_SegueLaRicorrenzaScelta(Ricorrenza ricorrenza, string attesa, string testo)
    {
        var originale = await CreaAsync(ricorrenza);
        var form = Form(originale);

        form.Completato = true;
        await form.AttendiSalvataggioAsync();

        Assert.Contains(testo, Assert.Single(_a.Dialog.Domande));
        var nuova = (await CartelleAsync()).Single(c => c.Id != originale.Id);
        Assert.Equal(DateOnly.Parse(attesa), (await _a.Servizio.CaricaCartellaAsync(nuova.Id))!.Dati.DataScadenza);
    }

    [Fact]
    public async Task RifiutandoLaProposta_NonSiCreaNulla()
    {
        var form = Form(await CreaAsync(Ricorrenza.Mensile));
        _a.Dialog.RispostaDomanda = false;
        var create = 0;
        form.CartellaSuccessivaCreata += () => create++;

        form.Completato = true;
        await form.AttendiSalvataggioAsync();

        Assert.Single(_a.Dialog.Domande);
        Assert.Single(await CartelleAsync());
        Assert.Equal(0, create);
        Assert.True((await _a.Servizio.CaricaCartellaAsync(form.Id))!.Dati.Completato); // il completamento resta
    }

    [Fact]
    public async Task UnaCartellaCheNonSiRipete_NonProponeNulla()
    {
        var form = Form(await CreaAsync(Ricorrenza.Nessuna));

        form.Completato = true;
        await form.AttendiSalvataggioAsync();

        Assert.Empty(_a.Dialog.Domande);
        Assert.Single(await CartelleAsync());
    }

    [Fact]
    public async Task TogliendoLaSpuntaDiCompletato_NonProponeNulla()
    {
        var originale = await CreaAsync(Ricorrenza.Mensile);
        await _a.Servizio.AggiornaCartellaAsync(originale.Id, originale.Dati with { Completato = true, DataCompletamento = Scadenza });
        var form = Form((await _a.Servizio.CaricaCartellaAsync(originale.Id))!);

        form.Completato = false;
        await form.AttendiSalvataggioAsync();

        Assert.Empty(_a.Dialog.Domande);
        Assert.Single(await CartelleAsync());
    }

    [Fact]
    public async Task UnaCartellaGiaCompletataAllApertura_NonProponeNulla()
    {
        var originale = await CreaAsync(Ricorrenza.Mensile);
        var completata = await _a.Servizio.AggiornaCartellaAsync(originale.Id, originale.Dati with { Completato = true, DataCompletamento = Scadenza });
        var form = Form(completata);

        form.Descrizione = "una nota in più"; // si modifica un'altra cosa
        await form.AttendiSalvataggioAsync();

        Assert.Empty(_a.Dialog.Domande);
    }

    [Fact]
    public async Task SpuntandoEDisattivandoPiuVolte_LaPropostaSiFaUnaSolaVoltaPerLaStessaScadenza()
    {
        var form = Form(await CreaAsync(Ricorrenza.Mensile));
        _a.Dialog.RispostaDomanda = false;

        form.Completato = true;
        await form.AttendiSalvataggioAsync();
        form.Completato = false;
        await form.AttendiSalvataggioAsync();
        form.Completato = true;
        await form.AttendiSalvataggioAsync();

        Assert.Single(_a.Dialog.Domande);
    }

    [Fact]
    public async Task ConUnaNuovaScadenza_LaPropostaSiRifaPerLaNuovaData()
    {
        var form = Form(await CreaAsync(Ricorrenza.Mensile));
        _a.Dialog.RispostaDomanda = false;

        form.Completato = true;
        await form.AttendiSalvataggioAsync();
        form.Completato = false;
        await form.AttendiSalvataggioAsync();
        form.DataScadenza = new DateTime(2026, 12, 20);
        await form.AttendiSalvataggioAsync();
        form.Completato = true;
        await form.AttendiSalvataggioAsync();

        Assert.Equal(2, _a.Dialog.Domande.Count);
        Assert.Contains("20/01/2027", _a.Dialog.Domande[1]);
    }

    [Fact]
    public async Task LaCartellaSuccessiva_PuoRipetersiAncora()
    {
        var originale = await CreaAsync(Ricorrenza.Mensile);
        var form1 = Form(originale);
        form1.Completato = true;
        await form1.AttendiSalvataggioAsync();

        var successiva = (await CartelleAsync()).Single(c => c.Id != originale.Id);
        var form2 = Form((await _a.Servizio.CaricaCartellaAsync(successiva.Id))!);
        form2.Completato = true;
        await form2.AttendiSalvataggioAsync();

        Assert.Equal(3, (await CartelleAsync()).Count);
        Assert.Contains("16/12/2026", _a.Dialog.Domande[1]);
    }

    // ---------- Nell'albero ----------

    [Fact]
    public async Task CompletandoDalFormPrincipale_LAlberoMostraLaNuovaCartella()
    {
        var originale = await CreaAsync(Ricorrenza.Mensile);
        var vm = new MainViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell,
            new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false });
        await vm.InizializzaAsync();
        var area = vm.Radici.Single().Aree.Single();
        area.Figli.Single().IsSelected = true;
        await vm.CaricamentoFormCompletato;

        vm.FormCartella!.Completato = true;
        await vm.FormCartella.AttendiSalvataggioAsync();

        // La rilettura dell'albero parte da sola, in background.
        for (var i = 0; i < 100 && vm.Radici.Single().Aree.Single().Figli.Count < 2; i++)
            await Task.Delay(50);

        var figli = vm.Radici.Single().Aree.Single().Figli;
        Assert.Equal(2, figli.Count);
        Assert.All(figli, f => Assert.Equal("F24", f.Nome));
        Assert.Equal(originale.Id, vm.NodoSelezionato!.Id); // resta selezionata la cartella completata
    }
}
