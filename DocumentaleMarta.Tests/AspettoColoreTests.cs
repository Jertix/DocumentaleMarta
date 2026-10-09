using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using DocumentaleMarta.App.Grafica;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

/// <summary>Il colore principale e lo sfondo colorato nel file delle impostazioni.</summary>
public class ColoreNelleImpostazioniTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();

    public void Dispose() => _tmp.Dispose();

    private ImpostazioniService Servizio() => new(_tmp.Combina("impostazioni.json"));

    [Fact]
    public void Predefiniti_ColoreAcciaio_SfondoNeutro()
    {
        var impostazioni = new ImpostazioniApp();

        Assert.Equal(ColoreApp.Acciaio, impostazioni.Colore);
        Assert.False(impostazioni.SfondoColorato);
        Assert.Equal(new AspettoApp(TemaApp.ComeWindows, ColoreApp.Acciaio, false), impostazioni.Aspetto);
    }

    [Fact]
    public void LAspettoSenzaScelte_VaDiPartenza_ConColoreAcciaioESfondoNeutro()
    {
        Assert.Equal(new AspettoApp(TemaApp.Scuro, ColoreApp.Acciaio, SfondoColorato: false), new AspettoApp(TemaApp.Scuro));
    }

    [Theory]
    [InlineData(ColoreApp.Acciaio)]
    [InlineData(ColoreApp.Fucina)]
    [InlineData(ColoreApp.Foresta)]
    [InlineData(ColoreApp.Prugna)]
    [InlineData(ColoreApp.Grafite)]
    [InlineData(ColoreApp.ComeWindows)]
    public void IlColore_SiSalvaESiRilegge_ComeTesto(ColoreApp colore)
    {
        var servizio = Servizio();
        servizio.Salva(new ImpostazioniApp { Colore = colore });

        Assert.Equal(colore, servizio.Carica().Colore);
        Assert.Contains($"\"Colore\": \"{colore}\"", File.ReadAllText(servizio.PercorsoFile));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LoSfondoColorato_SiSalvaESiRilegge(bool colorato)
    {
        var servizio = Servizio();
        servizio.Salva(new ImpostazioniApp { SfondoColorato = colorato });

        Assert.Equal(colorato, servizio.Carica().SfondoColorato);
    }

    [Theory]
    [InlineData("\"Colore\": \"fucina\"", ColoreApp.Fucina)]       // maiuscole e minuscole non contano
    [InlineData("\"Colore\": \"Fucsia\"", ColoreApp.Acciaio)]      // un colore che non esiste: si usa quello predefinito
    [InlineData("\"Colore\": \"\"", ColoreApp.Acciaio)]
    [InlineData("\"Colore\": 3", ColoreApp.Acciaio)]
    [InlineData("\"Colore\": null", ColoreApp.Acciaio)]
    [InlineData("\"Colore\": [1]", ColoreApp.Acciaio)]
    public void UnColoreStranoNelFile_NonImpediscePiuDiAvviareIlProgramma(string riga, ColoreApp atteso)
    {
        var percorso = _tmp.CreaFile("impostazioni.json", "{ " + riga + ", \"NomeRadice\": \"Tutti\" }");

        var impostazioni = new ImpostazioniService(percorso).Carica();

        Assert.Equal(atteso, impostazioni.Colore);
        Assert.Equal("Tutti", impostazioni.NomeRadice);
    }

    [Fact]
    public void UnFileDellaVersioneDeiTemi_SenzaColoreNeSfondo_UsaIPredefiniti()
    {
        var percorso = _tmp.CreaFile("impostazioni.json", "{ \"Tema\": \"Scuro\" }");

        var impostazioni = new ImpostazioniService(percorso).Carica();

        Assert.Equal(TemaApp.Scuro, impostazioni.Tema);
        Assert.Equal(ColoreApp.Acciaio, impostazioni.Colore);
        Assert.False(impostazioni.SfondoColorato);
    }

    [Fact]
    public void ClonaECopiaDa_PortanoColoreESfondo()
    {
        var originale = new ImpostazioniApp { Colore = ColoreApp.Prugna, SfondoColorato = true };

        Assert.Equal(ColoreApp.Prugna, originale.Clona().Colore);
        Assert.True(originale.Clona().SfondoColorato);

        var altra = new ImpostazioniApp();
        altra.CopiaDa(originale);
        Assert.Equal(new AspettoApp(TemaApp.ComeWindows, ColoreApp.Prugna, true), altra.Aspetto);
    }

    [Fact]
    public void ColoreESfondo_NonCambianoLaValidita_DelleImpostazioni()
    {
        Assert.Empty(new ImpostazioniApp { Colore = ColoreApp.Grafite, SfondoColorato = true }.Valida());
    }
}

