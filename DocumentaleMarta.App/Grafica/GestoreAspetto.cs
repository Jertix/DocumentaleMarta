using System.IO;
using System.Windows;
using DocumentaleMarta.Core.Impostazioni;
using Microsoft.Win32;

namespace DocumentaleMarta.App.Grafica;

/// <summary>Cambia l'aspetto del programma (tema chiaro o scuro) senza riavviarlo.</summary>
public interface IAspettoService
{
    void Applica(AspettoApp aspetto);
}

/// <summary>Dove si applica il tema: tutto il programma o, nelle prove, una sola finestra.</summary>
public interface IOspiteTema
{
    /// <summary>Le risorse a cui il tema e i colori del programma vengono aggiunti.</summary>
    ResourceDictionary Risorse { get; }

    /// <summary>Mette lo stile di Windows 11 in versione chiara o scura al posto di quello che c'era.</summary>
    void ImpostaTema(bool scuro);
}

/// <summary>Il programma intero: tutte le finestre, anche quelle che si apriranno dopo.</summary>
public sealed class OspiteApplicazione(Application applicazione) : IOspiteTema
{
    public ResourceDictionary Risorse => applicazione.Resources;

    public void ImpostaTema(bool scuro) =>
#pragma warning disable WPF0001 // il tema Fluent di .NET 10 è ancora segnato come sperimentale
        applicazione.ThemeMode = scuro ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
}

/// <summary>
/// Una finestra sola (per le prove, dove non c'è un'applicazione). Lo stile di Windows 11 lo mette a mano: WPF, su una singola finestra,
/// smette di cambiarlo appena nelle sue risorse viene aggiunto un altro dizionario (sull'applicazione intera invece funziona).
/// </summary>
public sealed class OspiteFinestra(Window finestra) : IOspiteTema
{
    private ResourceDictionary? _stile;

    public ResourceDictionary Risorse => finestra.Resources;

    public void ImpostaTema(bool scuro)
    {
        var uniti = finestra.Resources.MergedDictionaries;
        if (_stile is not null)
            uniti.Remove(_stile);

        _stile = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/Fluent.{(scuro ? "Dark" : "Light")}.xaml")
        };
        uniti.Insert(0, _stile);
    }
}

/// <summary>Nessuna applicazione né finestra: non fa niente (per chi costruisce i servizi senza aprire nulla).</summary>
public sealed class OspiteNullo : IOspiteTema
{
    public ResourceDictionary Risorse { get; } = new();

    public void ImpostaTema(bool scuro)
    {
    }
}

/// <summary>Il tema chiaro o scuro che Windows ha scelto per le applicazioni.</summary>
public static class TemaDiSistema
{
    public static bool UsaScuro()
    {
        try
        {
            using var chiave = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return chiave?.GetValue("AppsUseLightTheme") is int chiaro && chiaro == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>
/// Applica l'aspetto scelto: lo stile di Windows 11 chiaro o scuro (con pulsanti, caselle e griglie arrotondati) e, sopra, i colori
/// del programma (<see cref="ColoriTema"/>). Si può richiamare quante volte si vuole: ogni volta sostituisce quel che c'era.
/// </summary>
public sealed class GestoreAspetto(IOspiteTema ospite, Func<bool>? sistemaScuro = null) : IAspettoService
{
    private readonly Func<bool> _sistemaScuro = sistemaScuro ?? TemaDiSistema.UsaScuro;
    private ResourceDictionary? _colori;
    private AspettoApp _corrente = new(TemaApp.ComeWindows);

    /// <summary>Se in questo momento si vede il tema scuro.</summary>
    public bool ScuroInUso { get; private set; }

    /// <summary>Le ultime scelte applicate.</summary>
    public AspettoApp Corrente => _corrente;

    public void Applica(AspettoApp aspetto)
    {
        _corrente = aspetto;
        ScuroInUso = aspetto.Tema switch
        {
            TemaApp.Scuro => true,
            TemaApp.Chiaro => false,
            _ => _sistemaScuro()
        };

        // WPF sostituisce lo stile di Windows 11 solo se nelle risorse non c'è altro: i colori del programma si tolgono prima del cambio
        // e si rimettono dopo (in fondo, così, in caso di nomi uguali, vincono loro).
        var uniti = ospite.Risorse.MergedDictionaries;
        if (_colori is not null)
            uniti.Remove(_colori);

        ospite.ImpostaTema(ScuroInUso);

        _colori = ColoriTema.Crea(ScuroInUso, Sfumature.Da(SystemColors.AccentColor));
        uniti.Add(_colori);
    }

    /// <summary>Windows ha cambiato i suoi colori: se si segue Windows, ci si adegua.</summary>
    public void SistemaCambiato()
    {
        if (_corrente.Tema == TemaApp.ComeWindows)
            Applica(_corrente);
    }
}
