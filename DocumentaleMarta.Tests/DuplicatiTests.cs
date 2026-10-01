using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;

namespace DocumentaleMarta.Tests;

/// <summary>L'avviso quando si allega un file che esiste già nell'archivio (stesso contenuto, anche con un altro nome).</summary>
public class DuplicatiTests : IDisposable
{
    private readonly ArchivioDiProva _a = new();

    public void Dispose() => _a.Dispose();

    /// <summary>Un file fuori dall'archivio con il contenuto indicato.</summary>
    private string Sorgente(string nome, string contenuto) => _a.Tmp.CreaFile(Path.Combine("esterni", nome), contenuto);

    /// <summary>Archivia un file con quel nome e contenuto in una cartella nuova.</summary>
    private async Task<CartellaDettaglio> ArchiviaAsync(string area, string titolo, string nome, string contenuto)
    {
        var areaId = (await _a.Servizio.CaricaAlberoAsync()).FirstOrDefault(x => x.Nome == area)?.Id
                     ?? await _a.Servizio.CreaAreaAsync(area);
        return await _a.Servizio.CreaCartellaConDatiAsync(
            areaId, new DatiCartella(titolo, null, null, false, null), [_a.Tmp.CreaFile(Path.Combine("da-archiviare", Guid.NewGuid().ToString("N"), nome), contenuto)]);
    }

    // ---------- TrovaDuplicatiAsync ----------

    [Fact]
    public async Task UnFileIdenticoAUnoArchiviato_SiTrova_ConDoveSta()
    {
        await ArchiviaAsync("Fatture", "Fattura 123", "fattura.pdf", "contenuto A");

        var duplicati = await _a.Servizio.TrovaDuplicatiAsync([Sorgente("fattura.pdf", "contenuto A")]);

        var trovato = Assert.Single(duplicati);
        Assert.Equal("fattura.pdf", trovato.NomeFile);
        var dove = Assert.Single(trovato.GiaArchiviati);
        Assert.Equal("Fatture", dove.NomeArea);
        Assert.Equal("Fattura 123", dove.TitoloCartella);
    }

    [Fact]
    public async Task StessoContenutoConUnNomeDiverso_EUnDuplicato()
    {
        await ArchiviaAsync("Fatture", "F", "fattura.pdf", "contenuto A");

        var duplicati = await _a.Servizio.TrovaDuplicatiAsync([Sorgente("scansione 0042.pdf", "contenuto A")]);

        Assert.Equal("scansione 0042.pdf", Assert.Single(duplicati).NomeFile);
    }

    [Fact]
    public async Task StessoNomeMaContenutoDiverso_NonEUnDuplicato()
    {
        await ArchiviaAsync("Fatture", "F", "fattura.pdf", "contenuto A");

        Assert.Empty(await _a.Servizio.TrovaDuplicatiAsync([Sorgente("fattura.pdf", "contenuto B")]));
    }

    [Fact]
    public async Task SenzaDocumentiArchiviati_NienteDuplicati() =>
        Assert.Empty(await _a.Servizio.TrovaDuplicatiAsync([Sorgente("a.pdf", "x")]));

    [Fact]
    public async Task ElencoVuoto_NienteDuplicati() =>
        Assert.Empty(await _a.Servizio.TrovaDuplicatiAsync([]));

    [Fact]
    public async Task TraPiuFile_SiTrovanoSoloQuelliGiaArchiviati()
    {
        await ArchiviaAsync("Fatture", "F", "vecchio.pdf", "contenuto vecchio");

        var duplicati = await _a.Servizio.TrovaDuplicatiAsync(
            [Sorgente("nuovo.pdf", "contenuto nuovo"), Sorgente("copia.pdf", "contenuto vecchio"), Sorgente("altro.pdf", "altro ancora")]);

        Assert.Equal(["copia.pdf"], duplicati.Select(d => d.NomeFile));
    }

    [Fact]
    public async Task ConPiuCopieArchiviate_SiElencanoTutte_DallaPiuVecchia()
    {
        await ArchiviaAsync("Fatture", "Prima", "a.pdf", "uguale");
        await ArchiviaAsync("Contratti", "Seconda", "b.pdf", "uguale");

        var trovato = Assert.Single(await _a.Servizio.TrovaDuplicatiAsync([Sorgente("c.pdf", "uguale")]));

        Assert.Equal(["Prima", "Seconda"], trovato.GiaArchiviati.Select(g => g.TitoloCartella));
        Assert.Equal(["Fatture", "Contratti"], trovato.GiaArchiviati.Select(g => g.NomeArea));
    }