/// <summary>I colori principali offerti: devono essere leggibili (testo bianco sui pulsanti) e ben fatti.</summary>
public class TavolozzeTests
{
    private static IEnumerable<Tavolozza> ConUnColore => Tavolozze.Tutte.Where(t => t.Principale is not null);

    [Fact]
    public void OgniColoreDelleImpostazioni_HaLaSuaScelta_UnaSolaVolta()
    {
        Assert.Equal(Enum.GetValues<ColoreApp>().Order(), Tavolozze.Tutte.Select(t => t.Id).Order());
        Assert.All(Enum.GetValues<ColoreApp>(), colore => Assert.Equal(colore, Tavolozze.Per(colore).Id));
    }

    [Fact]
    public void ISoliNomiVisibili_SonoItaliani_EDiversi()
    {
        Assert.Equal(["Acciaio", "Fucina", "Foresta", "Prugna", "Grafite", "Come Windows"], Tavolozze.Tutte.Select(t => t.Nome));
        Assert.Equal(Tavolozze.Tutte.Count, Tavolozze.Tutte.Select(t => t.Nome).Distinct().Count());
    }

    [Fact]
    public void ILColoriSonoTuttiDiversi()
    {
        var colori = ConUnColore.Select(t => t.Principale).ToList();

        Assert.Equal(5, colori.Count);
        Assert.Equal(colori.Count, colori.Distinct().Count());
    }

    [Fact]
    public void ComeWindows_UsaIlColoreDiWindows()
    {
        Assert.Null(Tavolozze.Per(ColoreApp.ComeWindows).Principale);
        Assert.Equal(SystemColors.AccentColor, Tavolozze.Principale(ColoreApp.ComeWindows));
    }

    [Fact]
    public void ILTestoBianco_SiLegge_SulColorePrincipale_InTemaChiaro()
    {
        foreach (var t in ConUnColore)
            Assert.True(ColoriTemaTests.Contrasto(Colors.White, t.Principale!.Value) >= 4.5,
                $"{t.Nome}: {ColoriTemaTests.Contrasto(Colors.White, t.Principale!.Value):0.0}");
    }

    [Fact]
    public void ILTestoNero_SiLegge_SulColoreChiaro_DeiPulsantiInTemaScuro()
    {
        foreach (var t in ConUnColore)
            Assert.True(ColoriTemaTests.Contrasto(Colors.Black, Sfumature.Da(t.Principale!.Value).Chiara2) >= 4.5, t.Nome);
    }

    [Fact]
    public void IlColorePrincipale_NonEIlGrigioDelleSelezioni_ESiDistingueDalloSfondoDelleFinestre()
    {
        foreach (var t in ConUnColore)
        {
            var p = t.Principale!.Value;
            Assert.True(ColoriTemaTests.Contrasto(p, Color.FromRgb(0xFA, 0xFA, 0xFA)) >= 3, $"{t.Nome} su sfondo chiaro");
            Assert.True(ColoriTemaTests.Contrasto(Sfumature.Da(p).Chiara2, Color.FromRgb(0x20, 0x20, 0x20)) >= 3, $"{t.Nome} su sfondo scuro");
        }
    }
}

/// <summary>I colori del programma con lo sfondo colorato: la tinta deve restare leggera e tutto leggibile.</summary>
public class SfondoColoratoTests
{
    private static Color Colore(ResourceDictionary d, string chiave) => ((SolidColorBrush)d[chiave]).Color;

    private static IEnumerable<Color> Principali() =>
        Tavolozze.Tutte.Where(t => t.Principale is not null).Select(t => t.Principale!.Value).Append(Color.FromRgb(0x25, 0x82, 0x92));

    [Theory]
    [InlineData(false, "#FAFAFA")]
    [InlineData(true, "#202020")]
    public void SenzaTinta_LoSfondoDelleFinestre_ELoStessoDelTemaDiWindows(bool scuro, string atteso)
    {
        var d = ColoriTema.Crea(scuro, Sfumature.Da(Colori.Da("#1F6AA5")), sfondoColorato: false);

        Assert.Equal(Colori.Da(atteso), Colore(d, "SfondoFinestra"));
    }

    [Fact]
    public void ISfondiSonoPienamenteOpachi_ASenzaTinta_Anche()
    {
        foreach (var scuro in new[] { false, true })
        foreach (var tinta in new[] { false, true })
        {
            var d = ColoriTema.Crea(scuro, Sfumature.Da(Colori.Da("#B8500A")), tinta);

            Assert.Equal(255, Colore(d, "SfondoFinestra").A);
            Assert.Equal(255, Colore(d, "SfondoPannello").A);
        }
    }

