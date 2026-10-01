using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

/// <summary>Come si vede il programma con il tema chiaro e con quello scuro (e le immagini, se DOCUMENTALE_TEST_IMMAGINI indica una cartella).</summary>
[Collection("WPF")]
public class AspettoVisteTests
{
    private static string Nome(string base_, bool scuro) => $"{base_}-{(scuro ? "scuro" : "chiaro")}";

    private static Color Colore(Brush? pennello) => ((SolidColorBrush)pennello!).Color;

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinestraPrincipale_ConLeScadenze_SiVedeBeneInChiaroEInScuro(bool scuro)
    {
        using var a = new ArchivioDiProva();
        var (vm, _) = await VisteTests.ArchivioConAvvisiAsync(a);
        vm.Radici[0].IsSelected = true;
        await vm.CaricamentoElencoCompletato;

        var errori = VisteTests.InSta(() =>
        {
            AspettoDiProva.Applica(scuro ? TemaApp.Scuro : TemaApp.Chiaro);
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1180, 680, Nome("tema-radice", scuro));

            // La griglia ha lo sfondo del tema, e le righe in avviso il colore del tema (arancione e rosso, chiari o scuri).
            var griglia = VisteTests.FindDataGrid(contenuto)!;
            Assert.Equal(Colore(finestra.FindResource("SfondoSuperficie") as Brush), Colore(griglia.Background));
            var righe = Tutti<DataGridRow>(griglia).ToList();
            var arancione = righe.Where(r => ((DocumentoElencoViewModel)r.DataContext).Avviso == StatoAvviso.Arancione).ToList();
            var rosse = righe.Where(r => ((DocumentoElencoViewModel)r.DataContext).Avviso == StatoAvviso.Rosso).ToList();
            Assert.NotEmpty(arancione);
            Assert.NotEmpty(rosse);
            Assert.All(arancione, r => Assert.Equal(Colore(finestra.FindResource("SfondoArancione") as Brush), Colore(r.Background)));
            Assert.All(rosse, r => Assert.Equal(Colore(finestra.FindResource("SfondoRosso") as Brush), Colore(r.Background)));

            // Il tema scuro ha davvero sfondi scuri nelle righe colorate (e il chiaro, chiari).
            var luminosita = Colore(arancione[0].Background) is var c ? (c.R + c.G + c.B) / 3.0 : 0;
            Assert.Equal(scuro, luminosita < 128);
        });

        Assert.True(errori.Count == 0, string.Join("\n", errori.Distinct()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinestraPrincipale_ConUnaCartellaAperta_SiVedeIlFormInChiaroEInScuro(bool scuro)
    {
        using var a = new ArchivioDiProva();
        var (vm, _) = await VisteTests.ArchivioConAvvisiAsync(a);
        vm.Radici[0].Aree.First().Figli.First().IsSelected = true;
        await vm.CaricamentoFormCompletato;

        var errori = VisteTests.InSta(() =>
        {
            AspettoDiProva.Applica(scuro ? TemaApp.Scuro : TemaApp.Chiaro);
            var finestra = new MainWindow(vm);
            VisteTests.Disegna((FrameworkElement)finestra.Content, 1180, 680, Nome("tema-cartella", scuro));
        });

        Assert.True(errori.Count == 0, string.Join("\n", errori.Distinct()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinestraPrincipale_NodoScadenze_ConIlPromemoriaDelBackupEIFiltri_SiVedeBeneInChiaroEInScuro(bool scuro)
    {
        using var a = new ArchivioDiProva();
        var (vm, _) = await VisteTests.ArchivioConAvvisiAsync(a);
        vm.Radici[0].Figli.Single(f => f.Tipo == TipoNodo.Scadenze).IsSelected = true;
        await vm.CaricamentoElencoCompletato;
        vm.PannelloFiltriAperto = true;

        var errori = VisteTests.InSta(() =>
        {
            AspettoDiProva.Applica(scuro ? TemaApp.Scuro : TemaApp.Chiaro);
            var finestra = new MainWindow(vm);
            VisteTests.Disegna((FrameworkElement)finestra.Content, 1180, 680, Nome("tema-scadenze-filtri", scuro));
        });

        Assert.True(errori.Count == 0, string.Join("\n", errori.Distinct()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImpostazioniDialog_ConLaSceltaDelTema_SiVedeBeneInChiaroEInScuro(bool scuro)
    {
        var modello = new ImpostazioniViewModel(
            new ImpostazioniApp { PercorsoRadice = @"C:\Documentale", CartellaBackup = @"E:\Backup", Tema = scuro ? TemaApp.Scuro : TemaApp.Chiaro },
            @"C:\Users\Marta\AppData\Roaming\DocumentaleMarta\impostazioni.json", backupDisponibile: true);

        var errori = VisteTests.InSta(() =>
        {
            AspettoDiProva.Applica(modello.Tema);
            var finestra = new ImpostazioniDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 580, 960, Nome("tema-impostazioni", scuro));

            // I tre pulsanti di scelta ci sono e quello del tema attuale è acceso.
            var scelte = Tutti<RadioButton>(contenuto).Where(r => r.GroupName == "Tema").ToList();
            Assert.Equal(["Chiaro", "Scuro", "Come Windows"], scelte.Select(r => (string)r.Content));
            Assert.Equal(scuro ? "Scuro" : "Chiaro", scelte.Single(r => r.IsChecked == true).Content);

            // Sceglierne un altro cambia il modello (e, con l'anteprima collegata, il programma).
            scelte.Single(r => (string)r.Content == "Come Windows").IsChecked = true;
            Assert.Equal(TemaApp.ComeWindows, modello.Tema);
            Assert.False(scelte.Single(r => r.Content as string == (scuro ? "Scuro" : "Chiaro")).IsChecked);
        });

        Assert.True(errori.Count == 0, string.Join("\n", errori.Distinct()));
    }
}
