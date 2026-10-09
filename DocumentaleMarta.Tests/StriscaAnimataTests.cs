using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

/// <summary>Quando passa un personaggio nella striscia e chi: tempi, scelta e velocità.</summary>
public class PianificatoreStrisciaTests
{
    [Fact]
    public void IlPrimoPassaggio_ArrivaTra20E40Secondi()
    {
        var pianificatore = new PianificatoreStriscia(new Random(7));

        for (var i = 0; i < 200; i++)
            Assert.InRange(pianificatore.ProssimaAttesa(primaVolta: true), TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(40));
    }

    [Fact]
    public void GliAltriPassaggi_ArrivanoTra3E6Minuti()
    {
        var pianificatore = new PianificatoreStriscia(new Random(7));

        for (var i = 0; i < 200; i++)
            Assert.InRange(pianificatore.ProssimaAttesa(primaVolta: false), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(6));
    }

    [Fact]
    public void LeAttese_NonSonoTutteUguali()
    {
        var pianificatore = new PianificatoreStriscia(new Random(3));

        var attese = Enumerable.Range(0, 50).Select(_ => pianificatore.ProssimaAttesa(false)).Distinct().Count();

        Assert.True(attese > 40);
    }

    [Fact]
    public void IlPersonaggio_NonSiRipeteDueVolteDiFila()
    {
        var pianificatore = new PianificatoreStriscia(new Random(11));

        var passaggi = Enumerable.Range(0, 500).Select(_ => pianificatore.ProssimoPassaggio().Personaggio).ToList();

        for (var i = 1; i < passaggi.Count; i++)
            Assert.NotEqual(passaggi[i - 1], passaggi[i]);
    }

    [Fact]
    public void ConMoltiPassaggi_CompaionoTuttiIPersonaggi_EEntrambeLeDirezioni()
    {
        var pianificatore = new PianificatoreStriscia(new Random(5));

        var passaggi = Enumerable.Range(0, 300).Select(_ => pianificatore.ProssimoPassaggio()).ToList();

        Assert.Equal(Enum.GetValues<PersonaggioStriscia>().Order(), passaggi.Select(p => p.Personaggio).Distinct().Order());
        Assert.Contains(passaggi, p => p.DaDestra);
        Assert.Contains(passaggi, p => !p.DaDestra);
    }

    [Fact]
    public void UnPassaggioChiestoAMano_ResteIlPersonaggioChiesto_ELaSceltaSuccessivaNonLoRipete()
    {
        for (var seme = 0; seme < 50; seme++)
        {
            var pianificatore = new PianificatoreStriscia(new Random(seme));

            Assert.Equal(PersonaggioStriscia.Gatto, pianificatore.Passaggio(PersonaggioStriscia.Gatto).Personaggio);
            Assert.NotEqual(PersonaggioStriscia.Gatto, pianificatore.ProssimoPassaggio().Personaggio);
        }
    }

    [Fact]
    public void IlCaneCorre_PiuInFrettaDelGatto_EDellOmino()
    {
        Assert.True(PianificatoreStriscia.Velocita(PersonaggioStriscia.Cane) > PianificatoreStriscia.Velocita(PersonaggioStriscia.Gatto));
        Assert.True(PianificatoreStriscia.Velocita(PersonaggioStriscia.Cane) > PianificatoreStriscia.Velocita(PersonaggioStriscia.Omino));
        Assert.All(Enum.GetValues<PersonaggioStriscia>(), p => Assert.True(PianificatoreStriscia.Velocita(p) > 0));
    }

    [Fact]
    public void LaDurataDellAttraversamento_CresceConLaLarghezza_EScendeConLaVelocita()
    {
        var corto = PianificatoreStriscia.DurataAttraversamento(PersonaggioStriscia.Omino, 200, 22);
        var lungo = PianificatoreStriscia.DurataAttraversamento(PersonaggioStriscia.Omino, 400, 22);
        var cane = PianificatoreStriscia.DurataAttraversamento(PersonaggioStriscia.Cane, 270, 48);
        var gatto = PianificatoreStriscia.DurataAttraversamento(PersonaggioStriscia.Gatto, 270, 40);

        Assert.True(lungo > corto);
        Assert.True(cane < gatto);
        Assert.InRange(cane, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6)); // con la colonna di partenza (270 px) è una corsetta
        Assert.InRange(gatto, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
    }
}