    [Fact]
    public void ConLaTinta_LoSfondoSiAvvicinaAlColorePrincipale_MaRestaChiaroOScuro()
    {
        foreach (var principale in Principali())
        foreach (var scuro in new[] { false, true })
        {
            var senza = ColoriTema.Crea(scuro, Sfumature.Da(principale), false);
            var con = ColoriTema.Crea(scuro, Sfumature.Da(principale), true);

            foreach (var chiave in new[] { "SfondoFinestra", "SfondoPannello" })
            {
                var a = Colore(senza, chiave);
                var b = Colore(con, chiave);
                Assert.NotEqual(a, b);

                // Si muove verso il colore principale, e di poco.
                var distanzaPrima = Math.Abs(a.R - principale.R) + Math.Abs(a.G - principale.G) + Math.Abs(a.B - principale.B);
                var distanzaDopo = Math.Abs(b.R - principale.R) + Math.Abs(b.G - principale.G) + Math.Abs(b.B - principale.B);
                Assert.True(distanzaDopo < distanzaPrima, $"{chiave} {principale}");
                Assert.True(Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) <= 120, $"{chiave} {principale}: tinta troppo forte");
            }
        }
    }

    [Fact]
    public void ConLaTinta_ITestiSiLeggonoSulloSfondoDelleFinestreEDeiPannelli()
    {
        foreach (var principale in Principali())
        foreach (var scuro in new[] { false, true })
        {
            var d = ColoriTema.Crea(scuro, Sfumature.Da(principale), sfondoColorato: true);

            foreach (var sfondo in new[] { "SfondoFinestra", "SfondoPannello" })
            {
                foreach (var testo in new[] { "TestoSecondario", "TestoTenue", "TestoRosso", "TestoAccento" })
                    Assert.True(ColoriTemaTests.Contrasto(Colore(d, testo), Colore(d, sfondo)) >= 4.5,
                        $"{testo} su {sfondo} ({(scuro ? "scuro" : "chiaro")}, {principale}): {ColoriTemaTests.Contrasto(Colore(d, testo), Colore(d, sfondo)):0.0}");
            }

            // Le cartelle completate stanno nell'albero, che è direttamente sullo sfondo della finestra (non sui pannelli).
            Assert.True(ColoriTemaTests.Contrasto(Colore(d, "IconaCompletata"), Colore(d, "SfondoFinestra")) >= 3, "IconaCompletata");
            Assert.True(ColoriTemaTests.Contrasto(Colore(d, "TestoCompletata"), Colore(d, "SfondoFinestra")) >= 3.8, "TestoCompletata");
        }
    }

    [Fact]
    public void LeGrigliee_RestanoBianche_CosiSiVedonoComeSchede_SulloSfondoColorato()
    {
        var chiaro = ColoriTema.Crea(false, Sfumature.Da(Colori.Da("#1F6AA5")), true);
        var scuro = ColoriTema.Crea(true, Sfumature.Da(Colori.Da("#1F6AA5")), true);

        Assert.Equal(Colors.White, Colore(chiaro, "SfondoSuperficie"));
        Assert.Equal(Colori.Da("#2B2B2B"), Colore(scuro, "SfondoSuperficie"));
    }
}

/// <summary>Il colore principale nello stile di Windows 11: le voci che lo usano passano al colore scelto.</summary>
[Collection("WPF")]
public class AccentoTemaTests
{
    private static Color Colore(Window finestra, string chiave) => ((SolidColorBrush)finestra.FindResource(chiave)).Color;

    private static Sfumature Di(ColoreApp colore) => Sfumature.Da(Tavolozze.Principale(colore));

    private static GestoreAspetto Gestore(Window finestra) => new(new OspiteFinestra(finestra), () => false);

    [Fact]
    public void LeVociSintetiche_SiRiscrivono_ConLaSfumaturaGiusta_EToccanoSoloGliAccenti()
    {
        var nuove = Di(ColoreApp.Fucina);
        var sistema = SystemColors.AccentColorDark1;
        var stile = new ResourceDictionary
        {
            ["Pieno"] = new SolidColorBrush(sistema),
            ["Trasparente"] = new SolidColorBrush(Color.FromArgb(0x80, sistema.R, sistema.G, sistema.B)),
            ["Colore"] = SystemColors.AccentColorLight2,
            ["Altro"] = new SolidColorBrush(Color.FromRgb(1, 2, 3)),
            ["Bianco"] = new SolidColorBrush(Colors.White),
            ["Nero"] = new SolidColorBrush(Colors.Black),
            [42] = new SolidColorBrush(sistema) // una chiave che non è un nome: ignorata
        };

        var risultato = AccentoTema.Sovrascritture(stile, nuove);

        Assert.Equal(nuove.Scura1, ((SolidColorBrush)risultato["Pieno"]).Color);
        Assert.Equal(Color.FromArgb(0x80, nuove.Scura1.R, nuove.Scura1.G, nuove.Scura1.B), ((SolidColorBrush)risultato["Trasparente"]).Color);
        Assert.Equal(nuove.Chiara2, (Color)risultato["Colore"]);
        Assert.False(risultato.Contains("Altro"));
        Assert.False(risultato.Contains("Bianco"));
        Assert.False(risultato.Contains("Nero"));
        Assert.Equal(nuove.Base, (Color)risultato["SystemAccentColor"]);
        Assert.Equal(nuove.Chiara3, (Color)risultato["SystemAccentColorLight3"]);
        Assert.All(risultato.Values.OfType<SolidColorBrush>(), b => Assert.True(b.IsFrozen));
    }

