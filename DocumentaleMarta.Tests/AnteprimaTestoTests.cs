using System.Text;
using System.Windows;
using System.Windows.Controls;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;

namespace DocumentaleMarta.Tests;

/// <summary>L'anteprima dei file di testo (.txt, .csv, .xml): le prime righe, con il numero scelto nelle impostazioni.</summary>
[Collection("WPF")]
public class GeneratoreAnteprimaTestoTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();

    public void Dispose() => _tmp.Dispose();

    /// <summary>Un testo di tante righe «riga 1», «riga 2»…, con gli a capo di Windows.</summary>
    private static string Righe(int quante) =>
        string.Join("\r\n", Enumerable.Range(1, quante).Select(i => $"riga {i}"));

    private static string[] Linee(RisultatoAnteprima risultato) =>
        risultato.Testo!.Split(["\r\n", "\n"], StringSplitOptions.None);

    private static Task<RisultatoAnteprima> Genera(string percorso, int righe = 100) =>
        Task.Run(() => new GeneratoreAnteprima(() => righe).GeneraAsync(percorso, 0, CancellationToken.None));

    private string Scrivi(string nome, byte[] byte_)
    {
        var percorso = _tmp.Combina(nome);
        File.WriteAllBytes(percorso, byte_);
        return percorso;
    }

    [Fact]
    public async Task UnTxtCorto_SiMostraTutto_SenzaNota()
    {
        var risultato = await Genera(_tmp.CreaFile("appunti.txt", Righe(5)));

        Assert.Equal(StatoAnteprima.Pronta, risultato.Stato);
        Assert.Null(risultato.Immagine);
        Assert.Equal(5, Linee(risultato).Length);
        Assert.Equal("riga 5", Linee(risultato)[^1]);
        Assert.Equal("", risultato.Nota);
        Assert.True(risultato.TestoACapo);
    }

    [Fact]
    public async Task UnTxtLungo_MostraSoloLePrimeRighe_EDiceQuante()
    {
        var risultato = await Genera(_tmp.CreaFile("lungo.txt", Righe(300)), righe: 100);

        Assert.Equal(100, Linee(risultato).Length);
        Assert.Equal("riga 1", Linee(risultato)[0]);
        Assert.Equal("riga 100", Linee(risultato)[^1]);
        Assert.Equal(GeneratoreAnteprima.NotaTestoParziale(100), risultato.Nota);
        Assert.Contains("100 righe", risultato.Nota);
    }

    [Fact]
    public async Task UnTestoDiEsattamenteLeRigheChieste_NonDiceChePiuAvanti()
    {
        var risultato = await Genera(_tmp.CreaFile("giusto.txt", Righe(20) + "\r\n"), righe: 20);

        Assert.Equal(20, Linee(risultato).Length);
        Assert.Equal("", risultato.Nota);
    }

    [Fact]
    public async Task LeRigheMostrate_SiPrendonoDalNumeroDelleImpostazioni()
    {
        var percorso = _tmp.CreaFile("lungo.txt", Righe(300));

        Assert.Equal(20, Linee(await Genera(percorso, righe: 20)).Length);
        Assert.Equal(250, Linee(await Genera(percorso, righe: 250)).Length);
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(-5, 10)]
    [InlineData(100000, 1000)]
    public async Task UnNumeroDiRigheFuoriDaiLimiti_SiPortaAiLimiti(int chieste, int mostrate)
    {
        var risultato = await Genera(_tmp.CreaFile("enorme.txt", Righe(2000)), righe: chieste);

        Assert.Equal(mostrate, Linee(risultato).Length);
    }

    [Fact]
    public async Task SenzaImpostazioni_SiMostrano100Righe()
    {
        var percorso = _tmp.CreaFile("lungo.txt", Righe(300));

        var risultato = await Task.Run(() => new GeneratoreAnteprima().GeneraAsync(percorso, 0, CancellationToken.None));

        Assert.Equal(ImpostazioniApp.RigheAnteprimaPredefinite, Linee(risultato).Length);
    }

    [Fact]
    public async Task UnCsv_NonVaACapo_ESiScorreDiLato()
    {
        var risultato = await Genera(_tmp.CreaFile("elenco.csv", "codice;nome;importo\r\n1;Bulloni;12,50"));

        Assert.Equal(StatoAnteprima.Pronta, risultato.Stato);
        Assert.False(risultato.TestoACapo);
        Assert.Equal("codice;nome;importo", Linee(risultato)[0]);
    }

    // ---------- XML ----------

    [Fact]
    public async Task UnXmlGiaACapo_SiMostraCosiComeENonVaACapo()
    {
        const string xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\r\n<fattura>\r\n  <numero>12</numero>\r\n  <importo>12,50</importo>\r\n</fattura>";

        var risultato = await Genera(_tmp.CreaFile("fattura.xml", xml));

        Assert.Equal(StatoAnteprima.Pronta, risultato.Stato);
        Assert.False(risultato.TestoACapo);
        Assert.Equal(xml, risultato.Testo);
    }

    [Fact]
    public async Task UnXmlSuUnaRigaSola_SiMostraConIRientri_ELaDichiarazione()
    {
        var risultato = await Genera(_tmp.CreaFile("fattura.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?><fattura><numero>12</numero><righe><riga>Bulloni</riga></righe></fattura>"));

        Assert.Equal(
            ["<?xml version=\"1.0\" encoding=\"UTF-8\"?>", "<fattura>", "  <numero>12</numero>", "  <righe>", "    <riga>Bulloni</riga>",
             "  </righe>", "</fattura>"],
            Linee(risultato));
    }

    [Fact]
    public async Task UnXmlSenzaDichiarazione_SuUnaRiga_NonNeAggiungeUna()
    {
        var risultato = await Genera(_tmp.CreaFile("breve.xml", "<a><b>1</b></a>"));

        Assert.Equal(["<a>", "  <b>1</b>", "</a>"], Linee(risultato));
    }

    [Fact]
    public async Task UnXmlRovinatoSuUnaRiga_SiMostraCosiCome()
    {
        var risultato = await Genera(_tmp.CreaFile("rotto.xml", "<a><b>1</a>"));

        Assert.Equal(StatoAnteprima.Pronta, risultato.Stato);
        Assert.Equal("<a><b>1</a>", risultato.Testo);
    }

    [Fact]
    public async Task UnXmlConDefinizioniEsterne_NonLeSegue_ESiMostraCosiCome()
    {
        const string xml = "<!DOCTYPE a [<!ENTITY x SYSTEM \"file:///C:/Windows/win.ini\">]><a>&x;</a>";

        var risultato = await Genera(_tmp.CreaFile("insidioso.xml", xml));

        Assert.Equal(xml, risultato.Testo);
    }

    [Fact]
    public async Task UnXmlConLeLettereAccentate_DichiarateIso88591_SiLegge()
    {
        byte[] dati = [.. "<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?><a>caff"u8.ToArray(), 0xE8, .. "</a>"u8.ToArray()];

        var risultato = await Genera(Scrivi("vecchio.xml", dati));

        Assert.Equal(["<?xml version=\"1.0\" encoding=\"ISO-8859-1\"?>", "<a>caffè</a>"], Linee(risultato));
    }

    [Fact]
    public async Task UnXmlEnormeSuUnaRiga_NonSiRimetteInColonna_SiTaglia()
    {
        var risultato = await Genera(_tmp.CreaFile("enorme.xml", "<r>" + new string('a', 700_000) + "</r>"));

        Assert.Equal(LettoreTestoAnteprima.CaratteriMassimiPerRiga + 1, risultato.Testo!.Length);
        Assert.NotEqual("", risultato.Nota);
    }

    [Fact]
    public async Task LEstensioneInMaiuscolo_VaBeneLoStesso()
    {
        var risultato = await Genera(_tmp.CreaFile("LETTERA.TXT", "ciao"));

        Assert.Equal(StatoAnteprima.Pronta, risultato.Stato);
        Assert.Equal("ciao", risultato.Testo);
    }

    [Fact]
    public async Task UnFileVuoto_DiceCheEVuoto_ENonEUnErrore()
    {
        var risultato = await Genera(_tmp.CreaFile("vuoto.txt", ""));

        Assert.Equal(StatoAnteprima.NonDisponibile, risultato.Stato);
        Assert.Contains("vuoto", risultato.Messaggio);
    }

    [Fact]
    public async Task UnFileBinarioRinominatoTxt_NonSiMostra()
    {
        var risultato = await Genera(Scrivi("finto.txt", [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x01, 0x02]));

        Assert.Equal(StatoAnteprima.NonDisponibile, risultato.Stato);
        Assert.Contains("non sembra di testo", risultato.Messaggio);
        Assert.Contains("Apri", risultato.Messaggio);
        Assert.Null(risultato.Testo);
    }

    [Fact]
    public async Task UnTestoUtf8_ConLeLettereAccentate_SiLegge()
    {
        var risultato = await Genera(Scrivi("accenti.txt", Encoding.UTF8.GetBytes("perché è già così — 12 €")));

        Assert.Equal("perché è già così — 12 €", risultato.Testo);
    }

    [Fact]
    public async Task UnTestoUtf8_ConBom_NonMostraIlSegnoIniziale()
    {
        var risultato = await Genera(Scrivi("bom.txt", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes("è qui")]));

        Assert.Equal("è qui", risultato.Testo);
    }

    [Fact]
    public async Task UnVecchioTestoWindows1252_SiLegge_ConLEuro()
    {
        // «caffè » + € nella codifica dei vecchi file di testo di Windows: 0xE8 = è, 0x80 = €.
        var risultato = await Genera(Scrivi("vecchio.txt", [.. "caff"u8.ToArray(), 0xE8, 0x20, 0x80]));

        Assert.Equal("caffè €", risultato.Testo);
    }

    [Fact]
    public async Task UnTestoUtf16_ConBom_SiLegge()
    {
        var risultato = await Genera(Scrivi("utf16.txt", [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("riga uno\r\nriga due")]));

        Assert.Equal(StatoAnteprima.Pronta, risultato.Stato);
        Assert.Equal(["riga uno", "riga due"], Linee(risultato));
    }

    [Fact]
    public async Task UnaRigaLunghissima_SiTaglia()
    {
        var risultato = await Genera(_tmp.CreaFile("una-riga.csv", new string('a', 5000)));

        Assert.Equal(LettoreTestoAnteprima.CaratteriMassimiPerRiga + 1, risultato.Testo!.Length);
        Assert.EndsWith("…", risultato.Testo);
    }

    [Fact]
    public async Task UnFileEnorme_SiLegge_SoloAllInizio_ESiDiceCheProsegue()
    {
        // Più di 2 MB: oltre il limite di lettura. Le prime righe si vedono comunque, subito.
        var percorso = _tmp.CreaFile("enorme.txt", Righe(250_000));
        Assert.True(new FileInfo(percorso).Length > 2 * LettoreTestoAnteprima.ByteMassimi);

        var risultato = await Genera(percorso, righe: 50);

        Assert.Equal(50, Linee(risultato).Length);
        Assert.Equal("riga 50", Linee(risultato)[^1]);
        Assert.NotEqual("", risultato.Nota);
    }

    [Fact]
    public async Task UnCarattereSpezzatoDalLimiteDiLettura_NonRovinaLaCodifica()
    {
        // Un UTF-8 di soli «€» (3 byte l'uno, in una riga sola) più lungo del limite: il taglio cade in mezzo a un carattere.
        var risultato = await Genera(Scrivi("euro.txt", Encoding.UTF8.GetBytes(new string('€', 400_000))));

        Assert.StartsWith("€€€", risultato.Testo);
        Assert.DoesNotContain("â", risultato.Testo); // se si leggesse come Windows-1252 si vedrebbe «â‚¬»
    }

    [Fact]
    public async Task UnFileAperto_InUnAltroProgramma_SiLeggeLoStesso()
    {
        var percorso = _tmp.CreaFile("aperto.txt", "ciao");
        using var altroProgramma = new FileStream(percorso, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);

        Assert.Equal("ciao", (await Genera(percorso)).Testo);
    }

    [Fact]
    public async Task UnFileSparito_DiceCheNonCEPiu()
    {
        var risultato = await Genera(_tmp.Combina("sparito.txt"));

        Assert.Equal(StatoAnteprima.Errore, risultato.Stato);
        Assert.Contains("non si trova più", risultato.Messaggio);
    }

    [Fact]
    public async Task Annullando_SiInterrompe()
    {
        var percorso = _tmp.CreaFile("a.txt", "ciao");
        using var annullamento = new CancellationTokenSource();
        await annullamento.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Task.Run(() => new GeneratoreAnteprima().GeneraAsync(percorso, 0, annullamento.Token)));
    }
}

