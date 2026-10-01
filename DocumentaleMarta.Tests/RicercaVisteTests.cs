using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

[Collection("WPF")]
public class RicercaVisteTests
{
    private static async Task<(MainViewModel Vm, ArchivioDiProva Archivio)> ArchivioConRicercaAsync()
    {
        var estrattore = new FintoEstrattore(".txt")
        {
            Logica = (p, _) => Task.FromResult(Path.GetFileName(p) switch
            {
                "preventivo cancello.txt" => "Preventivo carpenteria metallica per la società Rossi: cancello scorrevole in acciaio zincato, totale 4.200 euro, consegna entro trenta giorni dalla conferma dell'ordine.",
                "verbale.txt" => "Riunione del martedì: si decide l'acquisto di lamiera zincata per le nuove ringhiere.",
                _ => "altro"
            })
        };
        var a = new ArchivioDiProva(estrattore);
        var fatture = await a.Servizio.CreaAreaAsync("Fatture");
        var riunioni = await a.Servizio.CreaAreaAsync("Riunioni");
        await a.CreaCartellaInAreaAsync(fatture, "Fattura Rossi", ["preventivo cancello.txt", "note.txt"], new DateOnly(2026, 10, 12));
        await a.CreaCartellaInAreaAsync(riunioni, "Verbali 2026", ["verbale.txt"]);
        await a.Indicizzazione!.AttendiSvuotamentoAsync().WaitAsync(TimeSpan.FromSeconds(20));

        var impostazioni = new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false };
        var monitor = new FintoMonitor();
        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, impostazioni,
            new AlertService(30, 7, true, new TempoFisso(new DateTime(2026, 10, 1))),
            a.Ricerca, monitor, new FintoOcr(disponibile: false)) { RitardoRicerca = TimeSpan.Zero };
        await vm.InizializzaAsync();
        monitor.Imposta(new StatoCodaIndicizzazione(3, "relazione.pdf", 0));
        return (vm, a);
    }

    [Fact]
    public async Task FinestraPrincipale_InRicerca_MostraRisultatiConLEstrattoEvidenziato_SenzaErroriDiBinding()
    {
        var (vm, a) = await ArchivioConRicercaAsync();
        using var _ = a;
        vm.TestoRicerca = "zincat";
        await vm.RicercaCompletata;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            VisteTests.Disegna((FrameworkElement)finestra.Content, 1100, 680, "ricerca-risultati");

            var griglia = VisteTests.FindDataGrid((FrameworkElement)finestra.Content, "Trovato")!;
            Assert.Equal(2, griglia.Items.Count);
            Assert.Equal(Visibility.Visible, griglia.Columns.Single(c => (string)c.Header == "Trovato").Visibility);
            Assert.Equal(Visibility.Collapsed, griglia.Columns.Single(c => (string)c.Header == "Scadenza").Visibility);
            Assert.Equal(Visibility.Visible, griglia.Columns.Single(c => (string)c.Header == "Area").Visibility);

            // La parola cercata è in grassetto dentro il testo della riga, e i segnaposto non si vedono.
            var testi = Enumerable.Range(0, griglia.Items.Count)
                .Select(i => griglia.ItemContainerGenerator.ContainerFromIndex(i))
                .SelectMany(riga => Tutti<TextBlock>(riga))
                .Where(t => t.Inlines.OfType<Run>().Any(r => r.FontWeight == FontWeights.SemiBold))
                .ToList();
            Assert.Equal(2, testi.Count);
            // Confronto carattere per carattere: quello "di cultura" di xUnit ignora i caratteri di controllo.
            Assert.All(testi, t =>
            {
                var visibile = new TextRange(t.ContentStart, t.ContentEnd).Text;
                Assert.False(visibile.Contains(RisultatoRicerca.InizioEvidenza) || visibile.Contains(RisultatoRicerca.FineEvidenza));
            });
            Assert.All(testi, t => Assert.Contains("zincat", string.Concat(t.Inlines.OfType<Run>().Where(r => r.FontWeight == FontWeights.SemiBold).Select(r => r.Text)), StringComparison.OrdinalIgnoreCase));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_SenzaRisultati_DiceCheNonHaTrovatoNulla()
    {
        var (vm, a) = await ArchivioConRicercaAsync();
        using var _ = a;
        vm.TestoRicerca = "xyzzyx";
        await vm.RicercaCompletata;

        var errori = VisteTests.InSta(() =>
            VisteTests.Disegna((FrameworkElement)new MainWindow(vm).Content, 1100, 680, "ricerca-vuota"));

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_SenzaRicerca_MostraIlCampo_ELoStatoInFondo_SenzaErroriDiBinding()
    {
        var (vm, a) = await ArchivioConRicercaAsync();
        using var _ = a;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            VisteTests.Disegna((FrameworkElement)finestra.Content, 1100, 680, "ricerca-campo-e-stato");
            var casella = (TextBox)((FrameworkElement)finestra.Content).FindName("CasellaRicerca");
            Assert.NotNull(casella);
        });

        Assert.Empty(errori);
        Assert.Equal("Lettura del testo: «relazione.pdf» e altri 2 in coda", vm.TestoIndicizzazione);
    }

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
}