    [Theory]
    [InlineData(TemaApp.Chiaro)]
    [InlineData(TemaApp.Scuro)]
    public void ScegliendoUnColore_PulsantiSpunteBordiESelezioni_Lo_Usano(TemaApp tema)
    {
        VisteTests.InSta(() =>
        {
            var finestra = new Window();
            var gestore = Gestore(finestra);
            var s = Di(ColoreApp.Fucina);
            var scuro = tema == TemaApp.Scuro;

            gestore.Applica(new AspettoApp(tema, ColoreApp.Fucina));

            // Il colore dei pulsanti principali: in chiaro la sfumatura scura 1 (il colore scelto), in scuro la chiara 2.
            var pulsante = scuro ? s.Chiara2 : s.Scura1;
            foreach (var chiave in new[] { "AccentButtonBackground", "AccentFillColorDefaultBrush", "TextControlFocusedBorderBrush",
                                           "CheckBoxCheckBackgroundFillChecked", "ComboBoxBorderBrushFocused", "ProgressBarForeground" })
                Assert.Equal(pulsante, Colore(finestra, chiave));

            // La selezione delle righe e i collegamenti usano altre sfumature della stessa famiglia.
            Assert.Equal(scuro ? s.Chiara3 : s.Scura1, Colore(finestra, "DataGridRowSelectedBackgroundThemeBrush"));
            Assert.Equal(scuro ? s.Chiara3 : s.Scura2, Colore(finestra, "HyperlinkForeground"));
        });
    }

    [Fact]
    public void ComeWindows_LasciaIlColoreDiWindows()
    {
        VisteTests.InSta(() =>
        {
            var finestra = new Window();
            Gestore(finestra).Applica(new AspettoApp(TemaApp.Chiaro, ColoreApp.ComeWindows));

            Assert.Equal(SystemColors.AccentColorDark1, Colore(finestra, "AccentButtonBackground"));
            Assert.Equal(2, finestra.Resources.MergedDictionaries.Count);
            Assert.False(finestra.Resources.MergedDictionaries[^1].Contains("AccentButtonBackground")); // niente sovrascritture
        });
    }

    [Fact]
    public void OgniVoceSovrascritta_EQuellaCheVinceNellaFinestra()
    {
        VisteTests.InSta(() =>
        {
            var finestra = new Window();
            var gestore = Gestore(finestra);
            gestore.Applica(new AspettoApp(TemaApp.Chiaro, ColoreApp.Prugna));

            var colori = finestra.Resources.MergedDictionaries[^1];
            var sovrascritte = colori.Keys.Cast<object>().Where(k => k is string s && !ColoriTema.Chiavi.Contains(s)).ToList();

            Assert.True(sovrascritte.Count > 50, $"troppo poche voci d'accento: {sovrascritte.Count}");
            foreach (string chiave in sovrascritte)
                Assert.Equal(colori[chiave] is SolidColorBrush b ? b.Color : (Color)colori[chiave], finestra.FindResource(chiave) is SolidColorBrush c ? c.Color : (Color)finestra.FindResource(chiave));
        });
    }

    [Fact]
    public void CambiandoColoreETemaPiuVolte_NienteSiAccumula_EL_ultimoVince()
    {
        VisteTests.InSta(() =>
        {
            var finestra = new Window();
            var gestore = Gestore(finestra);

            foreach (var (tema, colore) in new[]
                     {
                         (TemaApp.Chiaro, ColoreApp.Fucina), (TemaApp.Scuro, ColoreApp.Foresta), (TemaApp.Scuro, ColoreApp.Grafite),
                         (TemaApp.Chiaro, ColoreApp.Prugna), (TemaApp.Chiaro, ColoreApp.ComeWindows), (TemaApp.Chiaro, ColoreApp.Acciaio)
                     })
                gestore.Applica(new AspettoApp(tema, colore));

            Assert.Equal(2, finestra.Resources.MergedDictionaries.Count);
            Assert.Equal(Di(ColoreApp.Acciaio).Scura1, Colore(finestra, "AccentButtonBackground"));
            Assert.Equal(Di(ColoreApp.Acciaio).Scura1, Colore(finestra, "BordoAccento"));
        });
    }

