using System.Text.Json;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;
using DocumentaleMarta.Data.Testo;

namespace DocumentaleMarta.Tests;

/// <summary>Le impostazioni della ricerca nei file XML: spenta di base, poi «solo testo» o «tutto il file».</summary>
public class ImpostazioniRicercaXmlTests
{
    private static ImpostazioniViewModel Nuovo(ImpostazioniApp? impostazioni = null) =>
        new(impostazioni ?? new ImpostazioniApp(), @"C:\x\impostazioni.json");

    [Fact]
    public void Di_Base_LaRicercaNegliXmlESpenta_ESiCercaSoloNelTesto()
    {
        var impostazioni = new ImpostazioniApp();

        Assert.False(impostazioni.RicercaXmlAttiva);
        Assert.Equal(ModoRicercaXml.SoloTesto, impostazioni.ModoXml);
        Assert.Null(impostazioni.ModoXmlInUso);
    }

    [Theory]
    [InlineData(true, ModoRicercaXml.SoloTesto, ModoRicercaXml.SoloTesto)]
    [InlineData(true, ModoRicercaXml.TuttoIlFile, ModoRicercaXml.TuttoIlFile)]
    [InlineData(false, ModoRicercaXml.SoloTesto, null)]
    [InlineData(false, ModoRicercaXml.TuttoIlFile, null)]
    public void ModoXmlInUso_ESoloQuandoLaRicercaEAttiva(bool attiva, ModoRicercaXml modo, ModoRicercaXml? atteso)
    {
        var impostazioni = new ImpostazioniApp { RicercaXmlAttiva = attiva, ModoXml = modo };

        Assert.Equal(atteso, impostazioni.ModoXmlInUso);
    }

    [Fact]
    public void ISalvataggiDelFile_ConservanoLeScelte()
    {
        using var tmp = new CartellaTemporanea();
        var servizio = new ImpostazioniService(tmp.Combina("impostazioni.json"));

        servizio.Salva(new ImpostazioniApp { RicercaXmlAttiva = true, ModoXml = ModoRicercaXml.TuttoIlFile });
        var riletto = servizio.Carica();

        Assert.True(riletto.RicercaXmlAttiva);
        Assert.Equal(ModoRicercaXml.TuttoIlFile, riletto.ModoXml);
        Assert.Contains("\"TuttoIlFile\"", File.ReadAllText(servizio.PercorsoFile)); // il modo si legge nel file, non è un numero
    }

    [Fact]
    public void UnFileDiImpostazioniVecchio_SenzaQuesteScelte_LeTrovaSpente()
    {
        using var tmp = new CartellaTemporanea();
        var percorso = tmp.CreaFile("impostazioni.json", "{ \"NomeRadice\": \"Archivio\", \"SogliaRossaGiorni\": 5, \"SogliaArancioneGiorni\": 20 }");

        var impostazioni = new ImpostazioniService(percorso).Carica();

        Assert.False(impostazioni.RicercaXmlAttiva);
        Assert.Equal(ModoRicercaXml.SoloTesto, impostazioni.ModoXml);
    }

    [Fact]
    public void UnModoSconosciuto_NelFile_DiventaIlPredefinito()
    {
        var impostazioni = JsonSerializer.Deserialize<ImpostazioniApp>("{ \"RicercaXmlAttiva\": true, \"ModoXml\": \"Boh\" }")!;

        Assert.True(impostazioni.RicercaXmlAttiva);
        Assert.Equal(ModoRicercaXml.SoloTesto, impostazioni.ModoXml);
    }

    // ---------- La finestra ----------

    [Fact]
    public void LaFinestra_PartaSpenta_ConIlSoloTestoScelto()
    {
        var vm = Nuovo();

        Assert.False(vm.RicercaXmlAttiva);
        Assert.True(vm.SoloTestoXml);
        Assert.False(vm.TuttoIlFileXml);
        Assert.True(vm.PuoSalvare);
    }

    [Fact]
    public void LaFinestra_RiprendeLeScelteSalvate()
    {
        var vm = Nuovo(new ImpostazioniApp { RicercaXmlAttiva = true, ModoXml = ModoRicercaXml.TuttoIlFile });

        Assert.True(vm.RicercaXmlAttiva);
        Assert.False(vm.SoloTestoXml);
        Assert.True(vm.TuttoIlFileXml);
    }

