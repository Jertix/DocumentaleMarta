using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

/// <summary>Nell'albero le cartelle completate hanno un'icona (e un colore) diversi da quelle ancora aperte.</summary>
public class NodoCartellaCompletataTests
{
    private static NodoAlberoViewModel Nodo(TipoNodo tipo, string nome = "Fattura 1") =>
        new(tipo, 1, nome, "", null, _ => { });

    [Fact]
    public void LeIconeDeiNodi_SonoQuelleDiSempre()
    {
        Assert.Equal(NodoAlberoViewModel.IconaRadice, Nodo(TipoNodo.Radice).Icona);
        Assert.Equal(NodoAlberoViewModel.IconaScadenze, Nodo(TipoNodo.Scadenze).Icona);
        Assert.Equal(NodoAlberoViewModel.IconaArea, Nodo(TipoNodo.Area).Icona);
        Assert.Equal(NodoAlberoViewModel.IconaCartella, Nodo(TipoNodo.Cartella).Icona);
    }

    [Fact]
    public void LeIconeSonoGlifiDiversiTraLoro()
    {
        var glifi = new[]
        {
            NodoAlberoViewModel.IconaRadice, NodoAlberoViewModel.IconaScadenze, NodoAlberoViewModel.IconaArea,
            NodoAlberoViewModel.IconaCartella, NodoAlberoViewModel.IconaCartellaCompletata
        };

        Assert.Equal(glifi.Length, glifi.Distinct().Count());
        Assert.All(glifi, g => Assert.Equal(1, g.Length));
        Assert.Equal("", NodoAlberoViewModel.IconaCartellaCompletata); // il cerchio con la spunta
    }

    [Fact]
    public void UnaCartellaCompletata_HaLIconaDelleCompletate()
    {
        var nodo = Nodo(TipoNodo.Cartella);

        nodo.ImpostaScadenza(new DateOnly(2026, 10, 1), completato: true);

        Assert.True(nodo.Completato);
        Assert.Equal(NodoAlberoViewModel.IconaCartellaCompletata, nodo.Icona);
        Assert.Equal("Cartella completata", nodo.DescrizioneStato);
        Assert.Equal("Fattura 1 (completata)", nodo.NomeAccessibile);
    }

    [Fact]
    public void UnaCartellaAperta_HaLIconaNormale_ENessunaDescrizione()
    {
        var nodo = Nodo(TipoNodo.Cartella);

        nodo.ImpostaScadenza(new DateOnly(2026, 10, 1), completato: false);

        Assert.Equal(NodoAlberoViewModel.IconaCartella, nodo.Icona);
        Assert.Equal("", nodo.DescrizioneStato);
        Assert.Equal("Fattura 1", nodo.NomeAccessibile);
    }

    [Fact]
    public void CompletandoEPoiRiaprendo_LIconaSegue()
    {
        var nodo = Nodo(TipoNodo.Cartella);

        nodo.ImpostaScadenza(null, completato: true);
        nodo.ImpostaScadenza(null, completato: false);

        Assert.Equal(NodoAlberoViewModel.IconaCartella, nodo.Icona);
        Assert.False(nodo.Completato);
    }

    [Fact]
    public void CambiandoLoStato_ScattanoLeNotificheDiIconaDescrizioneENome()
    {
        var nodo = Nodo(TipoNodo.Cartella);
        var cambiate = new List<string?>();
        nodo.PropertyChanged += (_, e) => cambiate.Add(e.PropertyName);

        nodo.ImpostaScadenza(null, completato: true);

        Assert.Contains(nameof(NodoAlberoViewModel.Completato), cambiate);
        Assert.Contains(nameof(NodoAlberoViewModel.Icona), cambiate);
        Assert.Contains(nameof(NodoAlberoViewModel.DescrizioneStato), cambiate);
        Assert.Contains(nameof(NodoAlberoViewModel.NomeAccessibile), cambiate);
    }

