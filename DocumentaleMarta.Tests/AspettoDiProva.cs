using System.Windows;
using DocumentaleMarta.App.Grafica;
using DocumentaleMarta.Core.Impostazioni;

namespace DocumentaleMarta.Tests;

/// <summary>
/// Il tema nelle prove, come nel programma vero: sta nell'applicazione (una sola, sul thread WPF delle prove, vedi
/// <see cref="VisteTests.InSta"/>) ed è già applicato quando si crea una finestra. Di base è chiaro, perché le prove non
/// dipendano dalle impostazioni di Windows di chi le lancia; chi prova il tema scuro lo imposta e a fine prova torna chiaro da solo.
/// </summary>
internal static class AspettoDiProva
{
    private static GestoreAspetto? _gestore;

    /// <summary>Cosa risponde «Windows» quando il tema è «Come Windows».</summary>
    internal static bool WindowsScuro { get; set; }

    /// <summary>Da usare solo dal thread WPF delle prove.</summary>
    internal static GestoreAspetto Gestore => _gestore ??= new GestoreAspetto(new OspiteApplicazione(Application.Current), () => WindowsScuro);

    /// <summary>Cambia il tema di tutta l'applicazione delle prove (le finestre già create si adeguano, come nel programma).</summary>
    internal static GestoreAspetto Applica(TemaApp tema = TemaApp.Chiaro, bool windowsScuro = false) =>
        Applica(new AspettoApp(tema), windowsScuro);

    /// <summary>Cambia l'aspetto (tema, colore principale, sfondo) di tutta l'applicazione delle prove.</summary>
    internal static GestoreAspetto Applica(AspettoApp aspetto, bool windowsScuro = false)
    {
        if (Gestore.Corrente != aspetto || WindowsScuro != windowsScuro)
            ChiudiLeFinestre();

        WindowsScuro = windowsScuro;
        Gestore.Applica(aspetto);
        return Gestore;
    }

    /// <summary>
    /// Il tema di partenza di tutte le prove: chiaro. Per guardare come appaiono tutte le finestre in scuro (le immagini, con
    /// DOCUMENTALE_TEST_IMMAGINI) si può lanciare la suite con DOCUMENTALE_TEST_TEMA=Scuro: le prove che controllano i colori chiari falliranno.
    /// </summary>
    internal static TemaApp Base { get; } =
        Environment.GetEnvironmentVariable("DOCUMENTALE_TEST_TEMA") is "Scuro" ? TemaApp.Scuro : TemaApp.Chiaro;

    /// <summary>Rimette il tema di base, se qualche prova l'ha cambiato.</summary>
    internal static void Ripristina()
    {
        if (Gestore.Corrente != new AspettoApp(Base) || WindowsScuro)
            Applica(Base);
    }

    /// <summary>
    /// Le finestre delle prove restano nell'applicazione condivisa: a ogni cambio di tema WPF le rivaluterebbe tutte
    /// (sempre più lentamente, e con gli avvisi di binding delle prove precedenti). Chi cambia il tema le chiude prima.
    /// </summary>
    private static void ChiudiLeFinestre()
    {
        foreach (var finestra in Application.Current.Windows.OfType<Window>().ToList())
        {
            finestra.DataContext = null;
            finestra.Close();
        }
    }
}
