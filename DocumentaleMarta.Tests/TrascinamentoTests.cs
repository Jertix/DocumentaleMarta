using System.Windows;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

public class TrascinamentoFunzioniTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void SoloFile_TieneIFile_EscludeLeCartelle()
    {
        var file = _tmp.CreaFile("a.txt");
        var cartella = _tmp.Combina("sottocartella");
        Directory.CreateDirectory(cartella);

        var (risultato, escluse) = Trascinamento.SoloFile([file, cartella, _tmp.CreaFile("b.txt")]);

        Assert.Equal([file, _tmp.Combina("b.txt")], risultato);
        Assert.Equal(1, escluse);
    }

    [Fact]
    public void SoloFile_ListaVuota_NienteDaEscludere()
    {
        var (file, escluse) = Trascinamento.SoloFile([]);

        Assert.Empty(file);
        Assert.Equal(0, escluse);
    }

    [Fact]
    public void Messaggio_SingolareEPlurale()
    {
        Assert.StartsWith("Una cartella non è stata allegata", Trascinamento.MessaggioCartelleEscluse(1));
        Assert.StartsWith("3 cartelle non sono state allegate", Trascinamento.MessaggioCartelleEscluse(3));
    }

    [Fact]
    public void DatiTrascinati_FileEDocumento_SiLeggonoDaiDatiDelTrascinamento()
    {
        VisteTests.InSta(() =>
        {
            var conFile = new DataObject(DataFormats.FileDrop, new[] { @"C:\x\a.pdf", @"C:\x\b.pdf" });
            Assert.True(DatiTrascinati.HaFile(conFile));
            Assert.Equal([@"C:\x\a.pdf", @"C:\x\b.pdf"], DatiTrascinati.File(conFile));
            Assert.Null(DatiTrascinati.Documento(conFile));

            var conDocumento = new DataObject(Trascinamento.FormatoDocumento, new DocumentoTrascinato(7, 3));
            Assert.False(DatiTrascinati.HaFile(conDocumento));
            Assert.Empty(DatiTrascinati.File(conDocumento));
            Assert.Equal(new DocumentoTrascinato(7, 3), DatiTrascinati.Documento(conDocumento));

            var vuoto = new DataObject();
            Assert.False(DatiTrascinati.HaFile(vuoto));
            Assert.Null(DatiTrascinati.Documento(vuoto));
        });
    }

    [Fact]
    public async Task LeRigheDeiDocumenti_SannoChiSonoEDaDoveVengono()
    {
        using var a = new ArchivioDiProva();
        var d = await a.CreaCartellaAsync("A", "C", "x.pdf");
        var form = new CartellaFormViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, d);
        var riga = (IOrigineDocumento)form.Documenti.Single();

        Assert.Equal(d.Documenti[0].Id, riga.DocumentoId);
        Assert.Equal(d.Id, riga.CartellaOrigineId);

        var elenco = new ElencoDocumentiViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, null);
        await elenco.CaricaAsync();
        var rigaElenco = (IOrigineDocumento)elenco.Documenti.Single();
        Assert.Equal(d.Documenti[0].Id, rigaElenco.DocumentoId);
        Assert.Equal(d.Id, rigaElenco.CartellaOrigineId);
    }
}

public class TrascinamentoViewModelTests : IDisposable
{
    private readonly ArchivioDiProva _a = new();
    private readonly ImpostazioniApp _impostazioni;
    private readonly MainViewModel _vm;