    [Fact]
    public void RiimpostandoLoStessoStato_NonSiNotificaNulla()
    {
        var nodo = Nodo(TipoNodo.Cartella);
        nodo.ImpostaScadenza(null, completato: true);
        var cambiate = new List<string?>();
        nodo.PropertyChanged += (_, e) => cambiate.Add(e.PropertyName);

        nodo.ImpostaScadenza(new DateOnly(2027, 1, 1), completato: true); // cambia solo la scadenza

        Assert.Empty(cambiate);
        Assert.Equal(new DateOnly(2027, 1, 1), nodo.DataScadenza);
    }

    [Theory]
    [InlineData(TipoNodo.Radice)]
    [InlineData(TipoNodo.Scadenze)]
    [InlineData(TipoNodo.Area)]
    public void SoloLeCartelleHannoLoStatoCompletata(TipoNodo tipo)
    {
        var nodo = Nodo(tipo, "Nodo");
        nodo.ImpostaScadenza(null, completato: true);

        Assert.Equal("", nodo.DescrizioneStato);
        Assert.Equal("Nodo", nodo.NomeAccessibile);
        Assert.NotEqual(NodoAlberoViewModel.IconaCartellaCompletata, nodo.Icona);
    }

    [Fact]
    public void ILNomeAccessibile_PerLoScadenze_ConservaIlNumero()
    {
        var nodo = Nodo(TipoNodo.Scadenze, "Scadenze");
        nodo.NumeroAvvisi = 3;

        Assert.Equal("Scadenze (3)", nodo.NomeAccessibile);
    }
}

public class CartelleCompletateNellAlberoTests : IDisposable
{
    private static readonly DateOnly Oggi = new(2026, 10, 1);

    private readonly ArchivioDiProva _a = new();
    private readonly MainViewModel _vm;

    public CartelleCompletateNellAlberoTests()
    {
        var impostazioni = new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false };
        _vm = new MainViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, impostazioni,
            new AlertService(impostazioni, new TempoFisso(new DateTime(2026, 10, 1, 9, 0, 0))));
    }

    public void Dispose() => _a.Dispose();

    private NodoAlberoViewModel Cartella(string titolo) => _vm.Radici.Single().Aree.Single().Figli.Single(c => c.Nome == titolo);

    /// <summary>Fatture: "Aperta", "Chiusa" (completata) e "Scaduta e chiusa" (scaduta da 5 giorni ma completata).</summary>
    private async Task PreparaAsync()
    {
        var area = await _a.Servizio.CreaAreaAsync("Fatture");
        await _a.CreaCartellaInAreaAsync(area, "Aperta", [], Oggi.AddDays(20));
        var chiusa = await _a.CreaCartellaInAreaAsync(area, "Chiusa", []);
        var vecchia = await _a.CreaCartellaInAreaAsync(area, "Scaduta e chiusa", [], Oggi.AddDays(-5));
        await _a.Servizio.AggiornaCartellaAsync(chiusa.Id, chiusa.Dati with { Completato = true, DataCompletamento = Oggi });
        await _a.Servizio.AggiornaCartellaAsync(vecchia.Id, vecchia.Dati with { Completato = true, DataCompletamento = Oggi });
        await _vm.InizializzaAsync();
    }

    [Fact]
    public async Task All_Apertura_LeCompletateHannoLIconaGiusta_LeAltreNo()
    {
        await PreparaAsync();

        Assert.Equal(NodoAlberoViewModel.IconaCartella, Cartella("Aperta").Icona);
        Assert.Equal(NodoAlberoViewModel.IconaCartellaCompletata, Cartella("Chiusa").Icona);
        Assert.Equal(NodoAlberoViewModel.IconaCartellaCompletata, Cartella("Scaduta e chiusa").Icona);
    }

    [Fact]
    public async Task UnaCompletataScaduta_HaLIconaDelleCompletate_ENonUnAvviso()
    {
        await PreparaAsync();

        var nodo = Cartella("Scaduta e chiusa");

        Assert.Equal(StatoAvviso.Nessuno, nodo.Avviso);
        Assert.Equal(NodoAlberoViewModel.IconaCartellaCompletata, nodo.Icona);
    }

    [Fact]
    public async Task SpuntandoCompletatoNelForm_LIconaCambiaSubito_ESpuntandoDiNuovoTorna()
    {
        await PreparaAsync();
        Cartella("Aperta").IsSelected = true;
        await _vm.CaricamentoFormCompletato;

        _vm.FormCartella!.Completato = true;
        await _vm.FormCartella.AttendiSalvataggioAsync();
        Assert.Equal(NodoAlberoViewModel.IconaCartellaCompletata, Cartella("Aperta").Icona);
        Assert.Equal("Aperta (completata)", Cartella("Aperta").NomeAccessibile);

        _vm.FormCartella.Completato = false;
        await _vm.FormCartella.AttendiSalvataggioAsync();
        Assert.Equal(NodoAlberoViewModel.IconaCartella, Cartella("Aperta").Icona);
    }

    [Fact]
    public async Task LoStatoSiConserva_RileggendoLAlbero_ERinominando()
    {
        await PreparaAsync();

        await _vm.AggiornaCommand.ExecuteAsync(null);
        Assert.Equal(NodoAlberoViewModel.IconaCartellaCompletata, Cartella("Chiusa").Icona);

        Cartella("Chiusa").IsSelected = true;
        await _vm.CaricamentoFormCompletato;
        _vm.FormCartella!.Titolo = "Chiusa e archiviata a mano";
        await _vm.FormCartella.AttendiSalvataggioAsync();
        Assert.Equal(NodoAlberoViewModel.IconaCartellaCompletata, Cartella("Chiusa e archiviata a mano").Icona);
    }

    [Fact]
    public async Task ILNodoScadenze_NonMostraLeCompletate()
    {
        await PreparaAsync();
        _vm.Radici.Single().Figli.Single(f => f.Tipo == TipoNodo.Scadenze).IsSelected = true;
        await _vm.CaricamentoElencoCompletato;

        Assert.Equal(["Aperta"], _vm.ElencoScadenze!.Righe.Select(c => c.Titolo));
    }
}

