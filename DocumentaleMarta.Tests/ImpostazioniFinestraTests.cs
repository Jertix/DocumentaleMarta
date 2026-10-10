using System.Text.Json;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

public class ImpostazioniCopiaTests
{
    /// <summary>Impostazioni con ogni campo diverso dal valore di partenza.</summary>
    private static ImpostazioniApp Piena() => new()
    {
        Azienda = new DatiAzienda
        {
            RagioneSociale = "Altra ditta srl", CodiceFiscale = "CF1", PartitaIva = "PI1", Indirizzo = "Via Roma 1", Descrizione = "Fabbri"
        },
        PercorsoRadice = @"D:\Archivio",
        NomeRadice = "Archivio generale",
        AvvisiAttivi = false,
        RiepilogoAvvio = false,
        SogliaArancioneGiorni = 45,
        SogliaRossaGiorni = 10,
        CartellaBackup = @"E:\Backup",
        BackupPromemoriaGiorni = 14,
        UltimoBackup = new DateTime(2026, 9, 1, 12, 0, 0),
        RigheAnteprimaTesto = 250,
        RicercaXmlAttiva = true,
        ModoXml = ModoRicercaXml.TuttoIlFile,
        AnimazioniAttive = false
    };

    [Fact]
    public void Clona_EUnaCopiaIndipendente_AncheNellaDittaInterna()
    {
        var originale = Piena();

        var copia = originale.Clona();
        copia.Azienda.RagioneSociale = "Cambiata";
        copia.SogliaRossaGiorni = 1;

        Assert.Equal("Altra ditta srl", originale.Azienda.RagioneSociale);
        Assert.Equal(10, originale.SogliaRossaGiorni);
        Assert.NotSame(originale.Azienda, copia.Azienda);
    }

    [Fact]
    public void Clona_ConservaTuttiICampi() =>
        Assert.Equal(JsonSerializer.Serialize(Piena()), JsonSerializer.Serialize(Piena().Clona()));

    [Fact]
    public void CopiaDa_CopiaTuttiICampi_EL_OggettoRestaLoStesso()
    {
        var vivo = new ImpostazioniApp();
        var riferimento = vivo;
        var nuove = Piena();

        vivo.CopiaDa(nuove);

        Assert.Same(riferimento, vivo);
        Assert.Equal(JsonSerializer.Serialize(nuove), JsonSerializer.Serialize(vivo));
    }

    [Fact]
    public void CopiaDa_NonCondivideLaDittaInternaConLOrigine()
    {
        var vivo = new ImpostazioniApp();
        var nuove = Piena();

        vivo.CopiaDa(nuove);
        nuove.Azienda.RagioneSociale = "Cambiata dopo";

        Assert.Equal("Altra ditta srl", vivo.Azienda.RagioneSociale);
    }
}

public class ImpostazioniViewModelTests
{
    private static ImpostazioniViewModel Nuovo(ImpostazioniApp? impostazioni = null) =>
        new(impostazioni ?? new ImpostazioniApp(), @"C:\x\impostazioni.json");

    [Fact]
    public void Partenza_CopiaIValoriAttuali_ESiPuoSalvare()
    {
        var vm = Nuovo();

        Assert.Equal("METAL PROJET DI TEDDE PAOLO E SORRENTINO LUCA SNC", vm.RagioneSociale);
        Assert.Equal("01790610990", vm.CodiceFiscale);
        Assert.Equal("Tutti i documenti", vm.NomeRadice);
        Assert.True(vm.AvvisiAttivi);
        Assert.True(vm.RiepilogoAvvio);
        Assert.Equal("8", vm.SogliaArancione);
        Assert.Equal("4", vm.SogliaRossa);
        Assert.Equal(@"C:\Documentale", vm.PercorsoRadice);
        Assert.Equal(@"C:\x\impostazioni.json", vm.PercorsoFile);
        Assert.Equal("", vm.Errore);
        Assert.True(vm.PuoSalvare);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("-3")]
    [InlineData("3,5")]
    [InlineData("12 giorni")]
    public void SogliaArancioneNonNumerica_Errore(string testo)
    {
        var vm = Nuovo();

        vm.SogliaArancione = testo;

        Assert.Contains("arancione", vm.Errore);
        Assert.Contains("numero intero", vm.Errore);
        Assert.False(vm.PuoSalvare);
    }