    [Fact]
    public void IColoriDelProgrammaCheDipendonoDallAccento_SeguonoIlColoreScelto()
    {
        VisteTests.InSta(() =>
        {
            var finestra = new Window();
            var gestore = Gestore(finestra);
            var s = Di(ColoreApp.Foresta);

            gestore.Applica(new AspettoApp(TemaApp.Chiaro, ColoreApp.Foresta));
            Assert.Equal(s.Scura1, Colore(finestra, "BordoAccento"));
            Assert.Equal(s.Scura2, Colore(finestra, "TestoAccento"));

            gestore.Applica(new AspettoApp(TemaApp.Scuro, ColoreApp.Foresta));
            Assert.Equal(s.Chiara2, Colore(finestra, "BordoAccento"));
            Assert.Equal(s.Chiara3, Colore(finestra, "TestoAccento"));
        });
    }

    [Fact]
    public void LoSfondoColorato_SiApplicaESiToglie_ConIlGestore()
    {
        VisteTests.InSta(() =>
        {
            var finestra = new Window();
            var gestore = Gestore(finestra);
            var s = Di(ColoreApp.Acciaio);

            gestore.Applica(new AspettoApp(TemaApp.Chiaro, ColoreApp.Acciaio, SfondoColorato: true));
            var colorato = Colore(finestra, "SfondoFinestra");
            gestore.Applica(new AspettoApp(TemaApp.Chiaro, ColoreApp.Acciaio, SfondoColorato: false));
            var neutro = Colore(finestra, "SfondoFinestra");

            Assert.Equal(Colori.Da("#FAFAFA"), neutro);
            Assert.NotEqual(neutro, colorato);
            Assert.Equal(colorato, ColoriTema.Crea(false, s, true) is var d ? ((SolidColorBrush)d["SfondoFinestra"]).Color : default);
        });
    }

    [Fact]
    public void ConIlColoreComeWindows_ILCambioDelColoreDiWindows_RifaLeSfumature()
    {
        VisteTests.InSta(() =>
        {
            var finestra = new Window();
            var gestore = Gestore(finestra);
            gestore.Applica(new AspettoApp(TemaApp.Chiaro, ColoreApp.ComeWindows));
            var prima = finestra.Resources.MergedDictionaries[^1];

            gestore.SistemaCambiato();

            Assert.NotSame(prima, finestra.Resources.MergedDictionaries[^1]); // ha rifatto i colori
        });
    }

    [Fact]
    public void ConUnColoreFissoETemaFisso_IlCambioDiWindows_NonFaNiente()
    {
        VisteTests.InSta(() =>
        {
            var finestra = new Window();
            var gestore = Gestore(finestra);
            gestore.Applica(new AspettoApp(TemaApp.Scuro, ColoreApp.Fucina));
            var prima = finestra.Resources.MergedDictionaries[^1];

            gestore.SistemaCambiato();

            Assert.Same(prima, finestra.Resources.MergedDictionaries[^1]);
        });
    }

    [Fact]
    public void SenzaLoStileDiWindows_LeSovrascritture_NonCiSono_EIlResto_Funziona()
    {
        var gestore = new GestoreAspetto(new OspiteNullo(), () => false);

        gestore.Applica(new AspettoApp(TemaApp.Chiaro, ColoreApp.Fucina, SfondoColorato: true));

        Assert.Equal(new AspettoApp(TemaApp.Chiaro, ColoreApp.Fucina, true), gestore.Corrente);
    }
}

/// <summary>Le scelte di colore e sfondo nella finestra Impostazioni: prova immediata, annullamento, salvataggio.</summary>
public class ColoreDalleImpostazioniTests : IDisposable
{
    private readonly ArchivioDiProva _a = new();
    private readonly ImpostazioniService _servizio;
    private readonly ImpostazioniApp _impostazioni;
    private readonly FintoAspetto _aspetto = new();

    public ColoreDalleImpostazioniTests()
    {
        _servizio = new ImpostazioniService(_a.Tmp.Combina("impostazioni", "impostazioni.json"));
        _impostazioni = new ImpostazioniApp { PercorsoRadice = _a.Radice, RiepilogoAvvio = false, Tema = TemaApp.Chiaro };
    }

    public void Dispose() => _a.Dispose();

    private MainViewModel Principale() => new(
        _a.Servizio, _a.Files, _a.Dialog, _a.Shell, _impostazioni,
        new AlertService(_impostazioni, new TempoFisso(new DateTime(2026, 10, 1, 9, 0, 0))),
        servizioImpostazioni: _servizio, aspetto: _aspetto);

    private ImpostazioniApp DalFile() => new ImpostazioniService(_servizio.PercorsoFile).Carica();

    private static ImpostazioniViewModel Modello(ImpostazioniApp? partenza = null, Action<AspettoApp>? anteprima = null) =>
        new(partenza ?? new ImpostazioniApp(), @"C:\x\impostazioni.json", anteprimaAspetto: anteprima);

