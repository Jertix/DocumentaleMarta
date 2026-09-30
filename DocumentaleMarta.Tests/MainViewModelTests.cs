using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Data;

namespace DocumentaleMarta.Tests;

public class MainViewModelTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();
    private readonly string _radice;
    private readonly AppDbContextFactory _factory;
    private readonly ArchivioFileService _files;
    private readonly ArchivioService _archivio;
    private readonly FintoDialogService _dialog = new();
    private readonly FintoShellService _shell = new();
    private readonly MainViewModel _vm;

    public MainViewModelTests()
    {
        _radice = _tmp.Combina("Documentale");
        var percorsoDb = ArchivioDatabase.Inizializza(_radice);
        _factory = new AppDbContextFactory(ArchivioDatabase.CreaOpzioni(percorsoDb));
        _files = new ArchivioFileService(_radice, usaCestino: false);
        _archivio = new ArchivioService(_factory, _files);
        _vm = new MainViewModel(_archivio, _files, _dialog, _shell, new ImpostazioniApp { PercorsoRadice = _radice });
    }

    public void Dispose() => _tmp.Dispose();

    // L'albero viene ricreato a ogni modifica: i nodi si cercano sempre per nome, mai riusando quelli vecchi.
    private NodoAlberoViewModel Radice => _vm.Radici.Single();
    private NodoAlberoViewModel Area(string nome) => Radice.Figli.Single(a => a.Nome == nome);
    private NodoAlberoViewModel Cartella(string area, string titolo) => Area(area).Figli.First(c => c.Nome == titolo);
    private bool ExistsArea(string nome) => Radice.Figli.Any(a => a.Nome == nome);

    private async Task CreaAreaAsync(string nome)
    {
        _dialog.RispondiTesto(nome);
        await _vm.NuovaAreaCommand.ExecuteAsync(null);
    }

    private async Task CreaCartellaAsync(string area, string titolo)
    {
        Area(area).IsSelected = true;
        _dialog.RispondiNuovaCartella(m => m.Titolo = titolo);
        await _vm.NuovaCartellaCommand.ExecuteAsync(null);
        await _vm.CaricamentoFormCompletato;
    }

    private string Fisico(params string[] parti) => _files.PercorsoAssoluto(Path.Combine(parti));

    // ---------- Avvio ----------

    [Fact]
    public async Task Inizializza_MostraLaRadiceSelezionataEAperta()
    {
        await _vm.InizializzaAsync();

        Assert.Equal("Tutti i documenti", Radice.Nome);
        Assert.True(Radice.IsExpanded);
        Assert.Same(Radice, _vm.NodoSelezionato);
        Assert.True(_vm.RadiceSelezionata);
        Assert.Empty(Radice.Figli);
        Assert.Equal("Archivio", _vm.TipoDettaglio);
        Assert.Equal(_radice, _vm.PercorsoDettaglio.TrimEnd('\\'));
    }

    [Fact]
    public async Task Inizializza_CaricaAreeECartelleEsistenti_ConIContatori()
    {
        var areaId = await _archivio.CreaAreaAsync("Fatture");
        var cartellaId = await _archivio.CreaCartellaAsync(areaId, "Fattura 1");
        await _archivio.CreaCartellaAsync(areaId, "Fattura 2");
        using (var db = _factory.CreateDbContext())
        {
            db.Documenti.Add(new Documento { CartellaId = cartellaId, NomeFile = "a.pdf", PercorsoRelativo = "x", Hash = "H" });
            db.SaveChanges();
        }

        await _vm.InizializzaAsync();

        var fatture = Area("Fatture");
        Assert.Equal(["Fattura 1", "Fattura 2"], fatture.Figli.Select(c => c.Nome));
        Assert.Equal(2, fatture.NumeroCartelle);
        Assert.Equal(1, fatture.NumeroDocumenti);
        Assert.Equal(1, Radice.NumeroDocumenti);
        Assert.Equal(TipoNodo.Cartella, fatture.Figli[0].Tipo);
        Assert.Same(fatture, fatture.Figli[0].Padre);
    }

    [Fact]
    public void TestoAzienda_UsaIDatiDelleImpostazioni() =>
        Assert.Equal(
            "METAL PROJET DI TEDDE PAOLO E SORRENTINO LUCA SNC  -  C.F. 01790610990  -  P.IVA 01790610990 (IT)  -  VIA DELLE GINESTRE, 102 R - 16137 GENOVA (GE - IT)",
            _vm.TestoAzienda);

    // ---------- Creazione ----------

    [Fact]
    public async Task NuovaArea_LaCreaSulDiscoENellAlbero_ELaSeleziona()
    {
        await _vm.InizializzaAsync();

        await CreaAreaAsync("Fatture");

        Assert.Same(Area("Fatture"), _vm.NodoSelezionato);
        Assert.True(_vm.AreaSelezionata);
        Assert.True(Directory.Exists(Fisico("Fatture")));
        Assert.Equal("Area", _vm.TipoDettaglio);
        Assert.Equal("Fatture", _vm.TitoloDettaglio);
    }

    [Fact]
    public async Task NuovaArea_Annullata_NonFaNulla()
    {
        await _vm.InizializzaAsync();
        _dialog.RispondiTesto(null);

        await _vm.NuovaAreaCommand.ExecuteAsync(null);

        Assert.Empty(Radice.Figli);
        Assert.Empty(_dialog.Errori);
    }

    [Theory]
    [InlineData("Fatture")]
    [InlineData("fatture")]
    [InlineData("  FATTURE ")]
    public async Task NuovaArea_NomeGiaUsato_ValidatoreLoRifiuta(string secondo)
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");

        await CreaAreaAsync(secondo);

        Assert.Single(Radice.Figli);
        Assert.Contains("Esiste già", Assert.Single(_dialog.ErroriValidazione));
    }

    [Fact]
    public async Task NuovaArea_NomeVuoto_ValidatoreLoRifiuta()
    {
        await _vm.InizializzaAsync();

        await CreaAreaAsync("   ");

        Assert.Empty(Radice.Figli);
        Assert.Single(_dialog.ErroriValidazione);
    }

    [Fact]
    public async Task NuovaCartella_DaUnArea_LaCreaDentro_ApreLAreaESelezionaIlNuovoNodo()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");

        await CreaCartellaAsync("Fatture", "Fattura 1");

        Assert.Same(Cartella("Fatture", "Fattura 1"), _vm.NodoSelezionato);
        Assert.True(Area("Fatture").IsExpanded);
        Assert.True(Directory.Exists(Fisico("Fatture", "Fattura 1")));
        Assert.Equal("Fatture", _dialog.UltimaNuovaCartella!.NomeArea);
    }

    [Fact]
    public async Task NuovaCartella_DaUnaCartella_LaCreaNellaStessaArea()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        await CreaCartellaAsync("Fatture", "Prima");
        Cartella("Fatture", "Prima").IsSelected = true;

        _dialog.RispondiNuovaCartella(m => m.Titolo = "Seconda");
        await _vm.NuovaCartellaCommand.ExecuteAsync(null);

        Assert.Equal(["Prima", "Seconda"], Area("Fatture").Figli.Select(c => c.Nome));
    }

    [Fact]
    public async Task NuovaCartella_StessoTitoloDiUnaEsistente_Ammesso()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Scadenze");
        await CreaCartellaAsync("Scadenze", "F24");

        await CreaCartellaAsync("Scadenze", "F24");

        Assert.Equal(2, Area("Scadenze").Figli.Count(c => c.Nome == "F24"));
        Assert.Empty(_dialog.ErroriValidazione);
        Assert.Equal(2, Directory.GetDirectories(Fisico("Scadenze")).Length);
    }

    [Fact]
    public async Task NuovaCartella_ConFileEDati_LiCopia_LaSeleziona_EMostraIlForm()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        var f1 = _tmp.CreaFile(Path.Combine("src", "Fattura 123.pdf"), "uno");
        var f2 = _tmp.CreaFile(Path.Combine("src", "Fattura 124.pdf"), "due");
        Area("Fatture").IsSelected = true;

        _dialog.RispondiNuovaCartella(m =>
        {
            m.Descrizione = "Fatture di settembre";
            m.DataScadenza = new DateTime(2026, 10, 31);
            _dialog.RispondiFile(f1, f2);
            m.AllegaCommand.Execute(null);
        });
        await _vm.NuovaCartellaCommand.ExecuteAsync(null);
        await _vm.CaricamentoFormCompletato;

        var cartella = Cartella("Fatture", "Fattura 123 (+1)"); // titolo automatico
        Assert.Same(cartella, _vm.NodoSelezionato);
        Assert.Equal(2, cartella.NumeroDocumenti);
        Assert.Equal(2, Radice.NumeroDocumenti);
        Assert.True(File.Exists(Fisico("Fatture", "Fattura 123 (+1)", "Fattura 123.pdf")));
        Assert.True(File.Exists(f1));

        var form = _vm.FormCartella!;
        Assert.Equal("Fatture di settembre", form.Descrizione);
        Assert.Equal(new DateTime(2026, 10, 31), form.DataScadenza);
        Assert.Equal(["Fattura 123.pdf", "Fattura 124.pdf"], form.Documenti.Select(d => d.NomeFile));
    }

    [Fact]
    public async Task NuovaCartella_Annullata_NonCreaNulla()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        _dialog.RispondiNuovaCartella(null);

        await _vm.NuovaCartellaCommand.ExecuteAsync(null);

        Assert.Empty(Area("Fatture").Figli);
        Assert.Empty(Directory.GetDirectories(Fisico("Fatture")));
        Assert.Empty(_dialog.Errori);
    }

    [Fact]
    public async Task NuovaCartella_SenzaTitoloESenzaFile_LaFinestraNonSiChiude()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");

        _dialog.RispondiNuovaCartella(m => { /* l'utente preme Crea senza compilare nulla */ });
        await _vm.NuovaCartellaCommand.ExecuteAsync(null);

        Assert.Contains("titolo", _dialog.UltimaNuovaCartella!.Errore);
        Assert.Empty(Area("Fatture").Figli);
    }

    [Fact]
    public async Task NuovaCartella_CreazioneFallita_MostraErrore_RiapreLaFinestraConGliStessiDati_ENonCreaNulla()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        Area("Fatture").IsSelected = true;
        var mancante = _tmp.Combina("manca.pdf");

        _dialog.RispondiNuovaCartella(m =>
        {
            m.Titolo = "Pratica";
            _dialog.RispondiFile(mancante);
            m.AllegaCommand.Execute(null);
        });
        _dialog.RispondiNuovaCartella(null); // alla riapertura l'utente rinuncia
        var aperture = _dialog.AperturaNuovaCartella;

        await _vm.NuovaCartellaCommand.ExecuteAsync(null);

        Assert.Equal(aperture + 2, _dialog.AperturaNuovaCartella);
        Assert.Contains("Non è stato creato nulla", Assert.Single(_dialog.Errori));
        Assert.Equal("Pratica", _dialog.UltimaNuovaCartella!.Titolo); // i dati sono ancora lì
        Assert.Single(_dialog.UltimaNuovaCartella.Allegati);
        Assert.Empty(Area("Fatture").Figli);
        Assert.Empty(Directory.GetDirectories(Fisico("Fatture")));
    }

    // ---------- Form della cartella nel pannello di destra ----------

    [Fact]
    public async Task SelezionandoUnaCartella_SiCaricaIlForm_SelezionandoUnAreaSparisce()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        await CreaCartellaAsync("Fatture", "Pratica");

        Assert.NotNull(_vm.FormCartella);
        Assert.Equal("Pratica", _vm.FormCartella!.Titolo);
        Assert.False(_vm.RiepilogoVisibile);

        Area("Fatture").IsSelected = true;

        Assert.Null(_vm.FormCartella);
        Assert.True(_vm.RiepilogoVisibile);
    }

    [Fact]
    public async Task CambiandoSelezioneRapidamente_IlFormEQuelloDellUltimaCartella()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("A");
        await CreaCartellaAsync("A", "Uno");
        await CreaCartellaAsync("A", "Due");

        Cartella("A", "Uno").IsSelected = true;
        Cartella("A", "Due").IsSelected = true;
        await _vm.CaricamentoFormCompletato;

        Assert.Equal("Due", _vm.FormCartella!.Titolo);
    }

    [Fact]
    public async Task CambiandoIlTitoloDalForm_IlNodoSiRinominaSulPosto_SenzaPerdereLaSelezione()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        await CreaCartellaAsync("Fatture", "Bravo");
        await CreaCartellaAsync("Fatture", "Charlie");
        var nodo = Cartella("Fatture", "Bravo");
        nodo.IsSelected = true;
        await _vm.CaricamentoFormCompletato;

        _vm.FormCartella!.Titolo = "Zulu";
        await _vm.FormCartella.AttendiSalvataggioAsync();

        Assert.Equal("Zulu", nodo.Nome);
        Assert.Same(nodo, _vm.NodoSelezionato);                       // stesso oggetto: l'albero non è stato ricostruito
        Assert.Equal(["Charlie", "Zulu"], Area("Fatture").Figli.Select(c => c.Nome)); // e si è rimesso in ordine
        Assert.Equal(Path.Combine("Fatture", "Zulu"), nodo.PercorsoRelativo);
        Assert.True(Directory.Exists(Fisico("Fatture", "Zulu")));

        _vm.ApriInEsploraCommand.Execute(null);
        Assert.Equal([Fisico("Fatture", "Zulu")], _shell.CartelleAperte);
    }

    [Fact]
    public async Task AllegandoEdEliminandoDocumentiDalForm_IContatoriDellAlberoSiAggiornano()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        await CreaCartellaAsync("Fatture", "Pratica");
        await _vm.CaricamentoFormCompletato;

        _dialog.RispondiFile(_tmp.CreaFile("s/a.pdf"), _tmp.CreaFile("s/b.pdf"));
        await _vm.FormCartella!.AllegaCommand.ExecuteAsync(null);

        Assert.Equal(2, Cartella("Fatture", "Pratica").NumeroDocumenti);
        Assert.Equal(2, Area("Fatture").NumeroDocumenti);
        Assert.Equal(2, Radice.NumeroDocumenti);

        await _vm.FormCartella.Documenti[0].EliminaCommand.ExecuteAsync(null);

        Assert.Equal(1, Cartella("Fatture", "Pratica").NumeroDocumenti);
        Assert.Equal(1, Radice.NumeroDocumenti);
    }

    [Fact]
    public async Task IlFormSopravvive_AllaRinominaDallAlbero_ERiflettePercorsiAggiornati()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        await CreaCartellaAsync("Fatture", "Vecchia");
        await _vm.CaricamentoFormCompletato;
        _dialog.RispondiFile(_tmp.CreaFile("s/a.pdf"));
        await _vm.FormCartella!.AllegaCommand.ExecuteAsync(null);

        _dialog.RispondiTesto("Nuova");
        await _vm.RinominaCommand.ExecuteAsync(null);
        await _vm.CaricamentoFormCompletato;

        Assert.Equal("Nuova", _vm.FormCartella!.Titolo);
        Assert.Equal(Path.Combine("Fatture", "Nuova", "a.pdf"), _vm.FormCartella.Documenti.Single().PercorsoRelativo);
        Assert.False(_vm.FormCartella.Documenti.Single().FileMancante);
    }

    [Fact]
    public async Task CartellaEliminataAltrove_ILFormChiedeDiRicaricareLAlbero()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        await CreaCartellaAsync("Fatture", "Pratica");
        await _vm.CaricamentoFormCompletato;
        await _archivio.EliminaCartellaAsync(Cartella("Fatture", "Pratica").Id);

        var form = _vm.FormCartella!;
        form.Descrizione = "x";
        await form.AttendiSalvataggioAsync();

        Assert.Empty(Area("Fatture").Figli);
        Assert.Same(Radice, _vm.NodoSelezionato); // la cartella selezionata non esiste più: si ripiega sulla radice
        Assert.Null(_vm.FormCartella);
    }

    // ---------- Rinomina ----------

    [Fact]
    public async Task RinominaArea_ProponeIlNomeAttuale_AggiornaAlberoEConservaSelezioneEdEspansione()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Vecchia");
        await CreaCartellaAsync("Vecchia", "Pratica");
        Area("Vecchia").IsSelected = true;

        _dialog.RispondiTesto("Nuova");
        await _vm.RinominaCommand.ExecuteAsync(null);

        Assert.Equal("Vecchia", _dialog.ValoreInizialeUltimo);
        Assert.False(ExistsArea("Vecchia"));
        Assert.Same(Area("Nuova"), _vm.NodoSelezionato);
        Assert.True(Area("Nuova").IsExpanded);
        Assert.Equal(["Pratica"], Area("Nuova").Figli.Select(c => c.Nome));
        Assert.True(Directory.Exists(Fisico("Nuova", "Pratica")));
        Assert.False(Directory.Exists(Fisico("Vecchia")));
    }

    [Fact]
    public async Task RinominaArea_ConIlProprioNomeInMaiuscolo_NonEUnDuplicato()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("inps");

        _dialog.RispondiTesto("INPS");
        await _vm.RinominaCommand.ExecuteAsync(null);

        Assert.Empty(_dialog.ErroriValidazione);
        Assert.True(ExistsArea("INPS"));
    }

    [Fact]
    public async Task RinominaArea_ConNomeDiUnAltraArea_ValidatoreLaRifiuta()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("A");
        await CreaAreaAsync("B");

        _dialog.RispondiTesto("A");
        await _vm.RinominaCommand.ExecuteAsync(null);

        Assert.Single(_dialog.ErroriValidazione);
        Assert.True(ExistsArea("B"));
    }

    [Fact]
    public async Task RinominaCartella_AggiornaTitoloECartellaFisica()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        await CreaCartellaAsync("Fatture", "Vecchia");

        _dialog.RispondiTesto("Nuova");
        await _vm.RinominaCommand.ExecuteAsync(null);

        Assert.Equal("Nuova", _vm.NodoSelezionato!.Nome);
        Assert.Equal(TipoNodo.Cartella, _vm.NodoSelezionato.Tipo);
        Assert.True(Directory.Exists(Fisico("Fatture", "Nuova")));
        Assert.False(Directory.Exists(Fisico("Fatture", "Vecchia")));
    }

    [Fact]
    public async Task SullaRadice_RinominaEEliminaNonSonoDisponibili()
    {
        await _vm.InizializzaAsync();

        Assert.False(_vm.RinominaCommand.CanExecute(null));
        Assert.False(_vm.EliminaCommand.CanExecute(null));
    }

    // ---------- Eliminazione ----------

    [Fact]
    public async Task EliminaArea_ChiedeConfermaElencandoIlContenuto_EPoiSelezionaLaRadice()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        await CreaCartellaAsync("Fatture", "Fattura 1");
        using (var db = _factory.CreateDbContext())
        {
            var cartellaId = db.Cartelle.Single().Id;
            db.Documenti.Add(new Documento { CartellaId = cartellaId, NomeFile = "a.pdf", PercorsoRelativo = "x", Hash = "H" });
            db.SaveChanges();
        }
        await _vm.InizializzaAsync();
        Area("Fatture").IsSelected = true;

        await _vm.EliminaCommand.ExecuteAsync(null);

        var messaggio = Assert.Single(_dialog.Conferme);
        Assert.Contains("«Fatture»", messaggio);
        Assert.Contains("1 cartella e 1 documento", messaggio);
        Assert.Contains("Cestino", messaggio);
        Assert.Empty(Radice.Figli);
        Assert.Same(Radice, _vm.NodoSelezionato);
        Assert.False(Directory.Exists(Fisico("Fatture")));
    }

    [Fact]
    public async Task EliminaArea_VuotaConfermaSenzaElencoDelContenuto()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Vuota");

        await _vm.EliminaCommand.ExecuteAsync(null);

        Assert.DoesNotContain("Verranno eliminati anche", Assert.Single(_dialog.Conferme));
    }

    [Fact]
    public async Task EliminaArea_ConfermaNegata_NonEliminaNulla()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        _dialog.RispostaConferma = false;

        await _vm.EliminaCommand.ExecuteAsync(null);

        Assert.True(ExistsArea("Fatture"));
        Assert.True(Directory.Exists(Fisico("Fatture")));
    }

    [Fact]
    public async Task EliminaCartella_SelezionaLAreaPadre()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        await CreaCartellaAsync("Fatture", "Fattura 1");

        await _vm.EliminaCommand.ExecuteAsync(null);

        Assert.Same(Area("Fatture"), _vm.NodoSelezionato);
        Assert.Empty(Area("Fatture").Figli);
        Assert.Contains("la cartella «Fattura 1»", Assert.Single(_dialog.Conferme));
    }

    [Fact]
    public async Task Elimina_FileAperto_MostraErrore_EL_AlberoRestaInvariato()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        await CreaCartellaAsync("Fatture", "Pratica");
        var fisico = Fisico("Fatture", "Pratica", "a.txt");
        File.WriteAllText(fisico, "x");
        Area("Fatture").IsSelected = true;

        using (new FileStream(fisico, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await _vm.EliminaCommand.ExecuteAsync(null);
        }

        Assert.Contains("aperti in un altro programma", Assert.Single(_dialog.Errori));
        Assert.Equal(["Pratica"], Area("Fatture").Figli.Select(c => c.Nome));
    }

    [Fact]
    public async Task ElementoGiaEliminatoAltrove_MostraErroreERicaricaLAlbero()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        await _archivio.EliminaAreaAsync(Area("Fatture").Id); // sparisce dietro le spalle dell'interfaccia

        _dialog.RispondiTesto("Nuova");
        await _vm.RinominaCommand.ExecuteAsync(null);

        Assert.Contains("non esiste più", Assert.Single(_dialog.Errori));
        Assert.Empty(Radice.Figli);
        Assert.Same(Radice, _vm.NodoSelezionato);
    }

    // ---------- Esplora file e abilitazione dei comandi ----------

    [Fact]
    public async Task ApriInEsplora_ApreLaCartellaDelNodoSelezionato()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");

        _vm.ApriInEsploraCommand.Execute(null);

        Assert.Equal([Fisico("Fatture")], _shell.CartelleAperte);
    }

    [Fact]
    public async Task ApriInEsplora_DallaRadice_ApreLaCartellaPrincipale()
    {
        await _vm.InizializzaAsync();

        _vm.ApriInEsploraCommand.Execute(null);

        Assert.Equal(_radice, _shell.CartelleAperte.Single().TrimEnd('\\'));
    }

    [Fact]
    public async Task ApriInEsplora_CartellaSparitaDalDisco_MostraErrore()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        Directory.Delete(Fisico("Fatture"));

        _vm.ApriInEsploraCommand.Execute(null);

        Assert.Empty(_shell.CartelleAperte);
        Assert.Contains("non esiste sul disco", Assert.Single(_dialog.Errori));
    }

    [Fact]
    public async Task AbilitazioneComandi_DipendeDalTipoDiNodoSelezionato()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("Fatture");
        await CreaCartellaAsync("Fatture", "Pratica");

        Radice.IsSelected = true;
        Assert.Equal((false, false, false, true), Stato());
        Assert.True(_vm.RadiceSelezionata);

        Area("Fatture").IsSelected = true;
        Assert.Equal((true, true, true, true), Stato());
        Assert.True(_vm.AreaSelezionata);

        Cartella("Fatture", "Pratica").IsSelected = true;
        Assert.Equal((true, true, true, true), Stato());
        Assert.False(_vm.AreaSelezionata);

        (bool NuovaCartella, bool Rinomina, bool Elimina, bool Apri) Stato() =>
            (_vm.NuovaCartellaCommand.CanExecute(null), _vm.RinominaCommand.CanExecute(null),
             _vm.EliminaCommand.CanExecute(null), _vm.ApriInEsploraCommand.CanExecute(null));
    }

    [Fact]
    public async Task SelezionandoUnNodo_IlVecchioNodoSiDeseleziona()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("A");
        await CreaAreaAsync("B");

        Area("A").IsSelected = true;

        Assert.Same(Area("A"), _vm.NodoSelezionato);
        Assert.False(Area("B").IsSelected);
    }

    [Fact]
    public async Task RamiAperti_RestanoApertiDopoOgniModifica()
    {
        await _vm.InizializzaAsync();
        await CreaAreaAsync("A");
        await CreaCartellaAsync("A", "C1");
        await CreaAreaAsync("B");
        Area("A").IsExpanded = true;
        Area("B").IsExpanded = false;

        Area("B").IsSelected = true;
        _dialog.RispondiTesto("B2");
        await _vm.RinominaCommand.ExecuteAsync(null);

        Assert.True(Area("A").IsExpanded);
        Assert.True(Radice.IsExpanded);
    }
}