    [Fact]
    public void SogliaRossaNonNumerica_Errore()
    {
        var vm = Nuovo();

        vm.SogliaRossa = "sette";

        Assert.Contains("rossa", vm.Errore);
        Assert.False(vm.PuoSalvare);
    }

    [Theory]
    [InlineData("7", "7")]
    [InlineData("5", "7")]
    [InlineData("10", "10")]
    public void SogliaArancioneNonMaggioreDellaRossa_Errore(string arancione, string rossa)
    {
        var vm = Nuovo();

        vm.SogliaArancione = arancione;
        vm.SogliaRossa = rossa;

        Assert.Equal("La soglia arancione deve essere maggiore della soglia rossa.", vm.Errore);
        Assert.False(vm.PuoSalvare);
    }

    [Fact]
    public void RigheAnteprimaTesto_PartonoDa100_EPassanoAlleImpostazioniCostruite()
    {
        var vm = Nuovo();
        Assert.Equal("100", vm.RigheAnteprimaTesto);

        vm.RigheAnteprimaTesto = " 250 ";

        Assert.True(vm.PuoSalvare);
        Assert.Equal(250, vm.Costruisci().RigheAnteprimaTesto);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("-5")]
    [InlineData("50,5")]
    public void RigheAnteprimaTestoNonNumeriche_Errore(string testo)
    {
        var vm = Nuovo();

        vm.RigheAnteprimaTesto = testo;

        Assert.Equal("Le righe dell'anteprima di testo devono essere un numero intero.", vm.Errore);
        Assert.False(vm.PuoSalvare);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("9")]
    [InlineData("1001")]
    [InlineData("5000")]
    public void RigheAnteprimaTestoFuoriDaiLimiti_Errore(string testo)
    {
        var vm = Nuovo();

        vm.RigheAnteprimaTesto = testo;

        Assert.Contains("tra 10 e 1000", vm.Errore);
        Assert.False(vm.PuoSalvare);
    }

    [Theory]
    [InlineData("10")]
    [InlineData("1000")]
    public void RigheAnteprimaTestoAgliEstremi_Valide(string testo)
    {
        var vm = Nuovo();

        vm.RigheAnteprimaTesto = testo;

        Assert.True(vm.PuoSalvare);
    }

    [Fact]
    public void NomeDellaRadiceVuoto_Errore()
    {
        var vm = Nuovo();

        vm.NomeRadice = "   ";

        Assert.False(vm.PuoSalvare);
        Assert.Contains("radice", vm.Errore);
    }

    [Theory]
    [InlineData(" 12 ", "3")]
    [InlineData("30", "0")]
    [InlineData("1000", "999")]
    public void SoglieValide_ConSpaziAiLati_OppureZero(string arancione, string rossa)
    {
        var vm = Nuovo();

        vm.SogliaArancione = arancione;
        vm.SogliaRossa = rossa;

        Assert.True(vm.PuoSalvare);
        var costruite = vm.Costruisci();
        Assert.Equal(int.Parse(arancione.Trim()), costruite.SogliaArancioneGiorni);
        Assert.Equal(int.Parse(rossa.Trim()), costruite.SogliaRossaGiorni);
    }