    // ---------- Il modello della finestra ----------

    [Fact]
    public void LaFinestra_PartedalleScelteAttuali()
    {
        var m = Modello(new ImpostazioniApp { Colore = ColoreApp.Foresta, SfondoColorato = true });

        Assert.Equal(ColoreApp.Foresta, m.Colore);
        Assert.True(m.SfondoColorato);
        Assert.Equal(["Acciaio", "Fucina", "Foresta", "Prugna", "Grafite", "Come Windows"], m.OpzioniColore.Select(o => o.Nome));
        Assert.Equal(["Foresta"], m.OpzioniColore.Where(o => o.Scelto).Select(o => o.Nome));
    }

    [Fact]
    public void ScegliereUnPulsanteColore_CambiaIlColore_EGliAltriSiSpengono_ESiAvvisaLaVista()
    {
        var m = Modello();
        var avvisate = new List<string>();
        foreach (var o in m.OpzioniColore)
            o.PropertyChanged += (_, e) => avvisate.Add($"{o.Nome}:{e.PropertyName}");

        m.OpzioniColore.Single(o => o.Id == ColoreApp.Grafite).Scelto = true;

        Assert.Equal(ColoreApp.Grafite, m.Colore);
        Assert.Equal(["Grafite"], m.OpzioniColore.Where(o => o.Scelto).Select(o => o.Nome));
        Assert.Equal(m.OpzioniColore.Select(o => $"{o.Nome}:Scelto").Order(), avvisate.Order()); // tutti si riaggiornano
    }

    [Fact]
    public void UnPulsanteColoreCheSiSpegne_NonCambiaIlColore()
    {
        var m = Modello();

        m.OpzioniColore.Single(o => o.Id == ColoreApp.Acciaio).Scelto = false;

        Assert.Equal(ColoreApp.Acciaio, m.Colore);
    }

    [Fact]
    public void IPallini_HannoIColoriDellePalette_ELaVoceDiWindowsIlColoreDiWindows()
    {
        var m = Modello();

        foreach (var o in m.OpzioniColore)
        {
            var atteso = Tavolozze.Principale(o.Id);
            Assert.Equal(atteso, ((SolidColorBrush)o.Pennello).Color);
            Assert.True(o.Pennello.IsFrozen);
        }
    }

    [Fact]
    public void OgniScelta_AvvisaLAnteprima_ConTuttoLAspetto()
    {
        var anteprime = new List<AspettoApp>();
        var m = Modello(new ImpostazioniApp { Tema = TemaApp.Scuro }, anteprime.Add);

        m.OpzioniColore.Single(o => o.Id == ColoreApp.Fucina).Scelto = true;
        m.SfondoColorato = true;
        m.TemaChiaro = true;
        m.SfondoColorato = true; // già così: niente da avvisare

        Assert.Equal(
        [
            new AspettoApp(TemaApp.Scuro, ColoreApp.Fucina, false),
            new AspettoApp(TemaApp.Scuro, ColoreApp.Fucina, true),
            new AspettoApp(TemaApp.Chiaro, ColoreApp.Fucina, true)
        ], anteprime);
    }

    [Fact]
    public void Costruisci_PortaColoreESfondo_ELeAltreImpostazioniRestano()
    {
        var attuali = new ImpostazioniApp { NomeRadice = "Archivio", Colore = ColoreApp.Acciaio, SfondoColorato = false };
        var m = Modello(attuali);

        m.OpzioniColore.Single(o => o.Id == ColoreApp.Prugna).Scelto = true;
        m.SfondoColorato = true;
        var nuove = m.Costruisci();

        Assert.Equal(ColoreApp.Prugna, nuove.Colore);
        Assert.True(nuove.SfondoColorato);
        Assert.Equal("Archivio", nuove.NomeRadice);
        Assert.Equal(ColoreApp.Acciaio, attuali.Colore); // l'originale non si tocca finché non si salva
        Assert.False(attuali.SfondoColorato);
    }

    [Fact]
    public void ScegliereColoreESfondo_NonImpedisceDiSalvare()
    {
        var m = Modello();

        m.OpzioniColore.Single(o => o.Id == ColoreApp.Fucina).Scelto = true;
        m.SfondoColorato = true;

        Assert.True(m.PuoSalvare);
        Assert.Equal("", m.Errore);
    }

    // ---------- Aprendo le Impostazioni dal programma ----------