    public TrascinamentoViewModelTests()
    {
        _impostazioni = new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false };
        _vm = new MainViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni,
            new AlertService(_impostazioni), _a.Ricerca);
    }

    public void Dispose() => _a.Dispose();

    private NodoAlberoViewModel Radice => _vm.Radici.Single();
    private NodoAlberoViewModel Cartella(string area, string titolo) =>
        Radice.Aree.Single(x => x.Nome == area).Figli.Single(c => c.Nome == titolo);

    private string Sorgente(string nome) => _a.Tmp.CreaFile(Path.Combine("esterno", nome), "contenuto di " + nome);

    // ---------- Sul form: Allega con file trascinati ----------

    [Fact]
    public async Task FileTrascinatiSulForm_SiAllegano()
    {
        var d = await _a.CreaCartellaAsync("A", "C");
        var form = new CartellaFormViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, d);

        await form.AllegaCommand.ExecuteAsync(new[] { Sorgente("uno.pdf"), Sorgente("due.docx") });

        Assert.Equal(["uno.pdf", "due.docx"], form.Documenti.Select(x => x.NomeFile));
        Assert.True(File.Exists(_a.Fisico("A", "C", "uno.pdf")));
        Assert.Empty(_a.Dialog.Errori);
    }

    [Fact]
    public async Task UnaCartellaTrascinataSulForm_SiIgnora_ConUnMessaggio_EIFileSiAllegano()
    {
        var d = await _a.CreaCartellaAsync("A", "C");
        var form = new CartellaFormViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, d);
        var cartellaEsterna = _a.Tmp.Combina("una cartella");
        Directory.CreateDirectory(cartellaEsterna);

        await form.AllegaCommand.ExecuteAsync(new[] { cartellaEsterna, Sorgente("uno.pdf") });

        Assert.Equal(["uno.pdf"], form.Documenti.Select(x => x.NomeFile));
        Assert.StartsWith("Una cartella non è stata allegata", Assert.Single(_a.Dialog.Errori));
    }

    [Fact]
    public async Task SoloCartelleTrascinate_NienteAllegatoEUnMessaggio()
    {
        var d = await _a.CreaCartellaAsync("A", "C");
        var form = new CartellaFormViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, d);
        var uno = _a.Tmp.Combina("uno");
        var due = _a.Tmp.Combina("due");
        Directory.CreateDirectory(uno);
        Directory.CreateDirectory(due);

        await form.AllegaCommand.ExecuteAsync(new[] { uno, due });

        Assert.Empty(form.Documenti);
        Assert.StartsWith("2 cartelle non sono state allegate", Assert.Single(_a.Dialog.Errori));
    }

    [Fact]
    public async Task IlPulsanteAllega_SenzaFileTrascinati_ApreLaFinestraDiScelta()
    {
        var d = await _a.CreaCartellaAsync("A", "C");
        var form = new CartellaFormViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, d);
        _a.Dialog.RispondiFile(Sorgente("scelto.pdf"));

        await form.AllegaCommand.ExecuteAsync(null);

        Assert.Equal(["scelto.pdf"], form.Documenti.Select(x => x.NomeFile));
    }

    // ---------- Nella finestra "Nuova cartella" ----------

    [Fact]
    public void FileTrascinatiNellaFinestraDiCreazione_SiAggiungono_SenzaDoppioniEConIlTitoloAutomatico()
    {
        var vm = new NuovaCartellaViewModel(_a.Dialog, "Fatture");
        var cartella = _a.Tmp.Combina("sotto");
        Directory.CreateDirectory(cartella);

        vm.AggiungiAllegati([Sorgente("Fattura 123.pdf"), cartella]);
        vm.AggiungiAllegati([Sorgente("Fattura 123.pdf")]); // lo stesso file ancora

        Assert.Equal(["Fattura 123.pdf"], vm.Allegati.Select(x => x.NomeFile));
        Assert.Equal("Fattura 123", vm.Titolo);
        Assert.Single(_a.Dialog.Errori);
    }

    // ---------- Sull'albero: file da Esplora file ----------

    [Fact]
    public async Task FileTrascinatiSuUnaCartellaDellAlbero_SiAllegano_EIContatoriSiAggiornano()
    {
        await _a.CreaCartellaAsync("A", "C");
        await _vm.InizializzaAsync();

        await _vm.AllegaATrascinatiAsync(Cartella("A", "C").Id, [Sorgente("uno.pdf"), Sorgente("due.pdf")]);

        Assert.Equal(2, Cartella("A", "C").NumeroDocumenti);
        Assert.Equal(2, Radice.NumeroDocumenti);
        Assert.True(File.Exists(_a.Fisico("A", "C", "uno.pdf")));
    }

    [Fact]
    public async Task FileTrascinatiSuUnaCartellaDiversaDaQuellaAperta_LaSelezioneRestaLa()
    {
        await _a.CreaCartellaAsync("A", "Aperta");
        var areaId = (await _a.Servizio.CaricaAlberoAsync()).Single().Id;
        await _a.CreaCartellaInAreaAsync(areaId, "Altra", []);
        await _vm.InizializzaAsync();
        Cartella("A", "Aperta").IsSelected = true;
        await _vm.CaricamentoFormCompletato;

        await _vm.AllegaATrascinatiAsync(Cartella("A", "Altra").Id, [Sorgente("x.pdf")]);
        await _vm.CaricamentoFormCompletato;

        Assert.Equal("Aperta", _vm.FormCartella!.Titolo);
        Assert.Equal(1, Cartella("A", "Altra").NumeroDocumenti);
    }

    [Fact]
    public async Task SoloCartelleTrascinateSuUnaCartellaDellAlbero_NienteDaAllegare_UnMessaggio()
    {
        await _a.CreaCartellaAsync("A", "C");
        await _vm.InizializzaAsync();
        var sottocartella = _a.Tmp.Combina("sotto");
        Directory.CreateDirectory(sottocartella);

        await _vm.AllegaATrascinatiAsync(Cartella("A", "C").Id, [sottocartella]);

        Assert.Equal(0, Cartella("A", "C").NumeroDocumenti);
        Assert.Single(_a.Dialog.Errori);
    }

    [Fact]
    public async Task FileTrascinatoCheNonEsistePiu_ErroreEAlberoIntatto()
    {
        await _a.CreaCartellaAsync("A", "C");
        await _vm.InizializzaAsync();

        await _vm.AllegaATrascinatiAsync(Cartella("A", "C").Id, [_a.Tmp.Combina("sparito.pdf")]);

        Assert.Single(_a.Dialog.Errori);
        Assert.Equal(0, Cartella("A", "C").NumeroDocumenti);
    }

    // ---------- Sull'albero: documento da una griglia ----------

    [Fact]
    public async Task DocumentoTrascinatoSuUnaCartella_SiSposta_EIContatoriSiAggiornano()
    {
        var origine = await _a.CreaCartellaAsync("A", "Origine", "x.pdf", "y.pdf");
        var areaId = origine.AreaId;
        var destinazione = await _a.CreaCartellaInAreaAsync(areaId, "Destinazione", []);
        await _vm.InizializzaAsync();

        await _vm.SpostaDocumentoAsync(origine.Documenti[0].Id, destinazione.Id);

        Assert.Equal(1, Cartella("A", "Origine").NumeroDocumenti);
        Assert.Equal(1, Cartella("A", "Destinazione").NumeroDocumenti);
        Assert.True(File.Exists(_a.Fisico("A", "Destinazione", "x.pdf")));
        Assert.False(File.Exists(_a.Fisico("A", "Origine", "x.pdf")));
    }

    [Fact]
    public async Task DopoLoSpostamento_IlFormDellaCartellaDiOrigineMostraUnDocumentoInMeno()
    {
        var origine = await _a.CreaCartellaAsync("A", "Origine", "x.pdf", "y.pdf");
        var destinazione = await _a.CreaCartellaInAreaAsync(origine.AreaId, "Destinazione", []);
        await _vm.InizializzaAsync();
        Cartella("A", "Origine").IsSelected = true;
        await _vm.CaricamentoFormCompletato;
        Assert.Equal(2, _vm.FormCartella!.Documenti.Count);

        await _vm.SpostaDocumentoAsync(origine.Documenti[0].Id, destinazione.Id);
        await _vm.CaricamentoFormCompletato;

        Assert.Same(Cartella("A", "Origine"), _vm.NodoSelezionato);
        Assert.Equal(["y.pdf"], _vm.FormCartella!.Documenti.Select(x => x.NomeFile));
    }

    [Fact]
    public async Task SpostamentoConIlFileAperto_MostraErrore_ENulla_Cambia()
    {
        var origine = await _a.CreaCartellaAsync("A", "Origine", "x.pdf");
        var destinazione = await _a.CreaCartellaInAreaAsync(origine.AreaId, "Destinazione", []);
        await _vm.InizializzaAsync();

        using (new FileStream(_a.Fisico("A", "Origine", "x.pdf"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            await _vm.SpostaDocumentoAsync(origine.Documenti[0].Id, destinazione.Id);
        }

        Assert.Contains("aperti in un altro programma", Assert.Single(_a.Dialog.Errori));
        Assert.Equal(1, Cartella("A", "Origine").NumeroDocumenti);
        Assert.Equal(0, Cartella("A", "Destinazione").NumeroDocumenti);
    }

    [Fact]
    public async Task DocumentoGiaEliminatoAltrove_SpostandoloSiMostraErrore_EL_AlberoSiAllinea()
    {
        var origine = await _a.CreaCartellaAsync("A", "Origine", "x.pdf");
        var destinazione = await _a.CreaCartellaInAreaAsync(origine.AreaId, "Destinazione", []);
        await _vm.InizializzaAsync();
        await _a.Servizio.EliminaDocumentoAsync(origine.Documenti[0].Id);

        await _vm.SpostaDocumentoAsync(origine.Documenti[0].Id, destinazione.Id);

        Assert.Contains("non esiste più", Assert.Single(_a.Dialog.Errori));
        Assert.Equal(0, Cartella("A", "Origine").NumeroDocumenti);
    }

    [Fact]
    public async Task SpostandoUnDocumentoDuranteUnaRicerca_LaRicercaResta_EIRisultatiSiAggiornano()
    {
        var estrattore = new FintoEstrattore(".pdf");
        using var a = new ArchivioDiProva(estrattore);
        var origine = await a.CreaCartellaAsync("A", "Origine", "x.pdf");
        var destinazione = await a.CreaCartellaInAreaAsync(origine.AreaId, "Destinazione", []);
        await a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));
        var impostazioni = new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false };
        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, impostazioni, new AlertService(impostazioni), a.Ricerca)
        {
            RitardoRicerca = TimeSpan.Zero
        };
        await vm.InizializzaAsync();
        vm.TestoRicerca = "x.pdf";
        await vm.RicercaCompletata;
        Assert.Equal("Origine", vm.ElencoDocumenti!.Documenti.Single().TitoloCartella);

        await vm.SpostaDocumentoAsync(origine.Documenti[0].Id, destinazione.Id);
        await vm.CaricamentoElencoCompletato;

        Assert.True(vm.InRicerca);
        Assert.Equal("Destinazione", vm.ElencoDocumenti!.Documenti.Single().TitoloCartella);
    }
}