    [Fact]
    public void IDuePulsantiDiScelta_SiAccendonoAVicenda()
    {
        var vm = Nuovo();
        var cambiate = new List<string?>();
        vm.PropertyChanged += (_, e) => cambiate.Add(e.PropertyName);

        vm.TuttoIlFileXml = true;

        Assert.Equal(ModoRicercaXml.TuttoIlFile, vm.ModoXml);
        Assert.False(vm.SoloTestoXml);
        Assert.Contains(nameof(ImpostazioniViewModel.SoloTestoXml), cambiate);
        Assert.Contains(nameof(ImpostazioniViewModel.TuttoIlFileXml), cambiate);

        vm.TuttoIlFileXml = false; // spegnere un pulsante non cambia la scelta: si sceglie accendendo l'altro
        Assert.Equal(ModoRicercaXml.TuttoIlFile, vm.ModoXml);

        vm.SoloTestoXml = true;
        Assert.Equal(ModoRicercaXml.SoloTesto, vm.ModoXml);
        Assert.False(vm.TuttoIlFileXml);
    }

    [Fact]
    public void Costruisci_PortaLeScelteSullaRicercaXml()
    {
        var vm = Nuovo();

        vm.RicercaXmlAttiva = true;
        vm.TuttoIlFileXml = true;
        var risultato = vm.Costruisci();

        Assert.True(risultato.RicercaXmlAttiva);
        Assert.Equal(ModoRicercaXml.TuttoIlFile, risultato.ModoXml);
        Assert.Equal(ModoRicercaXml.TuttoIlFile, risultato.ModoXmlInUso);
    }
}

/// <summary>Un indicizzatore finto: ricorda cosa gli si è chiesto di rifare.</summary>
public class FintoIndicizzatore : IIndicizzatore
{
    public List<string> Riaccodati { get; } = [];

    public void Accoda(IEnumerable<int> documentiIds)
    {
    }

    public Task RiaccodaPerEstensioneAsync(string estensione)
    {
        Riaccodati.Add(estensione);
        return Task.CompletedTask;
    }
}

/// <summary>Salvando le impostazioni, gli XML già presenti si rifanno solo se è cambiata la ricerca nei file XML.</summary>
public class ImpostazioniXmlPrincipaleTests : IDisposable
{
    private static readonly DateTime Oggi = new(2026, 10, 1, 9, 0, 0);

    private readonly ArchivioDiProva _a = new();
    private readonly ImpostazioniService _servizio;
    private readonly ImpostazioniApp _impostazioni;
    private readonly FintoIndicizzatore _indicizzatore = new();

    public ImpostazioniXmlPrincipaleTests()
    {
        _servizio = new ImpostazioniService(_a.Tmp.Combina("impostazioni", "impostazioni.json"));
        _impostazioni = new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false };
    }

    public void Dispose() => _a.Dispose();

    private MainViewModel Principale(bool conIndicizzatore = true) => new(
        _a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni,
        new AlertService(_impostazioni, new TempoFisso(Oggi)),
        servizioImpostazioni: _servizio,
        indicizzatore: conIndicizzatore ? _indicizzatore : null);

    private async Task SalvaAsync(Action<ImpostazioniViewModel>? modifica, bool conIndicizzatore = true)
    {
        _a.Dialog.RispondiImpostazioni(modifica);
        await Principale(conIndicizzatore).ApriImpostazioniCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task AccendendoLaRicercaNegliXml_SiSalva_ESiRileggonoGliXmlGiaPresenti()
    {
        await SalvaAsync(m => m.RicercaXmlAttiva = true);

        Assert.Equal([".xml"], _indicizzatore.Riaccodati);
        Assert.True(_impostazioni.RicercaXmlAttiva); // vale subito, senza riavviare
        Assert.True(new ImpostazioniService(_servizio.PercorsoFile).Carica().RicercaXmlAttiva);
    }

    [Fact]
    public async Task SpegnendoLaRicerca_GliXmlSiRifannoPerTogliereliDallIndice()
    {
        _impostazioni.RicercaXmlAttiva = true;

        await SalvaAsync(m => m.RicercaXmlAttiva = false);

        Assert.Equal([".xml"], _indicizzatore.Riaccodati);
        Assert.False(_impostazioni.RicercaXmlAttiva);
    }

    [Fact]
    public async Task CambiandoIlModo_ConLaRicercaAttiva_SiRileggono()
    {
        _impostazioni.RicercaXmlAttiva = true;

        await SalvaAsync(m => m.TuttoIlFileXml = true);

        Assert.Equal([".xml"], _indicizzatore.Riaccodati);
        Assert.Equal(ModoRicercaXml.TuttoIlFile, _impostazioni.ModoXml);
    }

    [Fact]
    public async Task CambiandoIlModo_ConLaRicercaSpenta_NonSiFaNulla_MaLaScelta_SiSalva()
    {
        await SalvaAsync(m => m.TuttoIlFileXml = true);

        Assert.Empty(_indicizzatore.Riaccodati);
        Assert.Equal(ModoRicercaXml.TuttoIlFile, new ImpostazioniService(_servizio.PercorsoFile).Carica().ModoXml);
    }

    [Fact]
    public async Task SalvandoAltreModifiche_GliXmlNonSiRileggono()
    {
        _impostazioni.RicercaXmlAttiva = true;

        await SalvaAsync(m => m.SogliaArancione = "45");

        Assert.Empty(_indicizzatore.Riaccodati);
    }

    [Fact]
    public async Task Annullando_GliXmlNonSiRileggono_ELeImpostazioniRestanoCom_Erano()
    {
        await SalvaAsync(modifica: null);

        Assert.Empty(_indicizzatore.Riaccodati);
        Assert.False(_impostazioni.RicercaXmlAttiva);
    }

    [Fact]
    public async Task SenzaIndicizzatore_LeImpostazioniSiSalvanoLoStesso()
    {
        await SalvaAsync(m => m.RicercaXmlAttiva = true, conIndicizzatore: false);

        Assert.True(_impostazioni.RicercaXmlAttiva);
    }
}