    [Fact]
    public void Costruisci_PortaIValoriScelti_LiPuliscePerSpazi_EConservaIlResto()
    {
        var originale = new ImpostazioniApp
        {
            PercorsoRadice = @"D:\Archivio", CartellaBackup = @"E:\Backup", BackupPromemoriaGiorni = 14,
            UltimoBackup = new DateTime(2026, 9, 1)
        };
        var vm = Nuovo(originale);

        vm.RagioneSociale = "  Nuova ditta  ";
        vm.CodiceFiscale = "CF9";
        vm.PartitaIva = "PI9";
        vm.Indirizzo = " Via Nuova 5 ";
        vm.Descrizione = "  Carpenteria leggera.  ";
        vm.NomeRadice = " Archivio ";
        vm.AvvisiAttivi = false;
        vm.RiepilogoAvvio = false;
        vm.SogliaArancione = "60";
        vm.SogliaRossa = "14";

        var risultato = vm.Costruisci();

        Assert.Equal("Nuova ditta", risultato.Azienda.RagioneSociale);
        Assert.Equal("Via Nuova 5", risultato.Azienda.Indirizzo);
        Assert.Equal("Carpenteria leggera.", risultato.Azienda.Descrizione);
        Assert.Equal("Archivio", risultato.NomeRadice);
        Assert.False(risultato.AvvisiAttivi);
        Assert.False(risultato.RiepilogoAvvio);
        Assert.Equal(60, risultato.SogliaArancioneGiorni);
        Assert.Equal(14, risultato.SogliaRossaGiorni);
        // Quello che la finestra non mostra resta com'era.
        Assert.Equal(@"D:\Archivio", risultato.PercorsoRadice);
        Assert.Equal(@"E:\Backup", risultato.CartellaBackup);
        Assert.Equal(14, risultato.BackupPromemoriaGiorni);
        Assert.Equal(new DateTime(2026, 9, 1), risultato.UltimoBackup);
    }

    [Fact]
    public void Costruisci_NonTocca_LeImpostazioniDiPartenza()
    {
        var originale = new ImpostazioniApp();
        var vm = Nuovo(originale);

        vm.RagioneSociale = "Cambiata";
        vm.SogliaArancione = "99";
        vm.Costruisci();

        Assert.Equal("METAL PROJET DI TEDDE PAOLO E SORRENTINO LUCA SNC", originale.Azienda.RagioneSociale);
        Assert.Equal(8, originale.SogliaArancioneGiorni);
    }

    [Fact]
    public void Costruisci_ConSoglieNonValide_Lancia() =>
        Assert.Throws<InvalidOperationException>(() =>
        {
            var vm = Nuovo();
            vm.SogliaRossa = "x";
            vm.Costruisci();
        });

    [Fact]
    public void ModificandoUnCampo_ErroreEPuoSalvareSiAggiornano()
    {
        var vm = Nuovo();
        var cambiate = new List<string?>();
        vm.PropertyChanged += (_, e) => cambiate.Add(e.PropertyName);

        vm.SogliaArancione = "x";

        Assert.Contains(nameof(ImpostazioniViewModel.Errore), cambiate);
        Assert.Contains(nameof(ImpostazioniViewModel.PuoSalvare), cambiate);
    }

    [Fact]
    public void CorreggendoIlValore_LErroreSparisce()
    {
        var vm = Nuovo();
        vm.SogliaArancione = "x";

        vm.SogliaArancione = "20";

        Assert.Equal("", vm.Errore);
        Assert.True(vm.PuoSalvare);
    }
}

public class ImpostazioniApplicateTests : IDisposable
{
    private static readonly DateOnly Oggi = new(2026, 10, 1);

    private readonly ArchivioDiProva _a = new();
    private readonly ImpostazioniService _servizio;
    private readonly ImpostazioniApp _impostazioni;
    private readonly MainViewModel _vm;
    private readonly List<string?> _proprietaCambiate = [];

    public ImpostazioniApplicateTests(): this(null) { }

    private ImpostazioniApplicateTests(string? percorsoFile)
    {
        _servizio = new ImpostazioniService(percorsoFile ?? _a.Tmp.Combina("impostazioni", "impostazioni.json"));
        _impostazioni = new ImpostazioniApp
        {
            PercorsoRadice = _a.Radice, RiepilogoAvvio = false, SogliaArancioneGiorni = 30, SogliaRossaGiorni = 7
        };
        _vm = NuovoViewModel(_servizio);
        _vm.PropertyChanged += (_, e) => _proprietaCambiate.Add(e.PropertyName);
    }

    public void Dispose() => _a.Dispose();

    private MainViewModel NuovoViewModel(ImpostazioniService? servizio, IOcr? ocr = null) =>
        new(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni,
            new AlertService(_impostazioni, new TempoFisso(new DateTime(2026, 10, 1, 9, 0, 0))),
            _a.Ricerca, null, ocr, servizio);