/// <summary>Il pannello e la finestra grande disegnati con il testo di un file al posto dell'immagine.</summary>
[Collection("WPF")]
public class AnteprimaTestoVisteTests : IDisposable
{
    private record Doc(string NomeFile, string PercorsoRelativo) : IDocumentoAnteprima;

    private readonly CartellaTemporanea _tmp = new();
    private readonly ArchivioFileService _files;
    private readonly List<AnteprimaViewModel> _aperte = [];

    public AnteprimaTestoVisteTests() => _files = new ArchivioFileService(_tmp.Combina("Documentale"), usaCestino: false);

    public void Dispose() => _tmp.Dispose();

    private async Task<AnteprimaViewModel> PannelloAsync(IGeneratoreAnteprima generatore, string nome, string contenuto = "contenuto")
    {
        var relativo = Path.Combine("Fatture", "F", nome);
        var percorso = _files.PercorsoAssoluto(relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);
        File.WriteAllText(percorso, contenuto);

        var vm = new AnteprimaViewModel(generatore, _files, mostraIngrandita: _aperte.Add) { Ritardo = TimeSpan.Zero };
        vm.Mostra(new Doc(nome, relativo));
        await vm.Completamento;
        return vm;
    }

    private static string Righe(int quante) =>
        string.Join("\r\n", Enumerable.Range(1, quante).Select(i => $"riga {i} di un testo un po' lungo, per vedere come va a capo"));

