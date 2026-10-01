using System.Windows;
using System.Windows.Media;

namespace DocumentaleMarta.App.Grafica;

/// <summary>
/// Le sfumature di un colore principale: le sette che il tema di Windows usa per pulsanti, spunte, selezioni e bordi attivi
/// (in chiaro il colore dei pulsanti è <see cref="Scura1"/>, in scuro <see cref="Chiara2"/>).
/// </summary>
public readonly record struct Sfumature(
    Color Base, Color Scura1, Color Scura2, Color Scura3, Color Chiara1, Color Chiara2, Color Chiara3)
{
    /// <summary>Le sfumature di <paramref name="principale"/>, che è il colore dei pulsanti in tema chiaro.</summary>
    public static Sfumature Da(Color principale) => new(
        Colori.Mescola(principale, Colors.White, 0.10),
        principale,
        Colori.Mescola(principale, Colors.Black, 0.22),
        Colori.Mescola(principale, Colors.Black, 0.42),
        Colors.White.Mescola(principale, 0.78),
        Colors.White.Mescola(principale, 0.58),
        Colors.White.Mescola(principale, 0.40));
}

/// <summary>Piccoli calcoli sui colori.</summary>
public static class Colori
{
    /// <summary>Il colore a metà strada fra <paramref name="a"/> e <paramref name="b"/>: <paramref name="verso"/> = 0 è <paramref name="a"/>, 1 è <paramref name="b"/>.</summary>
    public static Color Mescola(this Color a, Color b, double verso) => Color.FromRgb(
        (byte)Math.Round(a.R + (b.R - a.R) * verso),
        (byte)Math.Round(a.G + (b.G - a.G) * verso),
        (byte)Math.Round(a.B + (b.B - a.B) * verso));

    /// <summary>Trasforma un colore scritto come testo esadecimale (es. «#2E7D32») in un colore WPF.</summary>
    public static Color Da(string esadecimale) => (Color)ColorConverter.ConvertFromString(esadecimale);
}

/// <summary>
/// I colori del programma che il tema di Windows non conosce: grigi dei testi e dei bordi, sfondi dei pannelli, colori delle scadenze
/// (arancione, rosso), delle cartelle completate e del promemoria del backup. Ne esiste una versione chiara e una scura;
/// le finestre li usano con <c>DynamicResource</c>, così cambiano quando cambia il tema.
/// </summary>
public static class ColoriTema
{
    /// <summary>Tutti i nomi che il dizionario contiene (e che i file XAML possono usare).</summary>
    public static readonly IReadOnlyList<string> Chiavi =
    [
        "Bordo", "BordoLeggero", "SfondoPannello", "SfondoSuperficie", "SfondoDivisore", "SfondoVisualizzatore",
        "TestoSecondario", "TestoTenue",
        "SfondoArancione", "SfondoRosso", "IconaArancione", "TestoArancione", "IconaRossa", "TestoRosso",
        "IconaCompletata", "TestoCompletata",
        "SfondoPromemoria", "BordoPromemoria", "IconaPromemoria",
        "SfondoAccentoLeggero", "BordoAccento", "TestoAccento"
    ];

    /// <summary>Il dizionario dei colori per il tema scuro o chiaro, con i colori che dipendono dall'accento ricavati da <paramref name="accento"/>.</summary>
    public static ResourceDictionary Crea(bool scuro, Sfumature accento)
    {
        var d = new ResourceDictionary();
        // Aggiunge un colore al dizionario: quello chiaro o quello scuro, secondo il tema che si sta preparando.
        void Pennello(string chiave, string chiaro, string scuroEsadecimale) =>
            d[chiave] = Congelato(new SolidColorBrush(Colori.Da(scuro ? scuroEsadecimale : chiaro)));
        // Aggiunge al dizionario un colore già calcolato (per quelli che dipendono dall'accento).
        void PennelloDa(string chiave, Color colore) => d[chiave] = Congelato(new SolidColorBrush(colore));

        // Struttura: bordi, sfondi dei pannelli, delle griglie e dello spazio dietro la pagina dell'anteprima.
        Pennello("Bordo", "#D0D0D0", "#4A4A4A");
        Pennello("BordoLeggero", "#E4E4E4", "#3A3A3A");
        Pennello("SfondoPannello", "#F5F5F5", "#272727");
        Pennello("SfondoSuperficie", "#FFFFFF", "#2B2B2B");
        Pennello("SfondoDivisore", "#E4E4E4", "#3A3A3A");
        Pennello("SfondoVisualizzatore", "#E9E9E9", "#161616");

        // Testi attenuati: etichette e spiegazioni.
        Pennello("TestoSecondario", "#555555", "#CFCFCF");
        Pennello("TestoTenue", "#6F6F6F", "#A0A0A0");

        // Scadenze: sfondo delle righe, icona e testo; uguali in albero, griglie e form.
        Pennello("SfondoArancione", "#FFF1DC", "#4A3718");
        Pennello("SfondoRosso", "#FDE4E4", "#4F2626");
        Pennello("IconaArancione", "#E08A00", "#FFB340");
        Pennello("TestoArancione", "#A85800", "#FFB74D");
        Pennello("IconaRossa", "#C42B1C", "#FF6B5E");
        Pennello("TestoRosso", "#B42318", "#FF8A80");

        // Cartelle completate: spunta verde e nome in grigio.
        Pennello("IconaCompletata", "#2E7D32", "#6CCB70");
        Pennello("TestoCompletata", "#777777", "#9A9A9A");

        // Promemoria del backup.
        Pennello("SfondoPromemoria", "#FFF4CE", "#433519");
        Pennello("BordoPromemoria", "#E6D08A", "#6B5A2A");
        Pennello("IconaPromemoria", "#9A6700", "#F5C45E");

        // Quel che dipende dal colore principale: dove si sta per rilasciare un file, testo e bordi di rilievo.
        PennelloDa("SfondoAccentoLeggero", scuro ? Colori.Da("#202020").Mescola(accento.Scura1, 0.40) : Colors.White.Mescola(accento.Scura1, 0.16));
        PennelloDa("BordoAccento", scuro ? accento.Chiara2 : accento.Scura1);
        PennelloDa("TestoAccento", scuro ? accento.Chiara3 : accento.Scura2);

        return d;
    }

    /// <summary>
    /// Rende il pennello non più modificabile: così costa meno e si può usare da qualsiasi parte del programma.
    /// </summary>
    private static SolidColorBrush Congelato(SolidColorBrush pennello)
    {
        pennello.Freeze();
        return pennello;
    }
}