    private NodoAlberoViewModel Radice => _vm.Radici.Single();
    private NodoAlberoViewModel Cartella(string area, string titolo) =>
        Radice.Aree.Single(a => a.Nome == area).Figli.Single(c => c.Nome == titolo);

    /// <summary>Area A: "Tra venti giorni" (scade tra 20 giorni: arancione con 30/7) e "Tra tre giorni" (rossa).</summary>
    private async Task PreparaAsync()
    {
        var area = await _a.Servizio.CreaAreaAsync("A");
        await _a.CreaCartellaInAreaAsync(area, "Tra venti giorni", ["venti.pdf"], Oggi.AddDays(20));
        await _a.CreaCartellaInAreaAsync(area, "Tra tre giorni", ["tre.pdf"], Oggi.AddDays(3));
        await _vm.InizializzaAsync();
        await _vm.CaricamentoElencoCompletato;
    }

    private ImpostazioniApp DalFile() => new ImpostazioniService(_servizio.PercorsoFile).Carica();

    // ---------- Aprire e annullare ----------

    [Fact]
    public async Task LaFinestraMostraIValoriAttuali_EAnnullandoNienteCambia()
    {
        await PreparaAsync();

        _a.Dialog.RispondiImpostazioni(null);
        await _vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Equal(1, _a.Dialog.AperturaImpostazioni);
        Assert.Equal("30", _a.Dialog.UltimeImpostazioni!.SogliaArancione);
        Assert.False(File.Exists(_servizio.PercorsoFile));
        Assert.Equal(StatoAvviso.Arancione, Cartella("A", "Tra venti giorni").Avviso);
    }

    [Fact]
    public async Task ValoriNonValidi_ILPulsanteSalvaEDisattivato_NienteSiSalva()
    {
        await PreparaAsync();

        _a.Dialog.RispondiImpostazioni(m => m.SogliaArancione = "abc");
        await _vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.False(File.Exists(_servizio.PercorsoFile));
        Assert.Equal(30, _impostazioni.SogliaArancioneGiorni);
    }

    // ---------- Salvare e applicare subito ----------

    [Fact]
    public async Task CambiandoLeSoglie_SiSalvanoNelFile_ESiApplicanoSubito()
    {
        await PreparaAsync();
        Assert.Equal(StatoAvviso.Arancione, Cartella("A", "Tra venti giorni").Avviso);

        _a.Dialog.RispondiImpostazioni(m => { m.SogliaArancione = "10"; m.SogliaRossa = "2"; });
        await _vm.ApriImpostazioniCommand.ExecuteAsync(null);

        // Salvate nel file...
        var salvate = DalFile();
        Assert.Equal(10, salvate.SogliaArancioneGiorni);
        Assert.Equal(2, salvate.SogliaRossaGiorni);
        // ...e applicate al programma in esecuzione.
        Assert.Equal(10, _impostazioni.SogliaArancioneGiorni);
        Assert.Equal(StatoAvviso.Nessuno, Cartella("A", "Tra venti giorni").Avviso); // 20 giorni non sono più "entro 10"
        Assert.Equal(StatoAvviso.Arancione, Cartella("A", "Tra tre giorni").Avviso);  // 3 giorni: ora arancione (rosso solo entro 2)
        Assert.Contains("entro 10 giorni", _vm.SuggerimentoStatoRicerca);
    }

    [Fact]
    public async Task IRisultatiGiaMostrati_SiRicalcolanoConLeNuoveSoglie()
    {
        await PreparaAsync();
        Assert.Equal(StatoAvviso.Arancione, _vm.ElencoDocumenti!.Documenti.Single(d => d.NomeFile == "venti.pdf").Avviso);

        _a.Dialog.RispondiImpostazioni(m => m.SogliaArancione = "10");
        await _vm.ApriImpostazioniCommand.ExecuteAsync(null);
        await _vm.CaricamentoElencoCompletato;

        Assert.Equal(StatoAvviso.Nessuno, _vm.ElencoDocumenti!.Documenti.Single(d => d.NomeFile == "venti.pdf").Avviso);
    }

