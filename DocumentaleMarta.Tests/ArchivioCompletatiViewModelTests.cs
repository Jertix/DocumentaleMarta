using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

public class NodiArchivioTests
{
    private static NodoAlberoViewModel Nodo(TipoNodo tipo, string nome = "Nodo", int id = 1, NodoAlberoViewModel? padre = null) =>
        new(tipo, id, nome, "", padre, _ => { });

    private static NodoAlberoViewModel Cartella(NodoAlberoViewModel padre, int id, int documenti)
    {
        var c = Nodo(TipoNodo.Cartella, "c" + id, id, padre);
        c.ImpostaNumeroDocumenti(documenti);
        padre.Figli.Add(c);
        return c;
    }

    [Fact]
    public void IlNodoArchivio_HaLaScatolaDArchivio_ELeAreeDelloStessoGruppoLIconaDellArea()
    {
        Assert.Equal(NodoAlberoViewModel.IconaArchivio, Nodo(TipoNodo.Archivio).Icona);
        Assert.Equal(NodoAlberoViewModel.IconaArea, Nodo(TipoNodo.AreaArchivio).Icona);
        Assert.Equal("", NodoAlberoViewModel.IconaArchivio);
    }

    [Fact]
    public void LeIconeNuove_SonoDiverseDaTutteLeAltre()
    {
        var tutte = new[]
        {
            NodoAlberoViewModel.IconaRadice, NodoAlberoViewModel.IconaScadenze, NodoAlberoViewModel.IconaArea,
            NodoAlberoViewModel.IconaCartella, NodoAlberoViewModel.IconaCartellaCompletata, NodoAlberoViewModel.IconaArchivio
        };

        Assert.Equal(tutte.Length, tutte.Distinct().Count());
    }

    [Fact]
    public void IlTestoDelNodoArchivio_ComprendeIlNumeroDiCartelle_SoloSeCeNeSono()
    {
        var archivio = Nodo(TipoNodo.Archivio, "Archivio completati");
        Assert.Equal("Archivio completati", archivio.Testo);

        var gruppo1 = Nodo(TipoNodo.AreaArchivio, "Fatture", 1, archivio);
        var gruppo2 = Nodo(TipoNodo.AreaArchivio, "INPS", 2, archivio);
        archivio.Figli.Add(gruppo1);
        archivio.Figli.Add(gruppo2);
        Cartella(gruppo1, 10, 2);
        Cartella(gruppo1, 11, 1);
        Cartella(gruppo2, 12, 4);

        Assert.Equal("Archivio completati (3)", archivio.Testo);
        Assert.Equal(3, archivio.NumeroCartelle);
        Assert.Equal(7, archivio.NumeroDocumenti);
        Assert.Equal(2, gruppo1.NumeroCartelle);
        Assert.Equal(3, gruppo1.NumeroDocumenti);
    }

    [Fact]
    public void UnAreaConCartelleArchiviate_LeContaNeiTotali_MaNonTraIFigli()
    {
        var area = Nodo(TipoNodo.Area, "Fatture");
        Cartella(area, 1, 2);
        Cartella(area, 2, 3);
        area.ImpostaArchiviate(cartelle: 4, documenti: 10);

        Assert.Equal(2, area.Figli.Count);
        Assert.Equal(6, area.NumeroCartelle);       // 2 visibili + 4 archiviate
        Assert.Equal(15, area.NumeroDocumenti);     // 5 + 10
        Assert.Equal(4, area.CartelleArchiviate);
    }

    [Fact]
    public void LaRadice_ContaOgniCosaUnaVolta_ComprendendoLeArchiviate()
    {
        var radice = Nodo(TipoNodo.Radice, "Tutti");
        var fatture = Nodo(TipoNodo.Area, "Fatture", 1, radice);
        var inps = Nodo(TipoNodo.Area, "INPS", 2, radice);
        var archivio = Nodo(TipoNodo.Archivio, "Archivio completati", 0, radice);
        var gruppo = Nodo(TipoNodo.AreaArchivio, "Fatture", 1, archivio);
        radice.Figli.Add(fatture);
        radice.Figli.Add(inps);
        radice.Figli.Add(archivio);
        archivio.Figli.Add(gruppo);
        Cartella(fatture, 1, 2);       // una cartella visibile in Fatture
        Cartella(inps, 2, 5);          // una in INPS
        Cartella(gruppo, 3, 4);        // una archiviata (di Fatture)
        fatture.ImpostaArchiviate(1, 4);

        Assert.Equal(3, radice.NumeroCartelle);
        Assert.Equal(11, radice.NumeroDocumenti); // 2 + 5 + 4, non 15: i documenti archiviati non si contano due volte
        Assert.Equal(1, radice.CartelleArchiviate);
        Assert.Equal(2, radice.Aree.Count());     // l'archivio non è un'area
    }