    [Fact]
    public async Task Salvando_ColoreESfondoSiVedonoSubito_ERestanoNelFile()
    {
        var vm = Principale();
        _a.Dialog.RispondiImpostazioni(m =>
        {
            m.OpzioniColore.Single(o => o.Id == ColoreApp.Fucina).Scelto = true;
            m.SfondoColorato = true;
        });

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        var scelto = new AspettoApp(TemaApp.Chiaro, ColoreApp.Fucina, true);
        Assert.Equal(ColoreApp.Fucina, DalFile().Colore);
        Assert.True(DalFile().SfondoColorato);
        Assert.Equal(scelto, _impostazioni.Aspetto);
        Assert.Equal(scelto, _aspetto.Applicate[^1]);
    }

    [Fact]
    public async Task ConAnnulla_ColoreESfondoTornanoQuelliDiPrima()
    {
        _impostazioni.Colore = ColoreApp.Foresta;
        var vm = Principale();
        var durante = new List<AspettoApp>();
        _a.Dialog.RispondiImpostazioni(m =>
        {
            m.OpzioniColore.Single(o => o.Id == ColoreApp.Prugna).Scelto = true;
            m.SfondoColorato = true;
            durante.AddRange(_aspetto.Applicate);
            m.SogliaArancione = "abc"; // con valori non validi la finta finestra equivale ad Annulla
        });

        await vm.ApriImpostazioniCommand.ExecuteAsync(null);

        Assert.Equal(new AspettoApp(TemaApp.Chiaro, ColoreApp.Prugna, true), durante[^1]); // durante la scelta si vedeva
        Assert.Equal(new AspettoApp(TemaApp.Chiaro, ColoreApp.Foresta, false), _aspetto.Applicate[^1]); // annullando torna com'era
        Assert.Equal(ColoreApp.Foresta, _impostazioni.Colore);
        Assert.False(File.Exists(_servizio.PercorsoFile));
    }
}

/// <summary>Come si vedono colori e sfondo nelle finestre (e le immagini, se DOCUMENTALE_TEST_IMMAGINI indica una cartella).</summary>
[Collection("WPF")]
public class ColoreVisteTests
{
    private static Color Sfondo(Window finestra) => ((SolidColorBrush)finestra.Background).Color;

