using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DocumentaleMarta.App.Grafica;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentaleMarta.Tests;

/// <summary>Registra le scelte di aspetto che il programma applica.</summary>
internal sealed class FintoAspetto : IAspettoService
{
    public List<AspettoApp> Applicate { get; } = [];

    public void Applica(AspettoApp aspetto) => Applicate.Add(aspetto);
}

/// <summary>Il tema (chiaro, scuro, come Windows) nel file delle impostazioni.</summary>
public class TemaNelleImpostazioniTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private ImpostazioniService Servizio() => new(_tmp.Combina("impostazioni.json"));

    private string ScriviFile(string json)
    {
        var percorso = _tmp.CreaFile("impostazioni.json", json);
        return percorso;
    }

    [Fact]
    public void Predefinito_SegueWindows()
    {
        Assert.Equal(TemaApp.ComeWindows, new ImpostazioniApp().Tema);
        Assert.Equal(new AspettoApp(TemaApp.ComeWindows), new ImpostazioniApp().Aspetto);
    }

    [Theory]
    [InlineData(TemaApp.Chiaro)]
    [InlineData(TemaApp.Scuro)]
    [InlineData(TemaApp.ComeWindows)]
    public void IlTema_SiSalvaESiRilegge(TemaApp tema)
    {
        var servizio = Servizio();
        servizio.Salva(new ImpostazioniApp { Tema = tema });

        Assert.Equal(tema, servizio.Carica().Tema);
    }

    [Fact]
    public void IlTema_NelFile_SiLeggeCome_Testo()
    {
        var servizio = Servizio();
        servizio.Salva(new ImpostazioniApp { Tema = TemaApp.Scuro });

        Assert.Contains("\"Tema\": \"Scuro\"", File.ReadAllText(servizio.PercorsoFile));
    }

    [Theory]
    [InlineData("\"Tema\": \"scuro\"", TemaApp.Scuro)]            // maiuscole e minuscole non contano
    [InlineData("\"Tema\": \"Fucsia\"", TemaApp.ComeWindows)]      // un valore che non esiste: si usa quello predefinito
    [InlineData("\"Tema\": \"\"", TemaApp.ComeWindows)]
    [InlineData("\"Tema\": 7", TemaApp.ComeWindows)]
    [InlineData("\"Tema\": 1", TemaApp.ComeWindows)]               // i numeri non sono un modo di scrivere il tema
    [InlineData("\"Tema\": null", TemaApp.ComeWindows)]
    [InlineData("\"Tema\": true", TemaApp.ComeWindows)]
    [InlineData("\"Tema\": { \"x\": [1, 2] }", TemaApp.ComeWindows)]
    public void UnValoreStranoNelFile_NonImpediscePiuDiAvviareIlProgramma(string riga, TemaApp atteso)
    {
        var servizio = new ImpostazioniService(ScriviFile("{ " + riga + ", \"NomeRadice\": \"Tutti\" }"));

        var impostazioni = servizio.Carica();

        Assert.Equal(atteso, impostazioni.Tema);
        Assert.Equal("Tutti", impostazioni.NomeRadice); // il resto del file si legge normalmente
    }

    [Fact]
    public void UnFileDiUnaVersioneVecchia_SenzaIlTema_SegueWindows()
    {
        var servizio = new ImpostazioniService(ScriviFile("{ \"NomeRadice\": \"Archivio\" }"));

        Assert.Equal(TemaApp.ComeWindows, servizio.Carica().Tema);
    }

    [Fact]
    public void ClonaECopiaDa_PortanoAncheIlTema()
    {
        var originale = new ImpostazioniApp { Tema = TemaApp.Scuro };

        Assert.Equal(TemaApp.Scuro, originale.Clona().Tema);

        var altra = new ImpostazioniApp();
        altra.CopiaDa(originale);
        Assert.Equal(TemaApp.Scuro, altra.Tema);
        Assert.Equal(new AspettoApp(TemaApp.Scuro), altra.Aspetto);
    }

    [Fact]
    public void LAspetto_NonEUnaProprietaDaSalvare()
    {
        var servizio = Servizio();
        servizio.Salva(new ImpostazioniApp { Tema = TemaApp.Scuro });

        Assert.DoesNotContain("\"Aspetto\"", File.ReadAllText(servizio.PercorsoFile));
    }
}