    [Fact]
    public void LaDescrizioneDiUnaCartellaArchiviata_DiceCheEArchiviata()
    {
        var gruppo = Nodo(TipoNodo.AreaArchivio, "Fatture");
        var c = Nodo(TipoNodo.Cartella, "Pagata", 5, gruppo);
        c.ImpostaScadenza(null, completato: true);
        c.ImpostaArchiviata(true);

        Assert.Equal("Cartella completata e archiviata", c.DescrizioneStato);
        Assert.Equal(NodoAlberoViewModel.IconaCartellaCompletata, c.Icona);
        Assert.True(c.Archiviata);
    }
}

public class ArchivioCompletatiNellAlberoTests : IDisposable
{
    private static readonly DateOnly Oggi = new(2026, 10, 1);

    private readonly ArchivioDiProva _a = new();
    private readonly MainViewModel _vm;
    private int _fatture, _inps;
    private CartellaDettaglio _aperta = null!, _chiusaA = null!, _chiusaB = null!, _chiusaC = null!;

    public ArchivioCompletatiNellAlberoTests()
    {
        var impostazioni = new ImpostazioniApp
        {
            PercorsoRadice = _a.Radice, RiepilogoAvvio = false, SogliaArancioneGiorni = 30, SogliaRossaGiorni = 7
        };
        _vm = new MainViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, impostazioni,
            new AlertService(impostazioni, new TempoFisso(new DateTime(2026, 10, 1, 9, 0, 0))), _a.Ricerca)
        { RitardoRicerca = TimeSpan.Zero };
    }

    public void Dispose() => _a.Dispose();

    private NodoAlberoViewModel Radice => _vm.Radici.Single();
    private NodoAlberoViewModel Archivio => Radice.Figli.Single(f => f.Tipo == TipoNodo.Archivio);
    private NodoAlberoViewModel Area(string nome) => Radice.Aree.Single(a => a.Nome == nome);
    private NodoAlberoViewModel CartellaInArea(string area, string titolo) => Area(area).Figli.Single(c => c.Nome == titolo);
    private NodoAlberoViewModel Gruppo(string area) => Archivio.Figli.Single(g => g.Nome == area);
    private NodoAlberoViewModel CartellaInArchivio(string area, string titolo) => Gruppo(area).Figli.Single(c => c.Nome == titolo);

    /// <summary>Fatture: "Aperta", "Chiusa A", "Chiusa B" (completate). INPS: "Chiusa C" (completata). Nessuna archiviata.</summary>
    private async Task PreparaAsync(bool avvia = true)
    {
        _fatture = await _a.Servizio.CreaAreaAsync("Fatture");
        _inps = await _a.Servizio.CreaAreaAsync("INPS");
        _aperta = await _a.CreaCartellaInAreaAsync(_fatture, "Aperta", ["aperta.pdf"], Oggi.AddDays(20));
        _chiusaA = await Chiudi(await _a.CreaCartellaInAreaAsync(_fatture, "Chiusa A", ["a1.pdf", "a2.pdf"]));
        _chiusaB = await Chiudi(await _a.CreaCartellaInAreaAsync(_fatture, "Chiusa B", ["b.pdf"]));
        _chiusaC = await Chiudi(await _a.CreaCartellaInAreaAsync(_inps, "Chiusa C", ["c.pdf"]));
        if (avvia)
            await _vm.InizializzaAsync();
    }

    private async Task<CartellaDettaglio> Chiudi(CartellaDettaglio c) =>
        await _a.Servizio.AggiornaCartellaAsync(c.Id, c.Dati with { Completato = true, DataCompletamento = Oggi });

    // ---------- L'albero ----------

    [Fact]
    public async Task IlNodoArchivio_ESempreInFondoAllaRadice_AncheSenzaCartelleArchiviate()
    {
        await PreparaAsync();

        Assert.Equal(TipoNodo.Archivio, Radice.Figli.Last().Tipo);
        Assert.Equal("Archivio completati", Archivio.Nome);
        Assert.Equal("Archivio completati", Archivio.Testo); // senza numero finché è vuoto
        Assert.Empty(Archivio.Figli);
        Assert.Equal([TipoNodo.Scadenze, TipoNodo.Area, TipoNodo.Area, TipoNodo.Archivio], Radice.Figli.Select(f => f.Tipo));
    }

    [Fact]
    public async Task LeCartelleNonArchiviate_RestanoNellaLoroArea()
    {
        await PreparaAsync();

        Assert.Equal(["Aperta", "Chiusa A", "Chiusa B"], Area("Fatture").Figli.Select(c => c.Nome));
        Assert.Equal(["Chiusa C"], Area("INPS").Figli.Select(c => c.Nome));
    }

    [Fact]
    public async Task UnaCartellaArchiviata_LasciaLaSuaArea_ECompareNelGruppoDelloStessoNomeNellArchivio()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCartellaAsync(_chiusaA.Id);
        await _vm.InizializzaAsync();

        Assert.Equal(["Aperta", "Chiusa B"], Area("Fatture").Figli.Select(c => c.Nome));
        var gruppo = Gruppo("Fatture");
        Assert.Equal(TipoNodo.AreaArchivio, gruppo.Tipo);
        Assert.Equal(_fatture, gruppo.Id);                                    // stesso id dell'area vera
        Assert.Equal(Area("Fatture").PercorsoRelativo, gruppo.PercorsoRelativo);
        var nodo = Assert.Single(gruppo.Figli);
        Assert.Equal((TipoNodo.Cartella, _chiusaA.Id, true), (nodo.Tipo, nodo.Id, nodo.Archiviata));
        Assert.Equal("Archivio completati (1)", Archivio.Testo);
    }

    [Fact]
    public async Task IGruppiDellArchivio_SonoUnoPerAreaConCartelleArchiviate()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCompletateAsync(null);
        await _vm.InizializzaAsync();

        Assert.Equal(["Fatture", "INPS"], Archivio.Figli.Select(g => g.Nome));
        Assert.Equal(["Chiusa A", "Chiusa B"], Gruppo("Fatture").Figli.Select(c => c.Nome));
        Assert.Equal("Archivio completati (3)", Archivio.Testo);
        Assert.Equal(["Aperta"], Area("Fatture").Figli.Select(c => c.Nome));
        Assert.Empty(Area("INPS").Figli);
    }

    [Fact]
    public async Task UnaCartellaArchiviata_NonContaPerGliAvvisiDellArea_EnonCambiaIlNodoScadenze()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCompletateAsync(null);
        await _vm.InizializzaAsync();

        Assert.Equal(StatoAvviso.Nessuno, Area("INPS").Avviso);
        Assert.Equal(StatoAvviso.Arancione, Area("Fatture").Avviso); // per "Aperta", che scade tra 20 giorni
        Assert.Equal(1, Radice.Figli.Single(f => f.Tipo == TipoNodo.Scadenze).NumeroAvvisi);
    }

    // ---------- Totali e riepiloghi ----------

    [Fact]
    public async Task ITotali_ComprendonoLeArchiviate_EIlRiepilogoLeNomina()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCartellaAsync(_chiusaA.Id); // 2 documenti
        await _vm.InizializzaAsync();

        Assert.Equal(3, Area("Fatture").NumeroCartelle);       // Aperta, Chiusa A (archiviata), Chiusa B
        Assert.Equal(1, Area("Fatture").CartelleArchiviate);
        Assert.Equal(4, Area("Fatture").NumeroDocumenti);      // 1 + 2 + 1
        Assert.Equal(4, Radice.NumeroCartelle);
        Assert.Equal(5, Radice.NumeroDocumenti);               // niente doppi conteggi
        Assert.Equal(1, Radice.CartelleArchiviate);

        Assert.Equal("2 aree  ·  4 cartelle (1 archiviata)  ·  5 documenti", _vm.RiepilogoDettaglio);
        Area("Fatture").IsSelected = true;
        Assert.Equal("3 cartelle (1 archiviata)  ·  4 documenti", _vm.RiepilogoDettaglio);
        Area("INPS").IsSelected = true;
        Assert.Equal("1 cartella  ·  1 documento", _vm.RiepilogoDettaglio); // nessuna archiviata: come prima
    }

    [Fact]
    public async Task IlRiepilogoDiPiuArchiviate_VaAlPlurale()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCompletateAsync(null);
        await _vm.InizializzaAsync();

        Assert.Equal("2 aree  ·  4 cartelle (3 archiviate)  ·  5 documenti", _vm.RiepilogoDettaglio);
    }

    // ---------- I pannelli ----------

    [Fact]
    public async Task SelezionandoLArchivio_SiVedonoSoloIDocumentiArchiviati_DiTutteLeAree()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCompletateAsync(null);
        await _vm.InizializzaAsync();

        Archivio.IsSelected = true;
        await _vm.CaricamentoElencoCompletato;

        Assert.Equal(["a1.pdf", "a2.pdf", "b.pdf", "c.pdf"], _vm.ElencoDocumenti!.Documenti.Select(d => d.NomeFile).Order());
        Assert.Equal("Cartelle completate", _vm.TipoDettaglio);
        Assert.Equal("Archivio completati", _vm.TitoloDettaglio);
        Assert.Equal("3 cartelle  ·  4 documenti", _vm.RiepilogoDettaglio);
        Assert.True(_vm.RiepilogoVisibile);
        Assert.Null(_vm.FormCartella);
    }

    [Fact]
    public async Task SelezionandoUnGruppo_SiVedonoSoloIDocumentiArchiviatiDiQuellArea()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCompletateAsync(null);
        await _vm.InizializzaAsync();

        Gruppo("INPS").IsSelected = true;
        await _vm.CaricamentoElencoCompletato;

        Assert.Equal(["c.pdf"], _vm.ElencoDocumenti!.Documenti.Select(d => d.NomeFile));
        Assert.Equal("Archiviate dall'area", _vm.TipoDettaglio);
        Assert.Equal("INPS", _vm.TitoloDettaglio);
    }

    [Fact]
    public async Task UnArchivioVuoto_DiceCosaFare()
    {
        await PreparaAsync();

        Archivio.IsSelected = true;
        await _vm.CaricamentoElencoCompletato;

        Assert.Empty(_vm.ElencoDocumenti!.Documenti);
        Assert.Contains("Nessuna cartella archiviata", _vm.ElencoDocumenti.TestoVuoto);
        Assert.Contains("Archivia", _vm.ElencoDocumenti.TestoVuoto);
    }

    [Fact]
    public async Task LeGrigliaDiRadiceEAree_ComprendonoIDocumentiArchiviati()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCompletateAsync(null);
        await _vm.InizializzaAsync();

        Radice.IsSelected = true;
        await _vm.CaricamentoElencoCompletato;
        Assert.Equal(5, _vm.ElencoDocumenti!.Documenti.Count);

        Area("Fatture").IsSelected = true;
        await _vm.CaricamentoElencoCompletato;
        Assert.Equal(["a1.pdf", "a2.pdf", "aperta.pdf", "b.pdf"], _vm.ElencoDocumenti!.Documenti.Select(d => d.NomeFile).Order());
    }

    [Fact]
    public async Task IlNodoArchivioNonHaUnaCartellaSuDisco_IlGruppoSiPerLArea()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCompletateAsync(null);
        await _vm.InizializzaAsync();

        Archivio.IsSelected = true;
        Assert.False(_vm.HaPercorsoFisico);
        Assert.Equal("", _vm.PercorsoDettaglio);
        Assert.False(_vm.ApriInEsploraCommand.CanExecute(null));
        Assert.False(_vm.PuoModificare);

        Gruppo("INPS").IsSelected = true;
        Assert.True(_vm.HaPercorsoFisico);
        Assert.Equal(_a.Fisico("INPS"), _vm.PercorsoDettaglio);
        Assert.False(_vm.PuoModificare); // il gruppo non si rinomina né si elimina
    }

    // ---------- Archivia e ripristina ----------

    [Fact]
    public async Task ArchiviandoUnaCartellaCompletata_SparisceDallArea_ESiRestaSuDiLei()
    {
        await PreparaAsync();
        CartellaInArea("Fatture", "Chiusa A").IsSelected = true;
        await _vm.CaricamentoFormCompletato;
        Assert.True(_vm.PuoArchiviare);
        Assert.False(_vm.PuoRipristinare);

        await _vm.ArchiviaCommand.ExecuteAsync(null);
        await _vm.CaricamentoFormCompletato;

        Assert.DoesNotContain(Area("Fatture").Figli, c => c.Nome == "Chiusa A");
        var nodo = CartellaInArchivio("Fatture", "Chiusa A");
        Assert.Same(nodo, _vm.NodoSelezionato);                   // si resta sulla cartella
        Assert.True(Gruppo("Fatture").IsExpanded && Archivio.IsExpanded); // e il ramo è aperto
        Assert.True(_vm.FormCartella!.Archiviata);
        Assert.False(_vm.PuoArchiviare);
        Assert.True(_vm.PuoRipristinare);
    }

    [Fact]
    public async Task ArchiviareNonSpostaNienteSuDisco()
    {
        await PreparaAsync();
        var fisicoPrima = Directory.GetFiles(_a.Fisico("Fatture", "Chiusa A")).Order().ToList();
        CartellaInArea("Fatture", "Chiusa A").IsSelected = true;
        await _vm.CaricamentoFormCompletato;

        await _vm.ArchiviaCommand.ExecuteAsync(null);

        Assert.Equal(fisicoPrima, Directory.GetFiles(_a.Fisico("Fatture", "Chiusa A")).Order());
        Assert.Empty(_a.Dialog.Errori);
    }

    [Fact]
    public async Task UnaCartellaNonCompletata_NonSiPuoArchiviare()
    {
        await PreparaAsync();

        CartellaInArea("Fatture", "Aperta").IsSelected = true;

        Assert.False(_vm.PuoArchiviare);
        Assert.False(_vm.ArchiviaCommand.CanExecute(null));
    }

    [Fact]
    public async Task Ripristinando_LaCartellaTornaNellaSuaArea_EIlGruppoSparisceSeVuoto()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCartellaAsync(_chiusaC.Id);
        await _vm.InizializzaAsync();
        CartellaInArchivio("INPS", "Chiusa C").IsSelected = true;
        await _vm.CaricamentoFormCompletato;
        Assert.True(_vm.RipristinaCommand.CanExecute(null));

        await _vm.RipristinaCommand.ExecuteAsync(null);
        await _vm.CaricamentoFormCompletato;

        Assert.Equal(["Chiusa C"], Area("INPS").Figli.Select(c => c.Nome));
        Assert.Empty(Archivio.Figli);
        Assert.Equal("Archivio completati", Archivio.Testo);
        Assert.Same(CartellaInArea("INPS", "Chiusa C"), _vm.NodoSelezionato);
        Assert.True(_vm.NodoSelezionato!.Completato); // ripristinare non riapre la cartella
        Assert.False(_vm.PuoRipristinare);
        Assert.True(_vm.PuoArchiviare);
    }

    // ---------- Archivia tutte le completate ----------

    [Fact]
    public async Task DaUnArea_SiArchivianoLeCompletateDiQuellArea_DopoAverChiesto()
    {
        await PreparaAsync();
        Area("Fatture").IsSelected = true;
        Assert.True(_vm.PuoArchiviareCompletate);

        await _vm.ArchiviaCompletateCommand.ExecuteAsync(null);

        var domanda = Assert.Single(_a.Dialog.Domande);
        Assert.Contains("2 cartelle completate", domanda);
        Assert.Contains("«Fatture»", domanda);
        Assert.Contains("Non si sposta nessun file", domanda);
        Assert.Equal(["Aperta"], Area("Fatture").Figli.Select(c => c.Nome));
        Assert.Equal(["Chiusa A", "Chiusa B"], Gruppo("Fatture").Figli.Select(c => c.Nome));
        Assert.Equal(["Chiusa C"], Area("INPS").Figli.Select(c => c.Nome)); // l'altra area non si tocca
        Assert.Same(Area("Fatture"), _vm.NodoSelezionato);
    }

    [Fact]
    public async Task DallaRadice_SiArchivianoLeCompletateDiTutteLeAree()
    {
        await PreparaAsync();
        Radice.IsSelected = true;

        await _vm.ArchiviaCompletateCommand.ExecuteAsync(null);

        var domanda = Assert.Single(_a.Dialog.Domande);
        Assert.Contains("3 cartelle completate", domanda);
        Assert.DoesNotContain(" di «", domanda); // dalla radice non si nomina nessuna area
        Assert.Equal("Archivio completati (3)", Archivio.Testo);
        Assert.Equal(["Aperta"], Area("Fatture").Figli.Select(c => c.Nome));
        Assert.Empty(Area("INPS").Figli);
    }

    [Fact]
    public async Task RispondendoDiNo_NonSiArchiviaNulla()
    {
        await PreparaAsync();
        Radice.IsSelected = true;
        _a.Dialog.RispostaDomanda = false;

        await _vm.ArchiviaCompletateCommand.ExecuteAsync(null);

        Assert.Single(_a.Dialog.Domande);
        Assert.Empty(Archivio.Figli);
        Assert.Equal(3, Area("Fatture").Figli.Count);
    }

    [Fact]
    public async Task SeNonCeNeSonoDaArchiviare_DiceCosi_SenzaChiedere()
    {
        var area = await _a.Servizio.CreaAreaAsync("Solo aperte");
        await _a.CreaCartellaInAreaAsync(area, "Aperta", ["x.pdf"]);
        await _vm.InizializzaAsync();
        Area("Solo aperte").IsSelected = true;

        await _vm.ArchiviaCompletateCommand.ExecuteAsync(null);

        Assert.Empty(_a.Dialog.Domande);
        Assert.Equal("Non ci sono cartelle completate da archiviare.", Assert.Single(_a.Dialog.Messaggi).Messaggio);
    }

    [Fact]
    public async Task LArchiviazioneInBlocco_SiOffreSoloDaRadiceEAree()
    {
        await PreparaAsync();

        Radice.IsSelected = true;
        Assert.True(_vm.PuoArchiviareCompletate);
        Area("Fatture").IsSelected = true;
        Assert.True(_vm.PuoArchiviareCompletate);
        CartellaInArea("Fatture", "Chiusa A").IsSelected = true;
        Assert.False(_vm.PuoArchiviareCompletate);
        Archivio.IsSelected = true;
        Assert.False(_vm.PuoArchiviareCompletate);
    }

    // ---------- Eliminare ----------

    [Fact]
    public async Task EliminandoUnAreaConCartelleArchiviate_LAvvisoLeNomina_ETutteSiEliminano()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCompletateAsync(_fatture);
        await _vm.InizializzaAsync();
        Area("Fatture").IsSelected = true;

        await _vm.EliminaCommand.ExecuteAsync(null);

        var conferma = Assert.Single(_a.Dialog.Conferme);
        Assert.Contains("3 cartelle", conferma);          // Aperta + le 2 archiviate
        Assert.Contains("4 documenti", conferma);          // 1 + 2 + 1
        Assert.Contains("2 cartelle archiviate", conferma);
        Assert.DoesNotContain(Radice.Aree, a => a.Nome == "Fatture");
        Assert.Empty(Archivio.Figli); // le archiviate di Fatture sono sparite con l'area, e le altre non erano archiviate
        Assert.False(Directory.Exists(_a.Fisico("Fatture")));
    }

    [Fact]
    public async Task EliminandoUnAreaSenzaArchiviate_LAvvisoNonNominaLArchivio()
    {
        await PreparaAsync();
        Area("INPS").IsSelected = true;

        await _vm.EliminaCommand.ExecuteAsync(null);

        Assert.DoesNotContain("archiviat", Assert.Single(_a.Dialog.Conferme));
    }

    [Fact]
    public async Task EliminandoUnaCartellaArchiviata_SparisceDallArchivio()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCartellaAsync(_chiusaA.Id);
        await _a.Servizio.ArchiviaCartellaAsync(_chiusaB.Id);
        await _vm.InizializzaAsync();
        CartellaInArchivio("Fatture", "Chiusa A").IsSelected = true;
        await _vm.CaricamentoFormCompletato;

        await _vm.EliminaCommand.ExecuteAsync(null);

        Assert.Equal(["Chiusa B"], Gruppo("Fatture").Figli.Select(c => c.Nome));
        Assert.False(Directory.Exists(_a.Fisico("Fatture", "Chiusa A")));
    }

    [Fact]
    public async Task RinominandoUnaCartellaArchiviata_RestaNellArchivio()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCartellaAsync(_chiusaA.Id);
        await _vm.InizializzaAsync();
        CartellaInArchivio("Fatture", "Chiusa A").IsSelected = true;
        await _vm.CaricamentoFormCompletato;
        _a.Dialog.RispondiTesto("Chiusa A di settembre");

        await _vm.RinominaCommand.ExecuteAsync(null);

        Assert.Equal(["Chiusa A di settembre"], Gruppo("Fatture").Figli.Select(c => c.Nome));
    }

    // ---------- Altre cose che devono continuare a funzionare ----------

    [Fact]
    public async Task NuovaCartellaDaUnaCartellaArchiviata_LaCreaNellaStessaArea_NonArchiviata()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCartellaAsync(_chiusaA.Id);
        await _vm.InizializzaAsync();
        CartellaInArchivio("Fatture", "Chiusa A").IsSelected = true;
        await _vm.CaricamentoFormCompletato;
        Assert.True(_vm.NuovaCartellaCommand.CanExecute(null));
        _a.Dialog.RispondiNuovaCartella(m => m.Titolo = "Nuova di ottobre");

        await _vm.NuovaCartellaCommand.ExecuteAsync(null);

        Assert.Contains("Nuova di ottobre", Area("Fatture").Figli.Select(c => c.Nome));
        Assert.DoesNotContain(Gruppo("Fatture").Figli, c => c.Nome == "Nuova di ottobre");
    }

    [Fact]
    public async Task LaRicerca_TrovaAncheIDocumentiArchiviati_EIlDoppioClicPortaNellArchivio()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCartellaAsync(_chiusaA.Id);
        await _vm.InizializzaAsync();

        _vm.TestoRicerca = "a1";
        await _vm.RicercaCompletata;
        var riga = Assert.Single(_vm.ElencoDocumenti!.Documenti);
        Assert.Equal("a1.pdf", riga.NomeFile);

        riga.VaiAllaCartellaCommand.Execute(null);
        await _vm.CaricamentoFormCompletato;

        Assert.Same(CartellaInArchivio("Fatture", "Chiusa A"), _vm.NodoSelezionato);
        Assert.True(Gruppo("Fatture").IsExpanded);
        Assert.True(Archivio.IsExpanded);
        Assert.Equal("", _vm.TestoRicerca);
    }

    [Fact]
    public async Task IRamiAperti_SiConservanoRileggendoLAlbero()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCompletateAsync(null);
        await _vm.InizializzaAsync();
        Archivio.IsExpanded = true;
        Gruppo("Fatture").IsExpanded = true;

        await _vm.AggiornaCommand.ExecuteAsync(null);

        Assert.True(Archivio.IsExpanded);
        Assert.True(Gruppo("Fatture").IsExpanded);
        Assert.False(Gruppo("INPS").IsExpanded);
    }

    [Fact]
    public async Task LaSelezioneDiUnaCartellaArchiviata_SiConservaRileggendoLAlbero()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCompletateAsync(null);
        await _vm.InizializzaAsync();
        CartellaInArchivio("INPS", "Chiusa C").IsSelected = true;
        await _vm.CaricamentoFormCompletato;

        await _vm.AggiornaCommand.ExecuteAsync(null);
        await _vm.CaricamentoFormCompletato;

        Assert.Equal("Chiusa C", _vm.NodoSelezionato!.Nome);
        Assert.True(_vm.NodoSelezionato.Archiviata);
        Assert.Equal("Chiusa C", _vm.FormCartella!.Titolo);
    }

    [Fact]
    public async Task ILNomiDelleAree_NelFiltroDellaRicercaAvanzata_NonComprendonoLArchivio()
    {
        await PreparaAsync(avvia: false);
        await _a.Servizio.ArchiviaCompletateAsync(null);
        await _vm.InizializzaAsync();

        Assert.Equal(["Tutte le aree", "Fatture", "INPS"], _vm.Filtri.Aree.Select(a => a.Nome));
    }
}