    private static IEnumerable<T> Tutti<T>(DependencyObject? radice) where T : DependencyObject
    {
        if (radice is null)
            yield break;
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(radice); i++)
        {
            var figlio = System.Windows.Media.VisualTreeHelper.GetChild(radice, i);
            if (figlio is T trovato)
                yield return trovato;
            foreach (var discendente in Tutti<T>(figlio))
                yield return discendente;
        }
    }

    [Fact]
    public async Task IlPannello_PerUnTxt_MostraIlTesto_AlPostoDellImmagine()
    {
        var vm = await PannelloAsync(new GeneratoreAnteprima(() => 30), "appunti.txt", Righe(80));

        var errori = VisteTests.InSta(() =>
        {
            var pannello = new AnteprimaView { DataContext = vm, Width = 320, Height = 520 };
            VisteTests.Disegna(pannello, 320, 520, "anteprima-testo-pannello");

            var casella = Tutti<TextBox>(pannello).Single(c => c.Text.StartsWith("riga 1 "));
            Assert.Equal(Visibility.Visible, casella.Visibility);
            Assert.True(casella.IsReadOnly);
            Assert.Equal(TextWrapping.Wrap, casella.TextWrapping);
            Assert.Equal(30, casella.Text.Split("\r\n").Length);
            Assert.Equal(Visibility.Collapsed, Tutti<Border>(pannello).First(b => b.Child is Image).Visibility);

            var ingrandisci = Tutti<Button>(pannello).Single(b =>
                (string?)System.Windows.Automation.AutomationProperties.GetName(b) == "Ingrandisci l'anteprima");
            Assert.Equal(Visibility.Visible, ingrandisci.Visibility);
            Assert.True(ingrandisci.IsEnabled);
        });

        Assert.Empty(errori);
        Assert.True(vm.HaNota); // «Si vedono le prime 30 righe…»
    }

    [Fact]
    public async Task IlPannello_PerUnCsv_NonVaACapo_ESiScorreDiLato()
    {
        var vm = await PannelloAsync(new GeneratoreAnteprima(), "elenco.csv", "codice;nome;importo;note\r\n1;Bulloni M8;12,50;ordinati il 3 settembre");

        var errori = VisteTests.InSta(() =>
        {
            var pannello = new AnteprimaView { DataContext = vm, Width = 320, Height = 520 };
            VisteTests.Disegna(pannello, 320, 520, "anteprima-testo-csv");

            var casella = Tutti<TextBox>(pannello).Single(c => c.Text.StartsWith("codice;"));
            Assert.Equal(TextWrapping.NoWrap, casella.TextWrapping);
            Assert.Equal(ScrollBarVisibility.Auto, casella.HorizontalScrollBarVisibility);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task IlPannello_PerUnaImmagine_NonMostraLaCasellaDiTesto()
    {
        var vm = await PannelloAsync(new FintoGeneratoreAnteprima(), "foto.png");

        var errori = VisteTests.InSta(() =>
        {
            var pannello = new AnteprimaView { DataContext = vm, Width = 320, Height = 520 };
            VisteTests.Disegna(pannello, 320, 520, "anteprima-testo-immagine");

            var caselle = Tutti<TextBox>(pannello).ToList();
            Assert.NotEmpty(caselle);
            Assert.All(caselle, c => Assert.Equal(Visibility.Collapsed, c.Visibility));
            Assert.Equal(Visibility.Visible, Tutti<Border>(pannello).First(b => b.Child is Image).Visibility);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task LaFinestraGrande_PerUnTxt_MostraIlTesto_ENonLoZoom()
    {
        var pannello = await PannelloAsync(new GeneratoreAnteprima(() => 30), "appunti.txt", Righe(80));
        pannello.IngrandisciCommand.Execute(null);
        var modello = _aperte.Single();
        await modello.Completamento;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new AnteprimaIngranditaDialog(modello);
            VisteTests.Disegna((FrameworkElement)finestra.Content, 1100, 780, "anteprima-testo-ingrandita");

            var casella = Tutti<TextBox>((FrameworkElement)finestra.Content).Single(c => c.Text.StartsWith("riga 1 "));
            Assert.Equal(Visibility.Visible, casella.Visibility);
            Assert.Equal(TextWrapping.Wrap, casella.TextWrapping);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)finestra.FindName("Scorrimento")!).Visibility);
            Assert.Equal(Visibility.Collapsed, ((FrameworkElement)((FrameworkElement)finestra.FindName("PulsanteMeno")!).Parent).Visibility);
            Assert.Equal("appunti.txt  —  anteprima ingrandita", finestra.Title);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task LaFinestraGrande_PerUnaImmagine_TieneLoZoom_ENonLaCasellaDiTesto()
    {
        var pannello = await PannelloAsync(new FintoGeneratoreAnteprima(), "foto.png");
        pannello.IngrandisciCommand.Execute(null);
        var modello = _aperte.Single();
        await modello.Completamento;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new AnteprimaIngranditaDialog(modello);
            VisteTests.Disegna((FrameworkElement)finestra.Content, 1100, 780, "anteprima-testo-ingrandita-immagine");

            Assert.Equal(Visibility.Visible, ((FrameworkElement)((FrameworkElement)finestra.FindName("PulsanteMeno")!).Parent).Visibility);
            var caselle = Tutti<TextBox>((FrameworkElement)finestra.Content).ToList();
            Assert.NotEmpty(caselle);
            Assert.All(caselle, c => Assert.Equal(Visibility.Collapsed, c.Visibility));
        });

        Assert.Empty(errori);
    }
}

/// <summary>Quel che il pannello fa vedere (e quando) per un file di testo.</summary>
[Collection("WPF")]
public class AnteprimaTestoViewModelTests : IDisposable
{
    private record Doc(string NomeFile, string PercorsoRelativo) : IDocumentoAnteprima;

    private readonly CartellaTemporanea _tmp = new();
    private readonly ArchivioFileService _files;
    private readonly FintoGeneratoreAnteprima _generatore = new();
    private readonly List<AnteprimaViewModel> _aperte = [];
    private readonly AnteprimaViewModel _vm;

    public AnteprimaTestoViewModelTests()
    {
        _files = new ArchivioFileService(_tmp.Combina("Documentale"), usaCestino: false);
        _vm = new AnteprimaViewModel(_generatore, _files, mostraIngrandita: _aperte.Add) { Ritardo = TimeSpan.Zero };
    }

    public void Dispose() => _tmp.Dispose();

    private Doc Documento(string nome)
    {
        var relativo = Path.Combine("Fatture", "F", nome);
        var percorso = _files.PercorsoAssoluto(relativo);
        Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);
        File.WriteAllText(percorso, "contenuto");
        return new Doc(nome, relativo);
    }

    private void RispondeConIlTesto(string testo, bool aCapo = true, string nota = "") =>
        _generatore.Comportamento = (_, _, _) => Task.FromResult(RisultatoAnteprima.DiTesto(testo, aCapo, nota));

    [Fact]
    public async Task UnFileDiTesto_SiMostraComeTesto_NonComeImmagine()
    {
        RispondeConIlTesto("riga 1\r\nriga 2", aCapo: false, nota: "Si vedono le prime 2 righe.");

        _vm.Mostra(Documento("appunti.txt"));
        await _vm.Completamento;

        Assert.Equal(StatoAnteprima.Pronta, _vm.Stato);
        Assert.Equal("riga 1\r\nriga 2", _vm.Testo);
        Assert.False(_vm.TestoACapo);
        Assert.True(_vm.HaTesto);
        Assert.True(_vm.HaContenuto);
        Assert.False(_vm.HaImmagine);
        Assert.False(_vm.HaMessaggio);
        Assert.False(_vm.CaricamentoVisibile);
        Assert.False(_vm.HaPagine);
        Assert.True(_vm.HaNota);
        Assert.Equal("Si vedono le prime 2 righe.", _vm.Nota);
    }

    [Fact]
    public async Task UnTesto_SiPuoIngrandire()
    {
        RispondeConIlTesto("ciao");
        _vm.Mostra(Documento("appunti.txt"));
        await _vm.Completamento;

        Assert.True(_vm.IngrandisciCommand.CanExecute(null));
        _vm.IngrandisciCommand.Execute(null);

        var ingrandita = Assert.Single(_aperte);
        await ingrandita.Completamento;
        Assert.Equal("ciao", ingrandita.Testo);
    }

    [Fact]
    public async Task PassandoDaUnTestoAUnaImmagine_IlTestoSiToglie()
    {
        RispondeConIlTesto("ciao");
        _vm.Mostra(Documento("appunti.txt"));
        await _vm.Completamento;

        _generatore.Comportamento = null; // il documento dopo è un'immagine
        _vm.Mostra(Documento("foto.png"));
        await _vm.Completamento;

        Assert.Null(_vm.Testo);
        Assert.False(_vm.HaTesto);
        Assert.True(_vm.HaImmagine);
    }

    [Fact]
    public async Task PassandoDaUnTestoAUnAltroDocumento_MentreSiPrepara_NonRestaIlTestoVecchio()
    {
        RispondeConIlTesto("vecchio");
        _vm.Mostra(Documento("a.txt"));
        await _vm.Completamento;

        var via = new TaskCompletionSource<RisultatoAnteprima>();
        _generatore.Comportamento = (_, _, _) => via.Task;
        _vm.Mostra(Documento("b.txt"));

        Assert.Null(_vm.Testo);
        Assert.Equal(StatoAnteprima.Caricamento, _vm.Stato);
        Assert.True(_vm.CaricamentoVisibile);

        via.SetResult(RisultatoAnteprima.DiTesto("nuovo", aCapo: true));
        await _vm.Completamento;
        Assert.Equal("nuovo", _vm.Testo);
    }

    [Fact]
    public async Task SvuotandoLAnteprima_ILTestoSparisce()
    {
        RispondeConIlTesto("ciao");
        _vm.Mostra(Documento("a.txt"));
        await _vm.Completamento;

        _vm.Svuota();

        Assert.Null(_vm.Testo);
        Assert.False(_vm.HaContenuto);
        Assert.True(_vm.HaMessaggio);
    }
}