    [Fact]
    public async Task DisattivandoGliAvvisi_SparisconoIconeENodoScadenze()
    {
        await PreparaAsync();
        Assert.Contains(Radice.Figli, f => f.Tipo == TipoNodo.Scadenze);

        _a.Dialog.RispondiImpostazioni(m => m.AvvisiAttivi = false);
        await _vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.DoesNotContain(Radice.Figli, f => f.Tipo == TipoNodo.Scadenze);
        Assert.Equal(StatoAvviso.Nessuno, Radice.Avviso);
        Assert.False(DalFile().AvvisiAttivi);
    }

    [Fact]
    public async Task RiattivandoGliAvvisi_TornaIlNodoScadenze()
    {
        await PreparaAsync();
        _a.Dialog.RispondiImpostazioni(m => m.AvvisiAttivi = false);
        await _vm.ApriImpostazioniCommand.ExecuteAsync(null);

        _a.Dialog.RispondiImpostazioni(m => m.AvvisiAttivi = true);
        await _vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Contains(Radice.Figli, f => f.Tipo == TipoNodo.Scadenze);
        Assert.Equal(StatoAvviso.Rosso, Radice.Avviso);
    }

    [Fact]
    public async Task SeIlNodoScadenzeEraSelezionato_DisattivandoGliAvvisi_SiTornaAllaRadice()
    {
        await PreparaAsync();
        Radice.Figli.Single(f => f.Tipo == TipoNodo.Scadenze).IsSelected = true;
        await _vm.CaricamentoElencoCompletato;

        _a.Dialog.RispondiImpostazioni(m => m.AvvisiAttivi = false);
        await _vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Same(Radice, _vm.NodoSelezionato);
    }

    [Fact]
    public async Task CambiandoIDatiDellaDitta_LaBarraInFondoSiAggiorna()
    {
        await PreparaAsync();
        _proprietaCambiate.Clear();

        _a.Dialog.RispondiImpostazioni(m =>
        {
            m.RagioneSociale = "NUOVA DITTA SRL";
            m.CodiceFiscale = "12345678901";
            m.PartitaIva = "12345678901 (IT)";
            m.Indirizzo = "VIA ROMA 1 - 16100 GENOVA";
            m.Descrizione = "Nuova descrizione";
        });
        await _vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Equal("NUOVA DITTA SRL  -  C.F. 12345678901  -  P.IVA 12345678901 (IT)  -  VIA ROMA 1 - 16100 GENOVA", _vm.TestoAzienda);
        Assert.Equal("Nuova descrizione", _vm.DescrizioneAzienda);
        Assert.Contains(nameof(MainViewModel.TestoAzienda), _proprietaCambiate);
        Assert.Contains(nameof(MainViewModel.DescrizioneAzienda), _proprietaCambiate);
        Assert.Equal("NUOVA DITTA SRL", DalFile().Azienda.RagioneSociale);
    }

    [Fact]
    public async Task CambiandoIlNomeDellaRadice_LAlberoLoMostraSubito()
    {
        await PreparaAsync();

        _a.Dialog.RispondiImpostazioni(m => m.NomeRadice = "Archivio Metal Projet");
        await _vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Equal("Archivio Metal Projet", Radice.Nome);
        Assert.Equal("Archivio Metal Projet", DalFile().NomeRadice);
    }

    [Fact]
    public async Task IlPercorsoDellArchivio_NonCambiaMai_DallaFinestra()
    {
        await PreparaAsync();

        _a.Dialog.RispondiImpostazioni(m => m.NomeRadice = "Altro nome");
        await _vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Equal(_a.Radice, _impostazioni.PercorsoRadice);
        Assert.Equal(_a.Radice, DalFile().PercorsoRadice);
    }

    [Fact]
    public async Task RiapertaDopoUnSalvataggio_LaFinestraMostraIValoriNuovi()
    {
        await PreparaAsync();
        _a.Dialog.RispondiImpostazioni(m => m.SogliaArancione = "45");
        await _vm.ApriImpostazioniCommand.ExecuteAsync(null);

        _a.Dialog.RispondiImpostazioni(null);
        await _vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Equal("45", _a.Dialog.UltimeImpostazioni!.SogliaArancione);
    }