public class ArchivioCompletatiNelFormTests : IDisposable
{
    private static readonly DateOnly Oggi = new(2026, 10, 1);

    private readonly ArchivioDiProva _a = new();
    private readonly MainViewModel _vm;

    public ArchivioCompletatiNelFormTests()
    {
        var impostazioni = new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false };
        _vm = new MainViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, impostazioni);
    }

    public void Dispose() => _a.Dispose();

    private NodoAlberoViewModel Radice => _vm.Radici.Single();

    private async Task<int> PreparaAsync(bool completata)
    {
        var area = await _a.Servizio.CreaAreaAsync("Fatture");
        var c = await _a.CreaCartellaInAreaAsync(area, "Fattura", ["f.pdf"]);
        if (completata)
            await _a.Servizio.AggiornaCartellaAsync(c.Id, c.Dati with { Completato = true, DataCompletamento = Oggi });
        await _vm.InizializzaAsync();
        _vm.Radici.Single().Aree.Single().Figli.Single().IsSelected = true;
        await _vm.CaricamentoFormCompletato;
        return c.Id;
    }

    [Fact]
    public async Task SpuntandoCompletato_ILPulsanteArchiviaCompare_ToglindoLaSpuntaSparisce()
    {
        await PreparaAsync(completata: false);
        var form = _vm.FormCartella!;
        var cambiate = new List<string?>();
        form.PropertyChanged += (_, e) => cambiate.Add(e.PropertyName);
        Assert.False(form.PuoArchiviare);

        form.Completato = true;
        Assert.True(form.PuoArchiviare);
        Assert.Contains(nameof(CartellaFormViewModel.PuoArchiviare), cambiate);
        await form.AttendiSalvataggioAsync(); // il menu dell'albero si aggiorna a salvataggio avvenuto
        Assert.True(_vm.PuoArchiviare);
        Assert.True(_vm.ArchiviaCommand.CanExecute(null));

        form.Completato = false;
        Assert.False(form.PuoArchiviare);
        await form.AttendiSalvataggioAsync();
        Assert.False(_vm.PuoArchiviare);
    }

    [Fact]
    public async Task IlPulsanteArchiviaDelForm_ArchiviaLaCartella_AncheSeIlSalvataggioEInCorso()
    {
        await PreparaAsync(completata: false);
        var form = _vm.FormCartella!;

        form.Completato = true;                           // il salvataggio parte, ma non lo si aspetta
        await form.ArchiviaCommand.ExecuteAsync(null);    // il comando lo aspetta da sé
        await _vm.CaricamentoFormCompletato;

        Assert.Empty(_a.Dialog.Errori);
        Assert.Equal(TipoNodo.Cartella, _vm.NodoSelezionato!.Tipo);
        Assert.True(_vm.NodoSelezionato.Archiviata);
        Assert.Equal("Archivio completati (1)", Radice.Figli.Last().Testo);
        Assert.True(_vm.FormCartella!.Archiviata);
    }

    [Fact]
    public async Task IlPulsanteRipristinaDelForm_RimetteLaCartellaNellArea()
    {
        await PreparaAsync(completata: true);
        await _vm.ArchiviaCommand.ExecuteAsync(null);
        await _vm.CaricamentoFormCompletato;
        Assert.True(_vm.FormCartella!.PuoRipristinare);

        await _vm.FormCartella.RipristinaCommand.ExecuteAsync(null);
        await _vm.CaricamentoFormCompletato;

        Assert.False(_vm.NodoSelezionato!.Archiviata);
        Assert.Equal(TipoNodo.Area, _vm.NodoSelezionato.Padre!.Tipo);
        Assert.False(_vm.FormCartella!.Archiviata);
        Assert.Equal("Archivio completati", Radice.Figli.Last().Testo);
    }

    [Fact]
    public async Task RiaprendoUnaCartellaArchiviata_TornaNellaSuaAreaDaSola()
    {
        await PreparaAsync(completata: true);
        await _vm.ArchiviaCommand.ExecuteAsync(null);
        await _vm.CaricamentoFormCompletato;
        Assert.Equal(TipoNodo.AreaArchivio, _vm.NodoSelezionato!.Padre!.Tipo);

        _vm.FormCartella!.Completato = false; // la si riapre
        await _vm.FormCartella.AttendiSalvataggioAsync();
        // L'albero si rifà in background: si aspetta che la cartella sia tornata nell'area.
        for (var i = 0; i < 100 && _vm.NodoSelezionato!.Padre!.Tipo != TipoNodo.Area; i++)
            await Task.Delay(50);

        Assert.Equal(TipoNodo.Area, _vm.NodoSelezionato!.Padre!.Tipo);
        Assert.False(_vm.NodoSelezionato.Archiviata);
        Assert.False(_vm.NodoSelezionato.Completato);
        Assert.Equal("Archivio completati", Radice.Figli.Last().Testo);
    }

    [Fact]
    public async Task UnaCartellaDiUnaRicorrenza_SiArchiviaIndipendentementeDallaSuccessiva()
    {
        var area = await _a.Servizio.CreaAreaAsync("Fiscale");
        var f24 = await _a.Servizio.CreaCartellaConDatiAsync(
            area, new DatiCartella("F24", null, new DateOnly(2026, 10, 16), false, null, Ricorrenza.Mensile), []);
        await _vm.InizializzaAsync();
        _vm.Radici.Single().Aree.Single().Figli.Single().IsSelected = true;
        await _vm.CaricamentoFormCompletato;

        _vm.FormCartella!.Completato = true;
        await _vm.FormCartella.AttendiSalvataggioAsync(); // propone (e crea) la successiva
        for (var i = 0; i < 100 && _vm.Radici.Single().Aree.Single().Figli.Count < 2; i++)
            await Task.Delay(50);
        await _vm.CaricamentoFormCompletato;

        await _vm.ArchiviaCommand.ExecuteAsync(null);

        var area2 = _vm.Radici.Single().Aree.Single();
        Assert.Single(area2.Figli);                                  // resta solo la successiva, ancora aperta
        Assert.False(area2.Figli.Single().Completato);
        Assert.Equal(f24.Id, _vm.NodoSelezionato!.Id);               // quella archiviata è la prima
        Assert.True(_vm.NodoSelezionato.Archiviata);
    }
}