/// <summary>
/// Un XML allegato si legge, e nell'indice resta, secondo le impostazioni: spenta, solo testo, tutto il file. Cambiando le
/// scelte, gli XML già presenti si rifanno.
/// </summary>
public class IndicizzazioneModoXmlTests : IDisposable
{
    private const string Xml = "<Fatture><Fattura numero=\"A12\">Bulloni di Rossi</Fattura></Fatture>";

    private ModoRicercaXml? _modo; // di base la ricerca nei file XML è spenta
    private readonly ArchivioDiProva _a;

    public IndicizzazioneModoXmlTests() => _a = new ArchivioDiProva(new EstrattoreXml(() => _modo));

    public void Dispose() => _a.Dispose();

    private Task AttendiAsync() => _a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));

    private async Task<int> AllegaXmlAsync()
    {
        var area = await _a.Servizio.CreaAreaAsync("Inps");
        var cartella = await _a.Servizio.CreaCartellaConDatiAsync(
            area, new DatiCartella("Pratica", null, null, false, null), [_a.Tmp.CreaFile("sorgenti/log.xml", Xml)]);
        await AttendiAsync();
        return cartella.Documenti.Single().Id;
    }

    /// <summary>Cambia le impostazioni e rifa gli XML, come fa il programma salvando la finestra.</summary>
    private async Task CambiaAsync(ModoRicercaXml? modo)
    {
        _modo = modo;
        await _a.Indicizzazione!.RiaccodaPerEstensioneAsync(".xml");
        await AttendiAsync();
    }

    private async Task<int> TrovatiAsync(string testo) => (await _a.Ricerca.CercaAsync(testo)).Risultati.Count;

    [Fact]
    public async Task ConLaRicercaSpenta_UnXmlNonSiLegge_ESiTrovaSoloPerNome()
    {
        var id = await AllegaXmlAsync();

        Assert.Equal(StatoIndicizzazione.NonSupportato, _a.StatoDi(id));
        Assert.Null(_a.TestoIndicizzato(id));
        Assert.Equal(0, await TrovatiAsync("Bulloni"));
        Assert.Equal(1, await TrovatiAsync("log")); // il nome del file si cerca sempre
    }

    [Fact]
    public async Task AccendendoLaRicerca_SoloTesto_SiTrovanoIValori_NonINomiDeiTag()
    {
        var id = await AllegaXmlAsync();

        await CambiaAsync(ModoRicercaXml.SoloTesto);

        Assert.Equal(StatoIndicizzazione.Indicizzato, _a.StatoDi(id));
        Assert.Equal(1, await TrovatiAsync("Bulloni"));
        Assert.Equal(1, await TrovatiAsync("A12")); // il valore di un attributo
        Assert.Equal(0, await TrovatiAsync("numero")); // il nome di un attributo no
        Assert.Equal(0, await TrovatiAsync("Fattura")); // né quello di un tag
    }

    [Fact]
    public async Task PassandoATuttoIlFile_SiTrovanoAncheITag()
    {
        var id = await AllegaXmlAsync();
        await CambiaAsync(ModoRicercaXml.SoloTesto);

        await CambiaAsync(ModoRicercaXml.TuttoIlFile);

        Assert.Equal(StatoIndicizzazione.Indicizzato, _a.StatoDi(id));
        Assert.Equal(1, await TrovatiAsync("Bulloni"));
        Assert.Equal(1, await TrovatiAsync("numero"));
        Assert.Equal(1, await TrovatiAsync("Fattura"));
    }

    [Fact]
    public async Task TornandoASoloTesto_ITagNonSiTrovanoPiu()
    {
        await AllegaXmlAsync();
        await CambiaAsync(ModoRicercaXml.TuttoIlFile);
        Assert.Equal(1, await TrovatiAsync("numero"));

        await CambiaAsync(ModoRicercaXml.SoloTesto);

        Assert.Equal(0, await TrovatiAsync("numero"));
        Assert.Equal(1, await TrovatiAsync("Bulloni"));
    }

    [Fact]
    public async Task SpegnendoLaRicerca_GliXmlSiTolgonoDallIndice()
    {
        var id = await AllegaXmlAsync();
        await CambiaAsync(ModoRicercaXml.SoloTesto);
        Assert.Equal(1, await TrovatiAsync("Bulloni"));

        await CambiaAsync(null);

        Assert.Equal(StatoIndicizzazione.NonSupportato, _a.StatoDi(id));
        Assert.Null(_a.TestoIndicizzato(id));
        Assert.Equal(0, await TrovatiAsync("Bulloni"));
        Assert.Equal(1, await TrovatiAsync("log")); // resta il nome del file
    }

    [Fact]
    public async Task RiaccodandoGliXml_GliAltriTipiNonSiToccano()
    {
        var area = await _a.Servizio.CreaAreaAsync("Inps");
        var cartella = await _a.Servizio.CreaCartellaConDatiAsync(
            area, new DatiCartella("Pratica", null, null, false, null), [_a.Tmp.CreaFile("sorgenti/nota.txt", "ciao")]);
        await AttendiAsync();
        var id = cartella.Documenti.Single().Id;
        Assert.Equal(StatoIndicizzazione.NonSupportato, _a.StatoDi(id)); // l'archivio di questa prova legge solo gli XML

        await CambiaAsync(ModoRicercaXml.SoloTesto);

        Assert.Equal(StatoIndicizzazione.NonSupportato, _a.StatoDi(id));
    }
}