    [Fact]
    public async Task UnFileCheNonSiPuoLeggere_SiIgnora_SenzaErrori()
    {
        await ArchiviaAsync("Fatture", "F", "a.pdf", "contenuto");
        var inesistente = _a.Tmp.Combina("non-esiste.pdf");
        var bloccato = Sorgente("bloccato.pdf", "contenuto");
        using var blocco = new FileStream(bloccato, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var duplicati = await _a.Servizio.TrovaDuplicatiAsync([inesistente, bloccato]);

        Assert.Empty(duplicati); // l'errore vero lo darà la copia, non il controllo
    }

    [Fact]
    public async Task UnFileAperto_InUnAltroProgramma_SiConfrontaLoStesso()
    {
        await ArchiviaAsync("Fatture", "F", "a.pdf", "contenuto");
        var aperto = Sorgente("aperto.pdf", "contenuto");
        using var lettore = new FileStream(aperto, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Single(await _a.Servizio.TrovaDuplicatiAsync([aperto]));
    }

    [Fact]
    public async Task UnDocumentoEliminato_NonEPiuUnDuplicato()
    {
        var cartella = await ArchiviaAsync("Fatture", "F", "a.pdf", "contenuto");
        await _a.Servizio.EliminaDocumentoAsync(cartella.Documenti.Single().Id);

        Assert.Empty(await _a.Servizio.TrovaDuplicatiAsync([Sorgente("a.pdf", "contenuto")]));
    }

    // ---------- Testo della finestra ----------

    private static DuplicatoTrovato Dup(string nome, params (string Area, string Cartella)[] dove) =>
        new(@"C:\x\" + nome, nome,
            dove.Select((d, i) => new DocumentoGiaArchiviato(i + 1, nome, i + 1, d.Cartella, d.Area)).ToList());

    [Fact]
    public void Testo_UnSoloFileDuplicato_SiPuoSoloAllegareONo()
    {
        var vm = new DuplicatiViewModel([Dup("fattura.pdf", ("Fatture", "Fattura 123"))], totaleFile: 1);

        Assert.Equal("Il file «fattura.pdf» è già nell'archivio.", vm.Intestazione);
        Assert.Equal("Vuoi allegarlo comunque?", vm.Domanda);
        Assert.False(vm.PuoSaltare);
        Assert.Equal("Non allegare", vm.TestoRifiuto);
        Assert.Equal("Già in: Fatture › Fattura 123", Assert.Single(vm.Righe).Dove);
    }

    [Fact]
    public void Testo_TuttiDuplicati_NonSiOffreDiSaltare()
    {
        var vm = new DuplicatiViewModel([Dup("a.pdf", ("A", "X")), Dup("b.pdf", ("A", "Y"))], totaleFile: 2);

        Assert.Equal("Questi 2 file sono già nell'archivio.", vm.Intestazione);
        Assert.Equal("Vuoi allegarli comunque?", vm.Domanda);
        Assert.False(vm.PuoSaltare);
    }

    [Theory]
    [InlineData(1, 3, "1 dei 3 file scelti è già nell'archivio.")]
    [InlineData(2, 5, "2 dei 5 file scelti sono già nell'archivio.")]
    public void Testo_AlcuniDuplicati_SiOffreDiSaltarli(int duplicati, int totale, string atteso)
    {
        var vm = new DuplicatiViewModel(
            Enumerable.Range(1, duplicati).Select(i => Dup($"f{i}.pdf", ("A", "X"))).ToList(), totale);

        Assert.Equal(atteso, vm.Intestazione);
        Assert.True(vm.PuoSaltare);
        Assert.Equal("Annulla", vm.TestoRifiuto);
    }

    [Fact]
    public void Testo_ConMoltiLuoghi_NeMostraTreERiassumeIlResto()
    {
        var vm = new DuplicatiViewModel(
            [Dup("a.pdf", ("A", "1"), ("A", "2"), ("B", "3"), ("B", "4"), ("C", "5"))], totaleFile: 1);

        Assert.Equal("Già in: A › 1; A › 2; B › 3; e altre 2", vm.Righe.Single().Dove);
    }

    // ---------- Pulsante «Allega» del form della cartella ----------

    private CartellaFormViewModel Form(CartellaDettaglio d) => new(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, d);

    private async Task<int> DocumentiInArchivioAsync() => (await _a.Servizio.CaricaDocumentiAsync(null)).Count;

    [Fact]
    public async Task Form_SenzaDuplicati_NonChiedeNulla()
    {
        var form = Form(await ArchiviaAsync("Fatture", "F", "a.pdf", "uno"));

        _a.Dialog.RispondiFile(Sorgente("b.pdf", "due"));
        await form.AllegaCommand.ExecuteAsync(null);

        Assert.Empty(_a.Dialog.DomandeDuplicati);
        Assert.Equal(2, form.Documenti.Count);
    }

    [Fact]
    public async Task Form_ConUnDuplicato_ChiedeDoveSta_EAllegaComunqueSeCosiSiSceglie()
    {
        var form = Form(await ArchiviaAsync("Fatture", "Fattura 123", "a.pdf", "uguale"));

        _a.Dialog.RispondiFile(Sorgente("copia.pdf", "uguale"));
        _a.Dialog.RispondiDuplicati(SceltaDuplicati.AllegaComunque);
        await form.AllegaCommand.ExecuteAsync(null);

        var (duplicati, totale) = Assert.Single(_a.Dialog.DomandeDuplicati);
        Assert.Equal(1, totale);
        Assert.Equal("Fattura 123", Assert.Single(Assert.Single(duplicati).GiaArchiviati).TitoloCartella);
        Assert.Equal(2, form.Documenti.Count);
        Assert.Equal(2, await DocumentiInArchivioAsync());
    }

    [Fact]
    public async Task Form_ConUnDuplicato_AnnullandoNonSiAllegaNulla_ENonSiCopiaNessunFile()
    {
        var form = Form(await ArchiviaAsync("Fatture", "F", "a.pdf", "uguale"));
        var righeFile = Directory.GetFiles(_a.Fisico("Fatture", "F")).Length;

        _a.Dialog.RispondiFile(Sorgente("copia.pdf", "uguale"));
        _a.Dialog.RispondiDuplicati(SceltaDuplicati.Annulla);
        await form.AllegaCommand.ExecuteAsync(null);

        Assert.Single(form.Documenti);
        Assert.Equal(1, await DocumentiInArchivioAsync());
        Assert.Equal(righeFile, Directory.GetFiles(_a.Fisico("Fatture", "F")).Length);
        Assert.Empty(_a.Dialog.Errori);
    }

    [Fact]
    public async Task Form_SaltandoIDuplicati_SiAllegaSoloIlNuovo()
    {
        var form = Form(await ArchiviaAsync("Fatture", "F", "a.pdf", "uguale"));

        _a.Dialog.RispondiFile(Sorgente("copia.pdf", "uguale"), Sorgente("nuovo.pdf", "diverso"));
        _a.Dialog.RispondiDuplicati(SceltaDuplicati.SaltaDuplicati);
        await form.AllegaCommand.ExecuteAsync(null);

        Assert.Equal(("copia.pdf", 2), (_a.Dialog.DomandeDuplicati.Single().Duplicati.Single().NomeFile, _a.Dialog.DomandeDuplicati.Single().TotaleFile));
        Assert.Equal(["a.pdf", "nuovo.pdf"], form.Documenti.Select(d => d.NomeFile));
    }

    [Fact]
    public async Task Form_FileTrascinatiSulForm_ControllanoIDuplicatiComeIlPulsante()
    {
        var form = Form(await ArchiviaAsync("Fatture", "F", "a.pdf", "uguale"));

        _a.Dialog.RispondiDuplicati(SceltaDuplicati.Annulla);
        await form.AllegaCommand.ExecuteAsync(new[] { Sorgente("trascinato.pdf", "uguale") });

        Assert.Single(form.Documenti);
        Assert.Single(_a.Dialog.DomandeDuplicati);
    }

    // ---------- Finestra «Nuova cartella» e trascinamento sull'albero ----------

    private MainViewModel Principale() => new(
        _a.Servizio, _a.Files, _a.Dialog, _a.Shell, new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false });

    private async Task<MainViewModel> PrincipaleConAreaSelezionataAsync()
    {
        await ArchiviaAsync("Fatture", "Esistente", "a.pdf", "uguale");
        var vm = Principale();
        await vm.InizializzaAsync();
        vm.Radici.Single().Aree.Single().IsSelected = true;
        await vm.CaricamentoElencoCompletato;
        return vm;
    }

    [Fact]
    public async Task NuovaCartella_ConUnDuplicato_AnnullandoLaFinestraSiRiapre_ENulla_SiCrea()
    {
        var vm = await PrincipaleConAreaSelezionataAsync();

        _a.Dialog.RispondiNuovaCartella(m =>
        {
            _a.Dialog.RispondiFile(Sorgente("copia.pdf", "uguale"));
            m.AllegaCommand.Execute(null);
        });
        _a.Dialog.RispondiDuplicati(SceltaDuplicati.Annulla);
        _a.Dialog.RispondiNuovaCartella(null); // alla riapertura l'utente rinuncia
        await vm.NuovaCartellaCommand.ExecuteAsync(null);

        Assert.Equal(2, _a.Dialog.AperturaNuovaCartella);
        Assert.Equal(["a.pdf"], (await _a.Servizio.CaricaDocumentiAsync(null)).Select(d => d.NomeFile));
        Assert.Single((await _a.Servizio.CaricaAlberoAsync()).Single().Cartelle);
    }

    [Fact]
    public async Task NuovaCartella_ConUnDuplicato_AllegandoComunqueSiCreaConTuttiIFile()
    {
        var vm = await PrincipaleConAreaSelezionataAsync();

        _a.Dialog.RispondiNuovaCartella(m =>
        {
            _a.Dialog.RispondiFile(Sorgente("copia.pdf", "uguale"));
            m.AllegaCommand.Execute(null);
        });
        await vm.NuovaCartellaCommand.ExecuteAsync(null); // risposta predefinita: allega comunque

        Assert.Single(_a.Dialog.DomandeDuplicati);
        Assert.Equal(2, (await _a.Servizio.CaricaDocumentiAsync(null)).Count);
    }

    [Fact]
    public async Task NuovaCartella_SaltandoIDuplicati_SiCreaSoloConIFileNuovi()
    {
        var vm = await PrincipaleConAreaSelezionataAsync();

        _a.Dialog.RispondiNuovaCartella(m =>
        {
            _a.Dialog.RispondiFile(Sorgente("copia.pdf", "uguale"), Sorgente("nuovo.pdf", "diverso"));
            m.AllegaCommand.Execute(null);
        });
        _a.Dialog.RispondiDuplicati(SceltaDuplicati.SaltaDuplicati);
        await vm.NuovaCartellaCommand.ExecuteAsync(null);

        var documenti = await _a.Servizio.CaricaDocumentiAsync(null);
        Assert.Equal(["a.pdf", "nuovo.pdf"], documenti.Select(d => d.NomeFile).Order());
    }

    [Fact]
    public async Task FileTrascinatiSuUnaCartellaDellAlbero_ConUnDuplicato_NonSiAllegaSeSiAnnulla()
    {
        var vm = await PrincipaleConAreaSelezionataAsync();
        var cartellaId = vm.Radici.Single().Aree.Single().Figli.Single().Id;

        _a.Dialog.RispondiDuplicati(SceltaDuplicati.Annulla);
        await vm.AllegaATrascinatiAsync(cartellaId, [Sorgente("copia.pdf", "uguale")]);

        Assert.Equal(1, await DocumentiInArchivioAsync());
        Assert.Single(_a.Dialog.DomandeDuplicati);
    }

    [Fact]
    public async Task FileTrascinatiSuUnaCartellaDellAlbero_SenzaDuplicati_NonChiedeNulla()
    {
        var vm = await PrincipaleConAreaSelezionataAsync();
        var cartellaId = vm.Radici.Single().Aree.Single().Figli.Single().Id;

        await vm.AllegaATrascinatiAsync(cartellaId, [Sorgente("nuovo.pdf", "diverso")]);

        Assert.Equal(2, await DocumentiInArchivioAsync());
        Assert.Empty(_a.Dialog.DomandeDuplicati);
    }
}