    private static IEnumerable<T> Tutti<T>(DependencyObject? radice) where T : DependencyObject
    {
        if (radice is null)
            yield break;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(radice); i++)
        {
            var figlio = VisualTreeHelper.GetChild(radice, i);
            if (figlio is T trovato)
                yield return trovato;
            foreach (var d in Tutti<T>(figlio))
                yield return d;
        }
    }

    [Theory]
    [InlineData(false, ColoreApp.Acciaio, false)]
    [InlineData(false, ColoreApp.Fucina, true)]
    [InlineData(false, ColoreApp.Foresta, true)]
    [InlineData(false, ColoreApp.Prugna, true)]
    [InlineData(false, ColoreApp.Grafite, true)]
    [InlineData(true, ColoreApp.Acciaio, true)]
    [InlineData(true, ColoreApp.Fucina, true)]
    [InlineData(true, ColoreApp.Foresta, false)]
    [InlineData(true, ColoreApp.Prugna, true)]
    public void LaFinestraImpostazioni_ConIColoriELoSfondo_SiVedeBene(bool scuro, ColoreApp colore, bool sfondoColorato)
    {
        var aspetto = new AspettoApp(scuro ? TemaApp.Scuro : TemaApp.Chiaro, colore, sfondoColorato);
        var modello = new ImpostazioniViewModel(
            new ImpostazioniApp { PercorsoRadice = @"C:\Documentale", CartellaBackup = @"E:\Backup", Tema = aspetto.Tema, Colore = colore, SfondoColorato = sfondoColorato },
            @"C:\Users\Marta\AppData\Roaming\DocumentaleMarta\impostazioni.json", backupDisponibile: true);

        var errori = VisteTests.InSta(() =>
        {
            AspettoDiProva.Applica(aspetto);
            var finestra = new ImpostazioniDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 640, 1000, $"colore-impostazioni-{(scuro ? "scuro" : "chiaro")}-{colore}-{(sfondoColorato ? "colorato" : "neutro")}".ToLowerInvariant());

            // La finestra ha lo sfondo del tema (neutro o con la tinta del colore scelto).
            var atteso = ((SolidColorBrush)finestra.FindResource("SfondoFinestra")).Color;
            Assert.Equal(atteso, Sfondo(finestra));
            Assert.Equal(sfondoColorato, atteso != Colori.Da(scuro ? "#202020" : "#FAFAFA"));

            // I pulsanti dei colori ci sono tutti e quello scelto è acceso.
            var scelte = Tutti<RadioButton>(contenuto).Where(r => r.GroupName == "Colore").ToList();
            Assert.Equal(6, scelte.Count);
            Assert.Equal(Tavolozze.Per(colore).Nome, ((TextBlock)Tutti<TextBlock>(scelte.Single(r => r.IsChecked == true)).Single()).Text);
            Assert.True(Tutti<CheckBox>(contenuto).Single(c => (string)c.Content == "Sfondo delle finestre colorato").IsChecked == sfondoColorato);
        });

        Assert.True(errori.Count == 0, string.Join("\n", errori.Distinct()));
    }

    [Fact]
    public void ScegliendoUnColoreNellaFinestra_ILModelloCambia_EIlPallinoSiAccende()
    {
        var modello = new ImpostazioniViewModel(new ImpostazioniApp { PercorsoRadice = @"C:\Documentale" }, @"C:\x\impostazioni.json");

        var errori = VisteTests.InSta(() =>
        {
            var finestra = new ImpostazioniDialog(modello);
            var contenuto = (FrameworkElement)finestra.Content;
            VisteTests.Disegna(contenuto, 640, 1000, "colore-impostazioni-clic");
            var scelte = Tutti<RadioButton>(contenuto).Where(r => r.GroupName == "Colore").ToList();

            scelte.Single(r => Tutti<TextBlock>(r).Single().Text == "Foresta").IsChecked = true;

            Assert.Equal(ColoreApp.Foresta, modello.Colore);
            Assert.Equal("Foresta", Tutti<TextBlock>(scelte.Single(r => r.IsChecked == true)).Single().Text);
        });

        Assert.True(errori.Count == 0, string.Join("\n", errori.Distinct()));
    }

    [Theory]
    [InlineData(false, ColoreApp.Fucina)]
    [InlineData(true, ColoreApp.Acciaio)]
    [InlineData(false, ColoreApp.Foresta)]
    [InlineData(true, ColoreApp.Prugna)]
    public async Task FinestraPrincipale_ConSfondoColorato_SiVedeBene(bool scuro, ColoreApp colore)
    {
        using var a = new ArchivioDiProva();
        var (vm, _) = await VisteTests.ArchivioConAvvisiAsync(a);
        vm.Radici[0].Aree.First().Figli.First().IsSelected = true;
        await vm.CaricamentoFormCompletato;

        var errori = VisteTests.InSta(() =>
        {
            var aspetto = new AspettoApp(scuro ? TemaApp.Scuro : TemaApp.Chiaro, colore, SfondoColorato: true);
            AspettoDiProva.Applica(aspetto);
            var finestra = new MainWindow(vm);
            VisteTests.Disegna((FrameworkElement)finestra.Content, 1180, 680, $"colore-finestra-{(scuro ? "scuro" : "chiaro")}-{colore}".ToLowerInvariant());

            Assert.Equal(((SolidColorBrush)finestra.FindResource("SfondoFinestra")).Color, Sfondo(finestra));
            Assert.NotEqual(Colori.Da(scuro ? "#202020" : "#FAFAFA"), Sfondo(finestra));
        });

        Assert.True(errori.Count == 0, string.Join("\n", errori.Distinct()));
    }

    [Fact]
    public async Task CambiandoAspettoConLaFinestraAperta_SfondoEColoriSiAggiornanoSubito()
    {
        using var a = new ArchivioDiProva();
        var (vm, _) = await VisteTests.ArchivioConAvvisiAsync(a);

        var errori = VisteTests.InSta(() =>
        {
            AspettoDiProva.Applica(new AspettoApp(TemaApp.Chiaro, ColoreApp.Acciaio, false));
            var finestra = new MainWindow(vm);
            var prima = Sfondo(finestra);

            AspettoDiProva.Gestore.Applica(new AspettoApp(TemaApp.Scuro, ColoreApp.Fucina, true));

            Assert.NotEqual(prima, Sfondo(finestra));
            Assert.Equal(((SolidColorBrush)finestra.FindResource("SfondoFinestra")).Color, Sfondo(finestra));
        });

        Assert.True(errori.Count == 0, string.Join("\n", errori.Distinct()));
    }
}

/// <summary>Ogni finestra del programma deve avere lo sfondo che segue il tema e la tinta.</summary>
public class SfondoNelleFinestreXamlTests
{
    [Fact]
    public void OgniFinestra_DichiaraLoSfondoDelTema()
    {
        var cartella = Path.Combine(AppContext.BaseDirectory, "XamlApp");
        var finestre = Directory.GetFiles(cartella, "*.xaml")
            .Select(f => (Nome: Path.GetFileName(f), Testo: File.ReadAllText(f)))
            .Where(f => Regex.IsMatch(f.Testo, @"^\s*<Window\b", RegexOptions.Multiline))
            .ToList();

        Assert.Equal(8, finestre.Count);
        Assert.All(finestre, f =>
        {
            var apertura = Regex.Match(f.Testo, @"<Window\b[^>]*>", RegexOptions.Singleline).Value;
            Assert.True(apertura.Contains("Background=\"{DynamicResource SfondoFinestra}\""), $"{f.Nome} non dichiara lo sfondo del tema");
        });
    }
}