/// <summary>I colori del programma: ci sono tutti, in chiaro e in scuro, e si leggono.</summary>
public class ColoriTemaTests
{
    private static readonly Sfumature Accento = Sfumature.Da(Color.FromRgb(0x1F, 0x6A, 0xA5));

    private static Color Colore(ResourceDictionary d, string chiave) => ((SolidColorBrush)d[chiave]).Color;

    private static double Luminanza(Color c)
    {
        static double Canale(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Canale(c.R) + 0.7152 * Canale(c.G) + 0.0722 * Canale(c.B);
    }

    /// <summary>Il rapporto di contrasto fra due colori (WCAG): 4,5 è il minimo per un testo da leggere comodamente.</summary>
    internal static double Contrasto(Color a, Color b)
    {
        var (chiaro, scuro) = Luminanza(a) >= Luminanza(b) ? (Luminanza(a), Luminanza(b)) : (Luminanza(b), Luminanza(a));
        return (chiaro + 0.05) / (scuro + 0.05);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IlDizionario_HaTuttiIColori_ComePennelliCongelati(bool scuro)
    {
        var d = ColoriTema.Crea(scuro, Accento);

        Assert.Equal(ColoriTema.Chiavi.Order(), d.Keys.Cast<string>().Order());
        Assert.All(ColoriTema.Chiavi, chiave =>
        {
            var pennello = Assert.IsType<SolidColorBrush>(d[chiave]);
            Assert.True(pennello.IsFrozen, chiave);
        });
    }

    [Fact]
    public void IlTemaChiaro_HaIColoriDiSempre()
    {
        var d = ColoriTema.Crea(scuro: false, Accento);

        Assert.Equal(Color.FromRgb(0x2E, 0x7D, 0x32), Colore(d, "IconaCompletata"));
        Assert.Equal(Color.FromRgb(0x77, 0x77, 0x77), Colore(d, "TestoCompletata"));
        Assert.Equal(Color.FromRgb(0xFF, 0xF1, 0xDC), Colore(d, "SfondoArancione"));
        Assert.Equal(Color.FromRgb(0xFD, 0xE4, 0xE4), Colore(d, "SfondoRosso"));
        Assert.Equal(Color.FromRgb(0xC4, 0x2B, 0x1C), Colore(d, "IconaRossa"));
        Assert.Equal(Color.FromRgb(0xFF, 0xF4, 0xCE), Colore(d, "SfondoPromemoria"));
    }

    [Fact]
    public void IlTemaScuro_HaColoriDiversiPerOgniColoreNeutroEDiAvviso()
    {
        var chiaro = ColoriTema.Crea(scuro: false, Accento);
        var scuro = ColoriTema.Crea(scuro: true, Accento);

        // Tutti i colori cambiano tra chiaro e scuro, anche quelli che dipendono dall'accento (sfondo di rilievo, bordo, testo).
        Assert.All(ColoriTema.Chiavi, chiave => Assert.NotEqual(Colore(chiaro, chiave), Colore(scuro, chiave)));
    }

    [Fact]
    public void InScuro_GliSfondiSonoScuri_EITestiChiari()
    {
        var d = ColoriTema.Crea(scuro: true, Accento);

        foreach (var sfondo in new[] { "SfondoPannello", "SfondoSuperficie", "SfondoDivisore", "SfondoVisualizzatore", "SfondoArancione", "SfondoRosso", "SfondoPromemoria" })
            Assert.True(Luminanza(Colore(d, sfondo)) < 0.1, sfondo);
        foreach (var testo in new[] { "TestoSecondario", "TestoTenue", "TestoArancione", "TestoRosso", "IconaCompletata", "TestoCompletata", "IconaPromemoria", "TestoAccento" })
            Assert.True(Luminanza(Colore(d, testo)) > 0.2, testo);
    }

    [Fact]
    public void InChiaro_GliSfondiSonoChiari_EITestiScuri()
    {
        var d = ColoriTema.Crea(scuro: false, Accento);

        foreach (var sfondo in new[] { "SfondoPannello", "SfondoSuperficie", "SfondoArancione", "SfondoRosso", "SfondoPromemoria", "SfondoAccentoLeggero" })
            Assert.True(Luminanza(Colore(d, sfondo)) > 0.6, sfondo);
        foreach (var testo in new[] { "TestoSecondario", "TestoTenue", "TestoArancione", "TestoRosso", "IconaPromemoria", "TestoAccento" })
            Assert.True(Luminanza(Colore(d, testo)) < 0.3, testo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ITesti_SiLeggonoSuiLoroSfondi_ConUnContrastoAlmenoDi4eMezzo(bool scuro)
    {
        var d = ColoriTema.Crea(scuro, Accento);
        // Testi da leggere: 4,5. Il nome delle cartelle completate è volutamente più spento (4); le icone sono segni, per cui basta 3.
        (string Testo, string Sfondo, double Minimo)[] coppie =
        [
            ("TestoSecondario", "SfondoSuperficie", 4.5), ("TestoSecondario", "SfondoPannello", 4.5),
            ("TestoTenue", "SfondoSuperficie", 4.5), ("TestoTenue", "SfondoPannello", 4.5),
            ("TestoArancione", "SfondoArancione", 4.5), ("TestoRosso", "SfondoRosso", 4.5),
            ("TestoRosso", "SfondoSuperficie", 4.5), ("TestoAccento", "SfondoSuperficie", 4.5),
            ("TestoAccento", "SfondoAccentoLeggero", 4.5), ("TestoCompletata", "SfondoSuperficie", 4),
            ("IconaCompletata", "SfondoSuperficie", 3), ("IconaPromemoria", "SfondoPromemoria", 3),
            ("IconaArancione", "SfondoArancione", 2), ("IconaRossa", "SfondoRosso", 3)
        ];

        foreach (var (testo, sfondo, minimo) in coppie)
            Assert.True(Contrasto(Colore(d, testo), Colore(d, sfondo)) >= minimo,
                $"{testo} su {sfondo}: {Contrasto(Colore(d, testo), Colore(d, sfondo)):0.0} (minimo {minimo})");
    }

    [Fact]
    public void LeSfumature_VannoDalPiuScuroAlPiuChiaro_ELaPrincipaleEIlColoreDeiPulsantiInChiaro()
    {
        var principale = Color.FromRgb(0xC8, 0x5A, 0x0A);
        var s = Sfumature.Da(principale);

        Assert.Equal(principale, s.Scura1);
        double[] dalPiuScuro = [Luminanza(s.Scura3), Luminanza(s.Scura2), Luminanza(s.Scura1), Luminanza(s.Base), Luminanza(s.Chiara1), Luminanza(s.Chiara2), Luminanza(s.Chiara3)];
        Assert.Equal(dalPiuScuro.Order(), dalPiuScuro);
    }

    [Fact]
    public void Mescola_VaDaUnColoreALAltro()
    {
        Assert.Equal(Colors.Black, Colors.Black.Mescola(Colors.White, 0));
        Assert.Equal(Colors.White, Colors.Black.Mescola(Colors.White, 1));
        Assert.Equal(Color.FromRgb(128, 128, 128), Colors.Black.Mescola(Colors.White, 0.5));
    }
}

/// <summary>Il gestore dell'aspetto: chiaro, scuro, come Windows, e il cambio al volo.</summary>
[Collection("WPF")]
public class GestoreAspettoTests
{
    private static Color Colore(Window finestra, string chiave) => ((SolidColorBrush)finestra.FindResource(chiave)).Color;

    private static int DizionariDelTema(Window finestra) =>
        finestra.Resources.MergedDictionaries.Count(d => d.Source?.OriginalString.Contains("PresentationFramework.Fluent") == true);

    [Fact]
    public void Chiaro_MetteLoStileChiaroEIColoriChiari_Scuro_QuelliScuri()
    {
        VisteTests.InSta(() =>
        {
            var finestra = new Window();
            var gestore = new GestoreAspetto(new OspiteFinestra(finestra), () => false);

            gestore.Applica(new AspettoApp(TemaApp.Chiaro));
            Assert.False(gestore.ScuroInUso);
            Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), Colore(finestra, "SfondoSuperficie"));
            var sfondoChiaro = ((SolidColorBrush)finestra.FindResource("WindowBackground")).Color;

            gestore.Applica(new AspettoApp(TemaApp.Scuro));
            Assert.True(gestore.ScuroInUso);
            Assert.Equal(Color.FromRgb(0x2B, 0x2B, 0x2B), Colore(finestra, "SfondoSuperficie"));
            var sfondoScuro = ((SolidColorBrush)finestra.FindResource("WindowBackground")).Color;

            // Cambia anche lo stile di Windows 11 (lo sfondo delle finestre), e ne resta uno solo.
            Assert.True(Luminanza(sfondoChiaro) > 0.8 && Luminanza(sfondoScuro) < 0.25, $"chiaro {sfondoChiaro}, scuro {sfondoScuro}");
            Assert.Equal(1, DizionariDelTema(finestra));
        });
    }

    private static double Luminanza(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;

    [Fact]
    public void ComeWindows_SegueIlTemaDiWindows_ECambiaQuandoCambiaLui()
    {
        VisteTests.InSta(() =>
        {
            var finestra = new Window();
            var windowsScuro = false;
            var gestore = new GestoreAspetto(new OspiteFinestra(finestra), () => windowsScuro);

            gestore.Applica(new AspettoApp(TemaApp.ComeWindows));
            Assert.False(gestore.ScuroInUso);

            windowsScuro = true;
            Assert.False(gestore.ScuroInUso);   // finché nessuno lo dice, resta com'è
            gestore.SistemaCambiato();
            Assert.True(gestore.ScuroInUso);
            Assert.Equal(Color.FromRgb(0x2B, 0x2B, 0x2B), Colore(finestra, "SfondoSuperficie"));

            windowsScuro = false;
            gestore.SistemaCambiato();
            Assert.False(gestore.ScuroInUso);
            Assert.Equal(1, DizionariDelTema(finestra));
        });
    }

    [Theory]
    [InlineData(TemaApp.Chiaro, true, false)]
    [InlineData(TemaApp.Scuro, false, true)]
    public void SeSiHaScelto_ChiaroOScuro_WindowsCheCambiaNonContaNiente(TemaApp tema, bool windowsScuroDopo, bool scuroAtteso)
    {
        VisteTests.InSta(() =>
        {
            var finestra = new Window();
            var windowsScuro = !windowsScuroDopo;
            var gestore = new GestoreAspetto(new OspiteFinestra(finestra), () => windowsScuro);
            gestore.Applica(new AspettoApp(tema));

            windowsScuro = windowsScuroDopo;
            gestore.SistemaCambiato();

            Assert.Equal(scuroAtteso, gestore.ScuroInUso);
        });
    }

    [Fact]
    public void ApplicandoPiuVolte_IColoriNonSiAccumulano_EStannoDopoLoStileDiWindows()
    {
        VisteTests.InSta(() =>
        {
            var finestra = new Window();
            var gestore = new GestoreAspetto(new OspiteFinestra(finestra), () => false);

            foreach (var tema in new[] { TemaApp.Chiaro, TemaApp.Scuro, TemaApp.Scuro, TemaApp.Chiaro, TemaApp.ComeWindows })
                gestore.Applica(new AspettoApp(tema));

            var uniti = finestra.Resources.MergedDictionaries;
            Assert.Equal(2, uniti.Count);                    // lo stile di Windows e i colori del programma
            Assert.Equal(1, DizionariDelTema(finestra));
            Assert.True(uniti[^1].Contains("Bordo"));         // i colori del programma sono l'ultimo
        });
    }

    [Fact]
    public void ILColoriDelProgramma_SiVedonoDaDentroLaFinestra_ECambiano_ConDynamicResource()
    {
        VisteTests.InSta(() =>
        {
            var finestra = new Window();
            var testo = new TextBlock { Text = "Prova" };
            testo.SetResourceReference(TextBlock.ForegroundProperty, "TestoSecondario");
            finestra.Content = testo;
            var gestore = new GestoreAspetto(new OspiteFinestra(finestra), () => false);

            gestore.Applica(new AspettoApp(TemaApp.Chiaro));
            Assert.Equal(Color.FromRgb(0x55, 0x55, 0x55), ((SolidColorBrush)testo.Foreground).Color);

            gestore.Applica(new AspettoApp(TemaApp.Scuro));
            Assert.Equal(Color.FromRgb(0xCF, 0xCF, 0xCF), ((SolidColorBrush)testo.Foreground).Color);
        });
    }

    [Fact]
    public void LOspiteNullo_NonFaNiente_EIlGestoreLoUsaSenzaProblemi()
    {
        var gestore = new GestoreAspetto(new OspiteNullo(), () => true);

        gestore.Applica(new AspettoApp(TemaApp.ComeWindows));

        Assert.True(gestore.ScuroInUso);
    }

    [Fact]
    public void IServizi_ComprendonoIlGestore_ComeUnicoOggetto()
    {
        using var tmp = new CartellaTemporanea();
        var radice = tmp.Combina("Documentale");
        var impostazioni = new ImpostazioniApp { PercorsoRadice = radice, Tema = TemaApp.Scuro };
        using var servizi = Composizione.Crea(impostazioni, ArchivioDatabase.Inizializza(radice));

        var gestore = servizi.GetRequiredService<GestoreAspetto>();
        Assert.Same(gestore, servizi.GetRequiredService<IAspettoService>());

        // E il modello principale si costruisce con lui (cambiare tema dalle impostazioni funziona davvero).
        Assert.NotNull(servizi.GetRequiredService<MainViewModel>());
    }
}

/// <summary>Nei file XAML non devono restare colori scritti a mano: ogni colore deve seguire il tema.</summary>
public class ColoriNeiXamlTests
{
    private static IEnumerable<(string Nome, string Contenuto)> FileXaml()
    {
        var cartella = Path.Combine(AppContext.BaseDirectory, "XamlApp");
        var file = Directory.GetFiles(cartella, "*.xaml");
        Assert.NotEmpty(file); // i file XAML del programma vengono copiati accanto ai test
        return file.Select(f => (Path.GetFileName(f), File.ReadAllText(f)));
    }

    [Fact]
    public void OgniColoreDinamicoUsatoNeiXaml_EnelTema()
    {
        var usati = FileXaml()
            .SelectMany(f => Regex.Matches(f.Contenuto, @"\{DynamicResource (\w+)\}").Select(m => (f.Nome, Chiave: m.Groups[1].Value)))
            .ToList();

        Assert.NotEmpty(usati);
        Assert.All(usati, u => Assert.True(ColoriTema.Chiavi.Contains(u.Chiave), $"{u.Nome} usa «{u.Chiave}», che il tema non definisce."));
    }

    [Fact]
    public void OgniColoreDelTema_SiUsaDaQualchePartenei_Xaml()
    {
        var testo = string.Join("\n", FileXaml().Select(f => f.Contenuto));

        Assert.All(ColoriTema.Chiavi, chiave => Assert.Contains($"{{DynamicResource {chiave}}}", testo));
    }

    [Fact]
    public void NeiXaml_NonCiSonoColoriScrittiAMano_TranneLeIcone()
    {
        foreach (var (nome, contenuto) in FileXaml().Where(f => f.Nome != "Icone.xaml"))
        {
            Assert.DoesNotMatch(@"=""#[0-9A-Fa-f]{3,8}""", contenuto);
            Assert.False(Regex.IsMatch(contenuto, @"(Foreground|Background|BorderBrush|Fill|Stroke)=""(Black|Gray|LightGray|WhiteSmoke|Red|Orange|Green|DarkGray|DimGray)"""), nome);
        }
    }

    [Fact]
    public void IlBiancoSiUsaSoloPerLaPaginaDelDocumento()
    {
        // La pagina dell'anteprima è «carta»: resta bianca anche con il tema scuro, come il documento vero.
        var conBianco = FileXaml()
            .Where(f => Regex.IsMatch(f.Contenuto, @"=""White"""))
            .Select(f => f.Nome)
            .Order()
            .ToList();

        Assert.Equal(["AnteprimaIngranditaDialog.xaml", "AnteprimaView.xaml"], conBianco);
    }

    [Fact]
    public void NeiXaml_NonCEPiuIlDizionarioDeiColoriFissi()
    {
        Assert.DoesNotContain(FileXaml(), f => f.Contenuto.Contains("Colori.xaml"));
        Assert.DoesNotContain(FileXaml(), f => f.Nome == "Colori.xaml");
    }
}

/// <summary>La scelta del tema nella finestra Impostazioni: prova immediata, annullamento, salvataggio.</summary>
public class TemaDalleImpostazioniTests : IDisposable
{
    private readonly ArchivioDiProva _a = new();
    private readonly ImpostazioniService _servizio;
    private readonly ImpostazioniApp _impostazioni;
    private readonly FintoAspetto _aspetto = new();

    public TemaDalleImpostazioniTests()
    {
        _servizio = new ImpostazioniService(_a.Tmp.Combina("impostazioni", "impostazioni.json"));
        _impostazioni = new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false, Tema = TemaApp.Chiaro };
    }

    public void Dispose() => _a.Dispose();

    private MainViewModel Principale(IAspettoService? aspetto = null, ImpostazioniService? servizio = null) => new(
        _a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni,
        new AlertService(_impostazioni, new TempoFisso(new DateTime(2026, 10, 1, 9, 0, 0))),
        servizioImpostazioni: servizio ?? _servizio, aspetto: aspetto ?? _aspetto);

    private ImpostazioniApp DalFile() => new ImpostazioniService(_servizio.PercorsoFile).Carica();

    // ---------- Il modello della finestra ----------

    [Fact]
    public void IlModello_PartedalTemaAttuale_EIPulsantiDiSceltaLoMostrano()
    {
        var modello = new ImpostazioniViewModel(new ImpostazioniApp { Tema = TemaApp.Scuro }, @"C:\x\impostazioni.json");

        Assert.Equal(TemaApp.Scuro, modello.Tema);
        Assert.True(modello.TemaScuro);
        Assert.False(modello.TemaChiaro);
        Assert.False(modello.TemaComeWindows);
    }

    [Fact]
    public void ScegliendoUnPulsante_CambiaIlTema_EGliAltriSiSpengono_ESiAvvisaLaVista()
    {
        var modello = new ImpostazioniViewModel(new ImpostazioniApp { Tema = TemaApp.Chiaro }, @"C:\x\impostazioni.json");
        var cambiate = new List<string>();
        modello.PropertyChanged += (_, e) => cambiate.Add(e.PropertyName!);

        modello.TemaScuro = true;

        Assert.Equal(TemaApp.Scuro, modello.Tema);
        Assert.Contains(nameof(modello.TemaChiaro), cambiate);
        Assert.Contains(nameof(modello.TemaScuro), cambiate);
        Assert.Contains(nameof(modello.TemaComeWindows), cambiate);
        Assert.False(modello.TemaChiaro);
    }

    [Fact]
    public void UnPulsanteCheSiSpegne_NonCambiaIlTema()
    {
        var modello = new ImpostazioniViewModel(new ImpostazioniApp { Tema = TemaApp.Chiaro }, @"C:\x\impostazioni.json");

        // WPF, scegliendo un altro pulsante, scrive "falso" in quello che era scelto prima.
        modello.TemaChiaro = false;

        Assert.Equal(TemaApp.Chiaro, modello.Tema);
    }

    [Fact]
    public void OgniCambioDiTema_AvvisaLAnteprima_PrimaDiSalvare()
    {
        var anteprime = new List<AspettoApp>();
        var modello = new ImpostazioniViewModel(new ImpostazioniApp { Tema = TemaApp.Chiaro }, @"C:\x\impostazioni.json",
            anteprimaAspetto: anteprime.Add);

        modello.TemaScuro = true;
        modello.TemaComeWindows = true;
        modello.TemaComeWindows = true; // già scelto: niente da avvisare
        modello.TemaChiaro = true;

        Assert.Equal([new AspettoApp(TemaApp.Scuro), new AspettoApp(TemaApp.ComeWindows), new AspettoApp(TemaApp.Chiaro)], anteprime);
    }

    [Fact]
    public void SenzaAnteprima_ScegliereIlTemaNonDaProblemi()
    {
        var modello = new ImpostazioniViewModel(new ImpostazioniApp(), @"C:\x\impostazioni.json");

        modello.TemaScuro = true;

        Assert.Equal(TemaApp.Scuro, modello.Tema);
    }

    [Fact]
    public void Costruisci_PortaIlTemaScelto_ELeAltreImpostazioniRestano()
    {
        var attuali = new ImpostazioniApp { Tema = TemaApp.Chiaro, NomeRadice = "Archivio", SogliaArancioneGiorni = 45 };
        var modello = new ImpostazioniViewModel(attuali, @"C:\x\impostazioni.json") { Tema = TemaApp.Scuro };

        var nuove = modello.Costruisci();

        Assert.Equal(TemaApp.Scuro, nuove.Tema);
        Assert.Equal("Archivio", nuove.NomeRadice);
        Assert.Equal(45, nuove.SogliaArancioneGiorni);
        Assert.Equal(TemaApp.Chiaro, attuali.Tema); // l'originale non si tocca finché non si salva
    }

    [Fact]
    public void ScegliereIlTema_NonImpediscediSalvare_EnonCambiaGliErrori()
    {
        var modello = new ImpostazioniViewModel(new ImpostazioniApp(), @"C:\x\impostazioni.json");

        modello.TemaScuro = true;

        Assert.True(modello.PuoSalvare);
        Assert.Equal("", modello.Errore);
    }

    // ---------- Aprendo le Impostazioni dal programma ----------

    [Fact]
    public async Task Salvando_ConUnTemaNuovo_SiVedeSubito_EResta_NelFile()
    {
        var vm = Principale();
        _a.Dialog.RispondiImpostazioni(m => m.TemaScuro = true);

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Equal(TemaApp.Scuro, DalFile().Tema);
        Assert.Equal(TemaApp.Scuro, _impostazioni.Tema);
        Assert.Equal(new AspettoApp(TemaApp.Scuro), _aspetto.Applicate[^1]); // alla fine vale quello salvato
        Assert.Contains(new AspettoApp(TemaApp.Scuro), _aspetto.Applicate);
    }

    [Fact]
    public async Task MentreLaFinestraEAperta_IlTemaSiProva_ConAnnullaTornaQuelloDiPrima()
    {
        var vm = Principale();
        List<AspettoApp>? durante = null;
        _a.Dialog.RispondiImpostazioni(m =>
        {
            m.TemaScuro = true;
            durante = [.. _aspetto.Applicate];
            m.SogliaArancione = "abc"; // con valori non validi la finta finestra equivale ad Annulla
        });

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Equal([new AspettoApp(TemaApp.Scuro)], durante);         // si è visto il tema scuro mentre si sceglieva
        Assert.Equal(new AspettoApp(TemaApp.Chiaro), _aspetto.Applicate[^1]); // e annullando torna il chiaro di partenza
        Assert.Equal(TemaApp.Chiaro, _impostazioni.Tema);
        Assert.False(File.Exists(_servizio.PercorsoFile));
    }

    [Fact]
    public async Task SenzaCambiareIlTema_SiRiapplicaSoloQuelloAttuale()
    {
        var vm = Principale();
        _a.Dialog.RispondiImpostazioni(m => m.SogliaArancione = "45");

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.All(_aspetto.Applicate, a => Assert.Equal(new AspettoApp(TemaApp.Chiaro), a));
    }

    [Fact]
    public async Task SeIlSalvataggioFallisce_IlTemaProvatoRestaFinoAlLaChiusuraDellaFinestra_PoiTornaQuelloVero()
    {
        var bloccante = _a.Tmp.CreaFile("bloccante", "sono un file");
        var vm = Principale(servizio: new ImpostazioniService(Path.Combine(bloccante, "impostazioni.json")));
        _a.Dialog.RispondiImpostazioni(m => m.TemaScuro = true);   // salva (ma fallisce) …
        _a.Dialog.RispondiImpostazioni(null);                        // … e alla riapertura annulla

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Equal(TemaApp.Chiaro, _impostazioni.Tema);
        Assert.Equal(new AspettoApp(TemaApp.Chiaro), _aspetto.Applicate[^1]);
    }

    [Fact]
    public async Task SenzaIlServizioDiAspetto_LaSceltaSiSalvaComunque()
    {
        var vm = new MainViewModel(_a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni,
            servizioImpostazioni: _servizio);
        _a.Dialog.RispondiImpostazioni(m => m.TemaScuro = true);

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Equal(TemaApp.Scuro, DalFile().Tema);
    }
}
