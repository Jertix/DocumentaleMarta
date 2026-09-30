using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.Tests;

public class NuovaCartellaViewModelTests : IDisposable
{
    private readonly ArchivioDiProva _a = new();
    private readonly NuovaCartellaViewModel _vm;

    public NuovaCartellaViewModelTests() => _vm = new NuovaCartellaViewModel(_a.Dialog, "Fatture");

    public void Dispose() => _a.Dispose();

    private string File(string nome) => _a.Tmp.CreaFile(Path.Combine("src", nome), "x");

    private void Allega(params string[] nomi)
    {
        _a.Dialog.RispondiFile(nomi.Select(File).ToArray());
        _vm.AllegaCommand.Execute(null);
    }

    [Fact]
    public void UnFile_IlTitoloEIlSuoNomeSenzaEstensione()
    {
        Allega("Fattura 123.pdf");

        Assert.Equal("Fattura 123", _vm.Titolo);
        Assert.Equal(["Fattura 123.pdf"], _vm.Allegati.Select(a => a.NomeFile));
    }

    [Fact]
    public void PiuFile_IlTitoloEPrimoPiuNumeroDeiRestanti()
    {
        Allega("Fattura 123.pdf", "Fattura 124.pdf", "scan.jpg");

        Assert.Equal("Fattura 123 (+2)", _vm.Titolo);
    }

    [Fact]
    public void TitoloScrittoDallUtente_NonVieneSovrascritto()
    {
        _vm.Titolo = "F24 settembre";

        Allega("scansione001.pdf");

        Assert.Equal("F24 settembre", _vm.Titolo);
    }

    [Fact]
    public void TitoloAutomatico_SegueGliAllegatiAggiuntiEGliAllegatiRimossi()
    {
        Allega("a.pdf");
        Assert.Equal("a", _vm.Titolo);

        Allega("b.pdf");
        Assert.Equal("a (+1)", _vm.Titolo);

        _vm.RimuoviCommand.Execute(_vm.Allegati[0]);
        Assert.Equal("b", _vm.Titolo);

        _vm.RimuoviCommand.Execute(_vm.Allegati[0]);
        Assert.Equal("", _vm.Titolo);
    }

    [Fact]
    public void TitoloAutomaticoModificatoDallUtente_DiventaSuo()
    {
        Allega("a.pdf");
        _vm.Titolo = "a corretto";

        Allega("b.pdf");

        Assert.Equal("a corretto", _vm.Titolo);
    }

    [Fact]
    public void LoStessoFileScelto2Volte_NonSiAllegaDueVolte()
    {
        var percorso = File("a.pdf");
        _a.Dialog.RispondiFile(percorso);
        _vm.AllegaCommand.Execute(null);
        _a.Dialog.RispondiFile(percorso.ToUpperInvariant());
        _vm.AllegaCommand.Execute(null);

        Assert.Single(_vm.Allegati);
    }

    [Fact]
    public void SelezioneAnnullata_NonCambiaNulla()
    {
        _vm.Titolo = "Mio";

        _a.Dialog.RispondiFile();
        _vm.AllegaCommand.Execute(null);

        Assert.Empty(_vm.Allegati);
        Assert.Equal("Mio", _vm.Titolo);
    }

    [Fact]
    public void Convalida_SenzaTitoloESenzaFile_DaUnMessaggio()
    {
        Assert.False(_vm.Convalida());
        Assert.Contains("titolo", _vm.Errore);
    }

    [Fact]
    public void Convalida_ConTitolo_Passa()
    {
        _vm.Titolo = "Pratica";

        Assert.True(_vm.Convalida());
        Assert.Equal("", _vm.Errore);
    }

    [Fact]
    public void Convalida_TitoloTroppoLungo_DaUnMessaggio()
    {
        _vm.Titolo = new string('a', 201);

        Assert.False(_vm.Convalida());
        Assert.Contains("troppo lungo", _vm.Errore);
    }

    [Fact]
    public void Completato_ImpostaLaDataDiOggi_ETogliendoLaSpuntaLaCancella()
    {
        _vm.Completato = true;
        Assert.Equal(DateTime.Today, _vm.DataCompletamento);

        _vm.Completato = false;
        Assert.Null(_vm.DataCompletamento);
    }

    [Fact]
    public void Completato_ConUnaDataGiaScelta_LaConserva()
    {
        _vm.Completato = true;
        _vm.DataCompletamento = new DateTime(2026, 5, 1);
        _vm.Completato = false;
        _vm.DataCompletamento = new DateTime(2026, 5, 1); // l'utente non può, ma il modello non deve impazzire
        _vm.Completato = true;

        Assert.Equal(new DateTime(2026, 5, 1), _vm.DataCompletamento);
    }

    [Fact]
    public void Dati_TraduceICampiPerIlServizio()
    {
        _vm.Titolo = "  Pratica  ";
        _vm.Descrizione = "   ";
        _vm.DataScadenza = new DateTime(2026, 12, 31, 15, 30, 0);
        _vm.Completato = true;
        _vm.DataCompletamento = new DateTime(2026, 10, 1);

        var dati = _vm.Dati;

        Assert.Equal("Pratica", dati.Titolo);
        Assert.Null(dati.Descrizione);
        Assert.Equal(new DateOnly(2026, 12, 31), dati.DataScadenza);
        Assert.True(dati.Completato);
        Assert.Equal(new DateOnly(2026, 10, 1), dati.DataCompletamento);
    }

    [Fact]
    public void CancellaScadenza_LaRimuove()
    {
        _vm.DataScadenza = DateTime.Today;

        _vm.CancellaScadenzaCommand.Execute(null);

        Assert.Null(_vm.DataScadenza);
    }
}