/// <summary>L'impostazione «Animazioni»: acceso di base, si salva, vale subito e arriva alla striscia.</summary>
public class AnimazioniImpostazioniTests : IDisposable
{
    private readonly ArchivioDiProva _a = new();
    private readonly ImpostazioniService _servizio;
    private readonly ImpostazioniApp _impostazioni;

    public AnimazioniImpostazioniTests()
    {
        _servizio = new ImpostazioniService(_a.Tmp.Combina("impostazioni", "impostazioni.json"));
        _impostazioni = new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false };
    }

    public void Dispose() => _a.Dispose();

    private MainViewModel Principale() => new(
        _a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni,
        new AlertService(_impostazioni, new TempoFisso(new DateTime(2026, 10, 9, 9, 0, 0))),
        servizioImpostazioni: _servizio);

    [Fact]
    public void Di_Base_LeAnimazioniSonoAccese() => Assert.True(new ImpostazioniApp().AnimazioniAttive);

    [Fact]
    public void UnFileVecchio_SenzaLaScelta_LaTrovaAccesa()
    {
        var percorso = _a.Tmp.CreaFile("vecchio.json", "{ \"NomeRadice\": \"Archivio\", \"SogliaRossaGiorni\": 5, \"SogliaArancioneGiorni\": 20 }");

        Assert.True(new ImpostazioniService(percorso).Carica().AnimazioniAttive);
    }

    [Fact]
    public void LaScelta_SiSalvaESiRilegge()
    {
        _servizio.Salva(new ImpostazioniApp { AnimazioniAttive = false });

        var riletto = _servizio.Carica();

        Assert.False(riletto.AnimazioniAttive);
        Assert.Contains("\"AnimazioniAttive\": false", File.ReadAllText(_servizio.PercorsoFile));
    }

    [Fact]
    public void ClonaECopiaDa_PortanoLaScelta()
    {
        var spente = new ImpostazioniApp { AnimazioniAttive = false };

        Assert.False(spente.Clona().AnimazioniAttive);

        var viva = new ImpostazioniApp();
        viva.CopiaDa(spente);
        Assert.False(viva.AnimazioniAttive);
        Assert.Equal(JsonSerializer.Serialize(spente), JsonSerializer.Serialize(viva));
    }

    [Fact]
    public void LaFinestraDelleImpostazioni_ParteDallaScelta_ELaRestituisce()
    {
        var modello = new ImpostazioniViewModel(new ImpostazioniApp { AnimazioniAttive = false }, @"C:\x\impostazioni.json");
        Assert.False(modello.AnimazioniAttive);
        Assert.False(modello.Costruisci().AnimazioniAttive);

        modello.AnimazioniAttive = true;

        Assert.True(modello.Costruisci().AnimazioniAttive);
        Assert.True(modello.PuoSalvare); // una casella non può impedire il salvataggio
        Assert.Contains("3-6 minuti", modello.SuggerimentoAnimazioni);
    }

    [Fact]
    public async Task SpegnendoleNelleImpostazioni_IlPrincipaleLoSaSubito_ESiSalva()
    {
        var principale = Principale();
        var notificate = new List<string?>();
        principale.PropertyChanged += (_, e) => notificate.Add(e.PropertyName);
        Assert.True(principale.AnimazioniAttive);
        _a.Dialog.RispondiImpostazioni(m => m.AnimazioniAttive = false);

        await principale.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.False(principale.AnimazioniAttive);
        Assert.Contains(nameof(MainViewModel.AnimazioniAttive), notificate);
        Assert.False(_impostazioni.AnimazioniAttive);
        Assert.False(new ImpostazioniService(_servizio.PercorsoFile).Carica().AnimazioniAttive);
    }

    [Fact]
    public async Task AnnullandoLeImpostazioni_LeAnimazioniRestanoComeSono()
    {
        var principale = Principale();
        _a.Dialog.RispondiImpostazioni(null); // l'utente annulla

        await principale.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.True(principale.AnimazioniAttive);
    }
}

/// <summary>La striscia e i suoi personaggi, disegnati davvero (senza mostrare finestre).</summary>
[Collection("WPF")]
public class StriscaAnimataVisteTests
{
    /// <summary>Una striscia già disposta alla larghezza indicata, con le animazioni di Windows «attive» e un pianificatore dal seme noto.</summary>
    private static StriscaAnimata Striscia(double larghezza = 270)
    {
        var striscia = new StriscaAnimata
        {
            AnimazioniDiSistema = () => true,
            Pianificatore = new PianificatoreStriscia(new Random(4))
        };
        VisteTests.Disegna(striscia, larghezza, 44, "striscia");
        return striscia;
    }