    // ---------- Quando il salvataggio non riesce ----------

    [Fact]
    public async Task SeIlSalvataggioFallisce_SiMostraLErrore_LaFinestraSiRiapre_ENulla_Cambia()
    {
        // Il "file" delle impostazioni sta dentro un file, non dentro una cartella: salvare è impossibile.
        var bloccante = _a.Tmp.CreaFile("bloccante", "sono un file");
        var servizio = new ImpostazioniService(Path.Combine(bloccante, "impostazioni.json"));
        var vm = NuovoViewModel(servizio);
        await vm.InizializzaAsync();

        _a.Dialog.RispondiImpostazioni(m => m.SogliaArancione = "99");
        _a.Dialog.RispondiImpostazioni(null); // alla riapertura l'utente rinuncia
        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Equal(2, _a.Dialog.AperturaImpostazioni);
        Assert.Contains("Non è stato possibile salvare le impostazioni", Assert.Single(_a.Dialog.Errori));
        Assert.Equal(30, _impostazioni.SogliaArancioneGiorni);
        Assert.Equal("99", _a.Dialog.UltimeImpostazioni!.SogliaArancione); // i valori digitati sono ancora lì
    }

    // ---------- Senza servizio ----------

    [Fact]
    public async Task SenzaServizioDiSalvataggio_LaFinestraNonCompare()
    {
        var vm = NuovoViewModel(null);
        await vm.InizializzaAsync();

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.False(vm.ImpostazioniDisponibili);
        Assert.Equal(0, _a.Dialog.AperturaImpostazioni);
    }

    [Fact]
    public void ConIlServizio_LaFinestraCompare() => Assert.True(_vm.ImpostazioniDisponibili);

    // ---------- Informazioni ----------

    [Fact]
    public async Task Informazioni_MostranoDittaVersionePercorsiEContenuto()
    {
        await PreparaAsync();
        var vm = NuovoViewModel(_servizio, new FintoOcr());
        await vm.InizializzaAsync();

        vm.ApriInformazioniCommand.Execute(null);

        var info = _a.Dialog.UltimeInformazioni!;
        Assert.Equal("Documentale", info.Nome);
        Assert.False(string.IsNullOrWhiteSpace(info.Versione));
        Assert.DoesNotContain("+", info.Versione); // senza l'identificativo di compilazione
        Assert.Equal("METAL PROJET DI TEDDE PAOLO E SORRENTINO LUCA SNC", info.Ditta);
        Assert.Equal("01790610990", info.CodiceFiscale);
        Assert.Equal("VIA DELLE GINESTRE, 102 R - 16137 GENOVA (GE - IT)", info.Indirizzo);
        Assert.Contains("fabbricazione di strutture metalliche", info.Descrizione);
        Assert.Equal(_a.Radice, info.PercorsoArchivio);
        Assert.Equal(Path.Combine(_a.Radice, "_dati", "documentale.db"), info.PercorsoDatabase);
        Assert.Equal(_servizio.PercorsoFile, info.PercorsoImpostazioni);
        Assert.Equal("1 area  ·  2 cartelle  ·  2 documenti", info.Contenuto);
        Assert.Equal("Disponibile (italiano).", info.StatoOcr);
        Assert.Contains(".NET", info.Ambiente);
    }

    [Fact]
    public async Task Informazioni_SeLOcrManca_DicePerche()
    {
        var vm = NuovoViewModel(_servizio, new FintoOcr(disponibile: false));
        await vm.InizializzaAsync();

        vm.ApriInformazioniCommand.Execute(null);

        Assert.Contains("lingua italiana", _a.Dialog.UltimeInformazioni!.StatoOcr);
    }

    [Fact]
    public async Task Informazioni_SenzaOcrVerificato_LoDice()
    {
        await _vm.InizializzaAsync();

        _vm.ApriInformazioniCommand.Execute(null);

        Assert.Equal("Non verificato.", _a.Dialog.UltimeInformazioni!.StatoOcr);
    }
}
