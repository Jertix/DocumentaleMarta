using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

/// <summary>Come si vede l'"Archivio completati": ramo dell'albero, pulsanti del form e voci del menu.</summary>
[Collection("WPF")]
public class ArchivioCompletatiVisteTests
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

    /// <summary>Visibile davvero: lo è l'elemento e lo sono tutti i suoi genitori.</summary>
    private static bool Visibile(UIElement elemento)
    {
        for (DependencyObject? o = elemento; o is not null; o = VisualTreeHelper.GetParent(o))
            if (o is UIElement u && u.Visibility != Visibility.Visible)
                return false;
        return true;
    }

    private static IEnumerable<string> TestiVisibili(DependencyObject radice) =>
        Tutti<TextBlock>(radice).Where(Visibile).Select(t => t.Text);

    private static IEnumerable<string?> PulsantiVisibili(DependencyObject radice) =>
        Tutti<Button>(radice).Where(Visibile).Select(b => b.Content as string);

    private static async Task<CartellaDettaglio> CreaAsync(ArchivioDiProva a, bool completata, bool archiviata)
    {
        var area = await a.Servizio.CreaAreaAsync("Fatture");
        var c = await a.CreaCartellaInAreaAsync(area, "Fattura 123", ["f.pdf"]);
        if (completata)
            c = await a.Servizio.AggiornaCartellaAsync(c.Id, c.Dati with { Completato = true, DataCompletamento = Oggi });
        if (archiviata)
            await a.Servizio.ArchiviaCartellaAsync(c.Id);
        return (await a.Servizio.CaricaCartellaAsync(c.Id))!;
    }

    // ---------- Il form della cartella ----------

    [Fact]
    public async Task Form_UnaCartellaAperta_NonHaNeArchiviaNeRipristina()
    {
        using var a = new ArchivioDiProva();
        var form = new CartellaFormViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, await CreaAsync(a, completata: false, archiviata: false));

        var errori = VisteTests.InSta(() =>
        {
            var vista = new CartellaView { DataContext = form };
            VisteTests.Disegna(vista, 780, 460, "form-aperta");

            Assert.DoesNotContain("Archivia", PulsantiVisibili(vista));
            Assert.DoesNotContain("Ripristina nell'area", PulsantiVisibili(vista));
            Assert.DoesNotContain(TestiVisibili(vista), t => t.Contains("archiviata in"));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task Form_UnaCartellaCompletata_OffreArchivia()
    {
        using var a = new ArchivioDiProva();
        var form = new CartellaFormViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, await CreaAsync(a, completata: true, archiviata: false));

        var errori = VisteTests.InSta(() =>
        {
            var vista = new CartellaView { DataContext = form };
            VisteTests.Disegna(vista, 780, 460, "form-completata-archivia");

            Assert.Contains("Archivia", PulsantiVisibili(vista));
            Assert.DoesNotContain("Ripristina nell'area", PulsantiVisibili(vista));
            var pulsante = Tutti<Button>(vista).Single(b => b.Content is "Archivia");
            Assert.Same(form.ArchiviaCommand, pulsante.Command);
            Assert.Contains("non sposta nessun file", (string)pulsante.ToolTip);

            // Togliendo la spunta il pulsante sparisce senza aspettare il salvataggio.
            form.Completato = false;
            VisteTests.Disegna(vista, 780, 460, "form-riaperta");
            Assert.DoesNotContain("Archivia", PulsantiVisibili(vista));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task Form_UnaCartellaArchiviata_OffreRipristina_EDiceCheEArchiviata()
    {
        using var a = new ArchivioDiProva();
        var form = new CartellaFormViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, await CreaAsync(a, completata: true, archiviata: true));

        var errori = VisteTests.InSta(() =>
        {
            var vista = new CartellaView { DataContext = form };
            VisteTests.Disegna(vista, 780, 460, "form-archiviata");

            Assert.Contains("Ripristina nell'area", PulsantiVisibili(vista));
            Assert.DoesNotContain("Archivia", PulsantiVisibili(vista));
            Assert.Same(form.RipristinaCommand, Tutti<Button>(vista).Single(b => b.Content is "Ripristina nell'area").Command);
            Assert.Contains("·  archiviata in «Archivio completati»", TestiVisibili(vista));
        });

        Assert.Empty(errori);
    }

    // ---------- L'albero ----------

    private static async Task<MainViewModel> PrincipaleConArchivioAsync(ArchivioDiProva a)
    {
        var fatture = await a.Servizio.CreaAreaAsync("Fatture");
        var inps = await a.Servizio.CreaAreaAsync("INPS");
        await a.CreaCartellaInAreaAsync(fatture, "Aperta", ["x.pdf"], Oggi.AddDays(20));
        foreach (var (area, titolo) in new[] { (fatture, "Pagata A"), (fatture, "Pagata B"), (inps, "Contributi 2025") })
        {
            var c = await a.CreaCartellaInAreaAsync(area, titolo, [titolo + ".pdf"]);
            await a.Servizio.AggiornaCartellaAsync(c.Id, c.Dati with { Completato = true, DataCompletamento = Oggi });
            await a.Servizio.ArchiviaCartellaAsync(c.Id);
        }

        var impostazioni = new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false };
        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, impostazioni,
            new AlertService(impostazioni, new TempoFisso(new DateTime(2026, 10, 1, 9, 0, 0))));
        await vm.InizializzaAsync();
        return vm;
    }

    [Fact]
    public async Task FinestraPrincipale_ConLArchivioAperto_MostraGruppiECartelleArchiviate()
    {
        using var a = new ArchivioDiProva();
        var vm = await PrincipaleConArchivioAsync(a);
        var radice = vm.Radici.Single();
        var archivio = radice.Figli.Last();
        archivio.IsExpanded = true;
        foreach (var gruppo in archivio.Figli)
            gruppo.IsExpanded = true;
        radice.Aree.First().IsExpanded = true;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1180, 560, "albero-archivio-aperto");

            var albero = Tutti<TreeView>(contenuto).Single();
            var testi = Tutti<TextBlock>(albero).Select(t => t.Text).ToList();

            Assert.Contains("Archivio completati (3)", testi);
            Assert.Contains(NodoAlberoViewModel.IconaArchivio, testi);
            Assert.Contains("Pagata A", testi);                       // nel gruppo dell'archivio
            Assert.Contains("Contributi 2025", testi);
            Assert.Contains("Aperta", testi);                         // nell'area
            Assert.Equal(2, testi.Count(t => t == "Fatture"));        // l'area e il suo gruppo nell'archivio
            Assert.Equal(3, testi.Count(t => t == NodoAlberoViewModel.IconaCartellaCompletata));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_SelezionandoLArchivio_MostraRiepilogoEGriglia()
    {
        using var a = new ArchivioDiProva();
        var vm = await PrincipaleConArchivioAsync(a);
        vm.Radici.Single().Figli.Last().IsSelected = true;
        await vm.CaricamentoElencoCompletato;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1180, 560, "archivio-selezionato");

            Assert.Contains("Archivio completati", TestiVisibili(contenuto));
            Assert.Contains("Cartelle completate", TestiVisibili(contenuto));
            Assert.Contains("3 cartelle  ·  3 documenti", TestiVisibili(contenuto));
            Assert.Equal(3, VisteTests.FindDataGrid(contenuto, "Nome file")!.Items.Count);
            // Senza cartella su disco: niente "Apri in Esplora file" per l'archivio.
            Assert.DoesNotContain("Apri in Esplora file", PulsantiVisibili(contenuto));
        });

        Assert.Empty(errori);
    }

    [Fact]
    public async Task FinestraPrincipale_UnArchivioVuoto_DiceCosaFare()
    {
        using var a = new ArchivioDiProva();
        await a.CreaCartellaAsync("Fatture", "Aperta");
        var vm = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell, new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false });
        await vm.InizializzaAsync();
        vm.Radici.Single().Figli.Last().IsSelected = true;
        await vm.CaricamentoElencoCompletato;

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1180, 560, "archivio-vuoto");

            Assert.Contains(TestiVisibili(contenuto), t => t.StartsWith("Nessuna cartella archiviata"));
        });

        Assert.Empty(errori);
    }

    // ---------- Il menu del tasto destro ----------

    /// <summary>Quali voci dell'archiviazione compaiono nel menu quando è selezionato il nodo indicato.</summary>
    private static List<string> VociVisibili(MainWindow finestra, MainViewModel vm)
    {
        var albero = (TreeView)finestra.FindName("Albero");
        var menu = albero.ContextMenu!;
        menu.DataContext = vm; // quando il menu si apre, l'XAML gli dà il modello della finestra
        return menu.Items.OfType<MenuItem>()
            .Where(m => m.Header is "Archivia" or "Ripristina nell'area" or "Archivia le cartelle completate")
            .Where(m => m.Visibility == Visibility.Visible)
            .Select(m => (string)m.Header)
            .ToList();
    }

    [Fact]
    public async Task IlMenu_OffreLeVociGiustePerOgniNodo()
    {
        using var a = new ArchivioDiProva();
        var vm = await PrincipaleConArchivioAsync(a);
        var chiusa = await a.Servizio.CreaAreaAsync("Altra");
        var daChiudere = await a.CreaCartellaInAreaAsync(chiusa, "Pagata ma non archiviata", ["z.pdf"]);
        await a.Servizio.AggiornaCartellaAsync(daChiudere.Id, daChiudere.Dati with { Completato = true, DataCompletamento = Oggi });
        await vm.AggiornaCommand.ExecuteAsync(null);

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new MainWindow(vm);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 1180, 560, "menu-prima");

            vm.Radici.Single().IsSelected = true;
            Assert.Equal(["Archivia le cartelle completate"], VociVisibili(finestra, vm));

            vm.Radici.Single().Aree.First().IsSelected = true;
            Assert.Equal(["Archivia le cartelle completate"], VociVisibili(finestra, vm));

            vm.Radici.Single().Aree.Single(x => x.Nome == "Altra").Figli.Single().IsSelected = true;
            Assert.Equal(["Archivia"], VociVisibili(finestra, vm));

            vm.Radici.Single().Aree.Single(x => x.Nome == "Fatture").Figli.Single(c => c.Nome == "Aperta").IsSelected = true;
            Assert.Empty(VociVisibili(finestra, vm)); // una cartella aperta non si archivia

            var archiviataAttuale = vm.Radici.Single().Figli.Last().Figli.First().Figli.First();
            archiviataAttuale.IsSelected = true;
            Assert.Equal(["Ripristina nell'area"], VociVisibili(finestra, vm));

            vm.Radici.Single().Figli.Last().IsSelected = true;
            Assert.Empty(VociVisibili(finestra, vm)); // il nodo "Archivio completati" non ha voci di archiviazione

            vm.Radici.Single().Figli.Last().Figli.First().IsSelected = true;
            Assert.Empty(VociVisibili(finestra, vm)); // e neanche il gruppo di un'area
        });

        Assert.Empty(errori);
    }
}