    private static IEnumerable<T> Discendenti<T>(DependencyObject radice) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(radice); i++)
        {
            var figlio = VisualTreeHelper.GetChild(radice, i);
            if (figlio is T trovato)
                yield return trovato;
            foreach (var nipote in Discendenti<T>(figlio))
                yield return nipote;
        }
    }

    /// <summary>I pixel del personaggio disegnato a 4 volte la grandezza, per confrontare le pose.</summary>
    private static byte[] Pixel(FrameworkElement elemento)
    {
        elemento.Measure(new Size(elemento.Width, elemento.Height));
        elemento.Arrange(new Rect(0, 0, elemento.Width, elemento.Height));
        elemento.UpdateLayout();

        var larghezza = (int)(elemento.Width * 4);
        var altezza = (int)(elemento.Height * 4);
        var bitmap = new RenderTargetBitmap(larghezza, altezza, 384, 384, PixelFormats.Pbgra32);
        bitmap.Render(elemento);

        var pixel = new byte[larghezza * altezza * 4];
        bitmap.CopyPixels(pixel, larghezza * 4, 0);
        return pixel;
    }

    [Fact]
    public void LaStriscia_SiDisegna_InChiaroEInScuro_SenzaErroriDiBinding()
    {
        var errori = VisteTests.InSta(() =>
        {
            var striscia = Striscia(270);
            Assert.Equal(Visibility.Visible, striscia.Visibility);
            Assert.Equal(0, striscia.PersonaggiPresenti);
            Assert.Null(striscia.PersonaggioInCorso);

            AspettoDiProva.Applica(TemaApp.Scuro);
            VisteTests.Disegna(striscia, 270, 44, "striscia-scuro");
        });

        Assert.Empty(errori);
    }

    [Theory]
    [InlineData(PersonaggioStriscia.Cane)]
    [InlineData(PersonaggioStriscia.Omino)]
    [InlineData(PersonaggioStriscia.Gatto)]
    [InlineData(PersonaggioStriscia.Uccellino)]
    [InlineData(PersonaggioStriscia.Operaio)]
    public void OgniPersonaggio_SiDisegna_ELaPosaCambiaConLaFase(PersonaggioStriscia personaggio)
    {
        var errori = VisteTests.InSta(() =>
        {
            var figura = StriscaAnimata.Crea(personaggio);

            Assert.True(figura.Width > 0 && figura.Height > 0 && figura.Height <= 36);
            Assert.True(figura.DurataPasso > TimeSpan.Zero);
            Assert.True(figura.AltezzaDaTerra >= 0);

            figura.Fase = 0;
            var inizio = Pixel(figura);
            Assert.Contains(inizio, b => b != 0); // il disegno c'è (non è trasparente)

            figura.Fase = 0.25;
            var quarto = Pixel(figura);
            figura.Fase = 0.75;
            var tre = Pixel(figura);

            Assert.False(inizio.SequenceEqual(quarto), "la posa a un quarto di passo è uguale a quella iniziale");
            Assert.False(quarto.SequenceEqual(tre), "le pose a un quarto e a tre quarti di passo sono uguali");

            // Un passo intero ritorna alla posa di partenza: il movimento si ripete senza scatti.
            figura.Fase = 1;
            Assert.True(inizio.SequenceEqual(Pixel(figura)), "dopo un passo intero la posa non è quella di partenza");
        });

        Assert.Empty(errori);
    }

    [Theory]
    [InlineData(PersonaggioStriscia.Cane, false)]
    [InlineData(PersonaggioStriscia.Omino, false)]
    [InlineData(PersonaggioStriscia.Gatto, false)]
    [InlineData(PersonaggioStriscia.Uccellino, true)]
    [InlineData(PersonaggioStriscia.Operaio, false)]
    public void FaiPassare_AggiungeUnSoloPersonaggio_EFinitoLaScenaEVuota(PersonaggioStriscia personaggio, bool volaAlto)
    {
        var errori = VisteTests.InSta(() =>
        {
            var striscia = Striscia();

            Assert.True(striscia.FaiPassare(personaggio));
            Assert.Equal(1, striscia.PersonaggiPresenti);
            Assert.Equal(personaggio, striscia.PersonaggioInCorso);

            var figura = striscia.Figura!;
            Assert.Equal(volaAlto, figura.AltezzaDaTerra > 0);
            Assert.True(Canvas.GetTop(figura) >= 0, "il personaggio esce dalla striscia in alto");

            // Parte fuori dal lato da cui arriva, ed è rivolto nel verso in cui va.
            var gruppo = (TransformGroup)figura.RenderTransform;
            var specchio = (ScaleTransform)gruppo.Children[0];
            var spostamento = (TranslateTransform)gruppo.Children[1];
            if (specchio.ScaleX < 0)
                Assert.Equal(striscia.ActualWidth, spostamento.X, 3); // da destra a sinistra
            else
                Assert.Equal(-figura.Width, spostamento.X, 3); // da sinistra a destra

            // Mentre uno passa, un secondo non parte.
            Assert.False(striscia.FaiPassare(PersonaggioStriscia.Cane));
            Assert.Equal(1, striscia.PersonaggiPresenti);

            striscia.TerminaPassaggio();
            Assert.Equal(0, striscia.PersonaggiPresenti);
            Assert.Null(striscia.PersonaggioInCorso);

            // Dopo, può passarne un altro.
            Assert.True(striscia.FaiPassare());
            striscia.TerminaPassaggio();
        });

        Assert.Empty(errori);
    }

    [Fact]
    public void ConLaStrisciaTroppoStretta_NonPassaNessuno()
    {
        var errori = VisteTests.InSta(() =>
        {
            var striscia = Striscia(larghezza: 60);

            Assert.False(striscia.FaiPassare(PersonaggioStriscia.Omino));
            Assert.Equal(0, striscia.PersonaggiPresenti);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public void Spenta_LaStrisciaSparisce_ESeUnoPassavaSiFerma()
    {
        var errori = VisteTests.InSta(() =>
        {
            var striscia = Striscia();
            striscia.FaiPassare(PersonaggioStriscia.Cane);

            striscia.Attiva = false;

            Assert.Equal(Visibility.Collapsed, striscia.Visibility);
            Assert.Equal(0, striscia.PersonaggiPresenti);

            striscia.Attiva = true;
            Assert.Equal(Visibility.Visible, striscia.Visibility);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public void ConLeAnimazioniDiWindowsSpente_LaStrisciaNonCompare()
    {
        var errori = VisteTests.InSta(() =>
        {
            var striscia = Striscia();

            striscia.AnimazioniDiSistema = () => false;
            Assert.Equal(Visibility.Collapsed, striscia.Visibility);

            striscia.AnimazioniDiSistema = () => true;
            Assert.Equal(Visibility.Visible, striscia.Visibility);
        });

        Assert.Empty(errori);
    }

    [Fact]
    public void IlDoppioClic_FaPassareQualcuno_IlClicSingoloNo()
    {
        var errori = VisteTests.InSta(() =>
        {
            var striscia = Striscia();
            var bordo = (Border)striscia.Content;

            Clic(bordo, 1);
            Assert.Equal(0, striscia.PersonaggiPresenti);

            Clic(bordo, 2);
            Assert.Equal(1, striscia.PersonaggiPresenti);

            striscia.TerminaPassaggio();
        });

        Assert.Empty(errori);
    }

    /// <summary>Simula un clic del tasto sinistro sul bordo della striscia, con il numero di clic indicato.</summary>
    private static void Clic(Border bordo, int numeroClic)
    {
        var evento = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            Source = bordo
        };
        // Il numero di clic lo imposta Windows: dalla prova lo si scrive a mano.
        typeof(MouseButtonEventArgs).GetProperty(nameof(MouseButtonEventArgs.ClickCount), BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(evento, numeroClic);
        bordo.RaiseEvent(evento);
    }

    [Fact]
    public async Task LaFinestraPrincipale_HaLaStriscia_SottoLAlbero_ESparisceSeLeAnimazioniSonoSpente()
    {
        using var a = new ArchivioDiProva();
        await a.Servizio.CreaAreaAsync("Fatture");

        var accese = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell,
            new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false });
        var spente = new MainViewModel(a.Servizio, a.Files, a.Dialog, a.Shell,
            new ImpostazioniApp { PercorsoRadice = a.Radice, RiepilogoAvvio = false, AnimazioniAttive = false });
        await accese.InizializzaAsync();
        await spente.InizializzaAsync();

        var errori = VisteTests.InSta(() =>
        {
            var finestraAccesa = new MainWindow(accese);
            var contenuto = (FrameworkElement)finestraAccesa.Content;
            VisteTests.Disegna(contenuto, 1100, 680, "finestra-striscia");

            var striscia = Discendenti<StriscaAnimata>(contenuto).Single();
            var albero = Discendenti<TreeView>(contenuto).Single();
            Assert.Equal(Visibility.Visible, striscia.Visibility);
            Assert.True(striscia.ActualWidth > 100);
            Assert.True(striscia.TranslatePoint(new Point(0, 0), contenuto).Y >= albero.TranslatePoint(new Point(0, albero.ActualHeight), contenuto).Y - 1,
                "la striscia deve stare sotto l'albero");
            Assert.Equal(albero.ActualWidth, striscia.ActualWidth, 1);

            var finestraSpenta = new MainWindow(spente);
            var altro = (FrameworkElement)finestraSpenta.Content;
            VisteTests.Disegna(altro, 1100, 680, "finestra-striscia-spenta");
            Assert.Equal(Visibility.Collapsed, Discendenti<StriscaAnimata>(altro).Single().Visibility);
        });

        Assert.Empty(errori);
    }

    /// <summary>
    /// Cinque strisce, una per personaggio, con il personaggio fermo a metà strada: per vedere a occhio dimensioni, posizione sul
    /// terreno e colori nella striscia vera. Si scrive solo se DOCUMENTALE_TEST_IMMAGINI indica una cartella.
    /// </summary>
    [Fact]
    public void LeStrisce_ConIPersonaggiAMetaStrada_SiDisegnano_SenzaErroriDiBinding()
    {
        var errori = VisteTests.InSta(() =>
        {
            foreach (var (tema, nome) in new[] { (TemaApp.Chiaro, "strisce-chiaro"), (TemaApp.Scuro, "strisce-scuro") })
            {
                AspettoDiProva.Applica(tema);
                var pila = new StackPanel { Width = 270 };
                var strisce = Enum.GetValues<PersonaggioStriscia>().Select(_ => Striscia()).ToList();
                foreach (var striscia in strisce)
                    pila.Children.Add(striscia);
                VisteTests.Disegna(pila, 270, strisce.Count * 44, nome + "-vuote");

                for (var i = 0; i < strisce.Count; i++)
                {
                    Assert.True(strisce[i].FaiPassare((PersonaggioStriscia)i));
                    var figura = strisce[i].Figura!;
                    var spostamento = (TranslateTransform)((TransformGroup)figura.RenderTransform).Children[1];
                    spostamento.BeginAnimation(TranslateTransform.XProperty, null); // lo si ferma a metà strada
                    spostamento.X = 110;
                    figura.Ferma();
                    figura.Fase = 0.25;
                }

                VisteTests.Disegna(pila, 270, strisce.Count * 44, nome);
            }
        });

        Assert.Empty(errori);
    }

    /// <summary>
    /// Il foglio delle pose di ogni personaggio (sei pose per passo), in chiaro e in scuro: per guardare a occhio che le sagome
    /// somiglino a quel che devono. Si scrive solo se DOCUMENTALE_TEST_IMMAGINI indica una cartella.
    /// </summary>
    [Fact]
    public void FoglioDellePose_SiDisegna_SenzaErroriDiBinding()
    {
        var errori = VisteTests.InSta(() =>
        {
            foreach (var (tema, nome) in new[] { (TemaApp.Chiaro, "pose-chiaro"), (TemaApp.Scuro, "pose-scuro") })
            {
                AspettoDiProva.Applica(tema);
                var foglio = new Grid { Width = 6 * 150, Height = 5 * 96 };
                foglio.SetResourceReference(Panel.BackgroundProperty, "SfondoPannello");
                for (var r = 0; r < 5; r++)
                    foglio.RowDefinitions.Add(new RowDefinition());
                for (var c = 0; c < 6; c++)
                    foglio.ColumnDefinitions.Add(new ColumnDefinition());

                foreach (var personaggio in Enum.GetValues<PersonaggioStriscia>())
                {
                    for (var posa = 0; posa < 6; posa++)
                    {
                        var figura = StriscaAnimata.Crea(personaggio);
                        figura.Fase = posa / 6.0;
                        var cella = new Viewbox { Child = figura, Margin = new Thickness(14) };
                        Grid.SetRow(cella, (int)personaggio);
                        Grid.SetColumn(cella, posa);
                        foglio.Children.Add(cella);
                    }
                }

                VisteTests.Disegna(foglio, foglio.Width, foglio.Height, nome);
            }
        });

        Assert.Empty(errori);
    }
}