/// <summary>Se si rifanno gli XML mentre uno si sta leggendo, quello si rilegge: nell'indice deve restare il testo nuovo.</summary>
public class RiaccodaMentreSiLegge : IDisposable
{
    private readonly FintoEstrattore _estrattore = new(".xml");
    private readonly ArchivioDiProva _a;

    public RiaccodaMentreSiLegge() => _a = new ArchivioDiProva(_estrattore);

    public void Dispose() => _a.Dispose();

    [Fact]
    public async Task UnXmlInLettura_SiRilegge_EInIndiceResta_IlTestoNuovo()
    {
        var letture = 0;
        var primaLetturaAvviata = new TaskCompletionSource();
        var viaLibera = new TaskCompletionSource();
        _estrattore.Logica = async (_, _) =>
        {
            if (Interlocked.Increment(ref letture) == 1)
            {
                primaLetturaAvviata.SetResult();
                await viaLibera.Task; // la prima lettura resta ferma finché il test non la libera
                return "testo vecchio";
            }
            return "testo nuovo";
        };

        var area = await _a.Servizio.CreaAreaAsync("Inps");
        var cartella = await _a.Servizio.CreaCartellaConDatiAsync(
            area, new DatiCartella("Pratica", null, null, false, null), [_a.Tmp.CreaFile("sorgenti/log.xml", "<a/>")]);
        await primaLetturaAvviata.Task.WaitAsync(TimeSpan.FromSeconds(10));

        await _a.Indicizzazione!.RiaccodaPerEstensioneAsync(".xml"); // cambiano le impostazioni mentre si legge
        viaLibera.SetResult();
        await _a.Indicizzazione.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));

        Assert.Equal(2, letture);
        Assert.Equal("testo nuovo", _a.TestoIndicizzato(cartella.Documenti.Single().Id));
    }
}