/// <summary>Come si vede: spunta verde, nome grigio, suggerimento al passaggio del mouse.</summary>
[Collection("WPF")]
public class CartelleCompletateVisteTests
{
    private static readonly DateOnly Oggi = new(2026, 10, 1);

    private static IEnumerable<T> Tutti<T>(DependencyObject? radice) where T : DependencyObject
    {
        if (radice is null)
            yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(radice); i++)
        {
            var figlio = VisualTreeHelper.GetChild(radice, i);
            if (figlio is T trovato)
                yield return trovato;
            foreach (var discendente in Tutti<T>(figlio))
                yield return discendente;
        }
    }

    private static Color Colore(TextBlock t) => ((SolidColorBrush)t.Foreground).Color;

    [Fact]
    public async Task FinestraPrincipale_LeCompletateHannoSpuntaVerdeENomeGrigio_LeAltreSonoNormali()
    {
        using var a = new ArchivioDiProva();
        var area = await a.Servizio.CreaAreaAsync("Fatture");
        await a.CreaCartellaInAreaAsync(area, "Fattura aperta", [], Oggi.AddDays(20));
        var chiusa = await a.CreaCartellaInAreaAsync(area, "Fattura pagata", []);
        await a.Servizio.AggiornaCartellaAsync(chiusa.Id, chiusa.Dati with { Completato = true, DataCompletamento = Oggi });
        var impostazioni = new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false };
        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, impostazioni,
            new AlertService(impostazioni, new TempoFisso(new DateTime(2026, 10, 1, 9, 0, 0))));
        await vm.InizializzaAsync();
        vm.Radici.Single().Aree.Single().IsExpanded = true;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1100, 500, "albero-cartelle-completate");

            var testi = Tutti<TextBlock>(contenuto).ToList();

            // La completata: icona con la spunta in verde, nome grigio, suggerimento.
            var iconaChiusa = testi.Single(t => t.Text == NodoAlberoViewModel.IconaCartellaCompletata);
            Assert.Equal(Color.FromRgb(0x2E, 0x7D, 0x32), Colore(iconaChiusa));
            Assert.Equal("Cartella completata", iconaChiusa.ToolTip);
            var nomeChiusa = testi.Single(t => t.Text == "Fattura pagata");
            Assert.Equal(Color.FromRgb(0x77, 0x77, 0x77), Colore(nomeChiusa));

            // L'aperta: icona e nome come sempre (non verde, non grigio).
            var nomeAperta = testi.Single(t => t.Text == "Fattura aperta");
            Assert.NotEqual(Color.FromRgb(0x77, 0x77, 0x77), Colore(nomeAperta));
            var iconeCartella = testi.Where(t => t.Text == NodoAlberoViewModel.IconaCartella).ToList();
            Assert.Single(iconeCartella);
            Assert.NotEqual(Color.FromRgb(0x2E, 0x7D, 0x32), Colore(iconeCartella[0]));
            Assert.True(string.IsNullOrEmpty(iconeCartella[0].ToolTip as string));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_UnaCompletataSelezionata_NonHaColoriChiariSulFondoDellaSelezione()
    {
        using var a = new ArchivioDiProva();
        var area = await a.Servizio.CreaAreaAsync("Fatture");
        var chiusa = await a.CreaCartellaInAreaAsync(area, "Fattura pagata", []);
        await a.Servizio.AggiornaCartellaAsync(chiusa.Id, chiusa.Dati with { Completato = true, DataCompletamento = Oggi });
        var impostazioni = new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false };
        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, impostazioni);
        await vm.InizializzaAsync();
        vm.Radici.Single().Aree.Single().IsExpanded = true;
        vm.Radici.Single().Aree.Single().Figli.Single().IsSelected = true;
        await vm.CaricamentoFormCompletato;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1100, 500, "albero-completata-selezionata");

            // Il nome compare anche nel form a destra: qui interessano solo i testi dell'albero.
            var albero = Tutti<TreeView>(contenuto).Single();

            // Selezionata: il nome e l'icona NON hanno il grigio e il verde delle completate (valgono i colori della selezione).
            var nome = Tutti<TextBlock>(albero).Single(t => t.Text == "Fattura pagata");
            Assert.NotEqual(Color.FromRgb(0x77, 0x77, 0x77), Colore(nome));
            var icona = Tutti<TextBlock>(albero).Single(t => t.Text == NodoAlberoViewModel.IconaCartellaCompletata);
            Assert.NotEqual(Color.FromRgb(0x2E, 0x7D, 0x32), Colore(icona));

            // Scegliendo un altro nodo, tornano i colori delle completate.
            vm.Radici.Single().IsSelected = true;
            VisteTests.Disegna(contenuto, 1100, 500, "albero-completata-non-selezionata");
            Assert.Equal(Color.FromRgb(0x77, 0x77, 0x77), Colore(Tutti<TextBlock>(albero).Single(t => t.Text == "Fattura pagata")));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_CompletandoLaCartella_LIconaCambiaNellaVista()
    {
        using var a = new ArchivioDiProva();
        await a.CreaCartellaAsync("Fatture", "Da chiudere");
        var impostazioni = new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false };
        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, impostazioni);
        await vm.InizializzaAsync();
        vm.Radici.Single().Aree.Single().IsExpanded = true;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1100, 500, "albero-prima-di-completare");
            Assert.DoesNotContain(Tutti<TextBlock>(contenuto), t => t.Text == NodoAlberoViewModel.IconaCartellaCompletata);

            // Come fa il form quando si spunta "Completato" e si salva.
            vm.Radici.Single().Aree.Single().Figli.Single().ImpostaScadenza(null, completato: true);
            VisteTests.Disegna(contenuto, 1100, 500, "albero-dopo-completato");
            Assert.Single(Tutti<TextBlock>(contenuto), t => t.Text == NodoAlberoViewModel.IconaCartellaCompletata);
            Assert.DoesNotContain(Tutti<TextBlock>(contenuto), t => t.Text == NodoAlberoViewModel.IconaCartella);
        });

        Assert.Empty(errori);
    }
}
