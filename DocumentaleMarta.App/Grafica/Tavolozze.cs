using System.Windows;
using System.Windows.Media;
using DocumentaleMarta.Core.Impostazioni;

namespace DocumentaleMarta.App.Grafica;

/// <summary>Un colore principale tra cui scegliere nelle Impostazioni.</summary>
/// <param name="Principale">Il colore dei pulsanti principali in tema chiaro; null per «Come Windows» (si usa quello di Windows).</param>
public record Tavolozza(ColoreApp Id, string Nome, Color? Principale);

/// <summary>I colori principali offerti all'utente. Tutti hanno testo bianco leggibile sui pulsanti in tema chiaro (contrasto di almeno 4,5).</summary>
public static class Tavolozze
{
    /// <summary>Le scelte, nell'ordine in cui compaiono nelle Impostazioni.</summary>
    public static readonly IReadOnlyList<Tavolozza> Tutte =
    [
        new(ColoreApp.Acciaio, "Acciaio", Colori.Da("#1F6AA5")),
        new(ColoreApp.Fucina, "Fucina", Colori.Da("#B8500A")),
        new(ColoreApp.Foresta, "Foresta", Colori.Da("#2A7A4B")),
        new(ColoreApp.Prugna, "Prugna", Colori.Da("#7B4BA8")),
        new(ColoreApp.Grafite, "Grafite", Colori.Da("#475569")),
        new(ColoreApp.ComeWindows, "Come Windows", null)
    ];

    /// <summary>La scelta corrispondente a un colore.</summary>
    public static Tavolozza Per(ColoreApp colore) => Tutte.First(t => t.Id == colore);

    /// <summary>Il colore principale vero e proprio: quello della palette o, per «Come Windows», quello scelto in Windows.</summary>
    public static Color Principale(ColoreApp colore) => Per(colore).Principale ?? SystemColors.AccentColor;
}

/// <summary>
/// Cambia il colore principale dello stile di Windows 11. Lo stile usa il colore scelto in Windows per pulsanti, spunte, selezioni,
/// bordi attivi e link, in tante voci diverse (ognuna in una delle sette sfumature): qui si cercano tutte e se ne scrive una
/// copia con le sfumature del colore scelto, da aggiungere dopo lo stile (così vincono le nostre).
/// </summary>
public static class AccentoTema
{
    /// <summary>Le sette sfumature dell'accento di Windows, nello stesso ordine di <see cref="Sfumature"/>.</summary>
    private static Color[] DiWindows() =>
    [
        SystemColors.AccentColor, SystemColors.AccentColorDark1, SystemColors.AccentColorDark2, SystemColors.AccentColorDark3,
        SystemColors.AccentColorLight1, SystemColors.AccentColorLight2, SystemColors.AccentColorLight3
    ];

    private static readonly string[] NomiSfumature =
    [
        "SystemAccentColor", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3",
        "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3"
    ];

    /// <summary>
    /// Le voci dello stile di Windows (pennelli e colori) che hanno il colore di una sfumatura dell'accento di Windows, riscritte con la
    /// sfumatura corrispondente di <paramref name="nuove"/> (trasparenza compresa), più le sette sfumature stesse.
    /// </summary>
    public static ResourceDictionary Sovrascritture(ResourceDictionary stileWindows, Sfumature nuove)
    {
        var vecchie = DiWindows();
        Color[] sostituti = [nuove.Base, nuove.Scura1, nuove.Scura2, nuove.Scura3, nuove.Chiara1, nuove.Chiara2, nuove.Chiara3];
        var risultato = new ResourceDictionary();

        foreach (var chiave in stileWindows.Keys.Cast<object>().ToList())
        {
            if (chiave is not string nome)
                continue;

            switch (stileWindows[nome])
            {
                case SolidColorBrush pennello when Sostituto(pennello.Color, vecchie, sostituti) is { } nuovo:
                    risultato[nome] = Congelato(new SolidColorBrush(nuovo));
                    break;
                case Color colore when Sostituto(colore, vecchie, sostituti) is { } nuovoColore:
                    risultato[nome] = nuovoColore;
                    break;
            }
        }

        for (var i = 0; i < NomiSfumature.Length; i++)
            risultato[NomiSfumature[i]] = sostituti[i];
        return risultato;
    }

    /// <summary>La sfumatura nuova per un colore, se è una sfumatura dell'accento di Windows (stessa trasparenza); null altrimenti.</summary>
    private static Color? Sostituto(Color colore, Color[] vecchie, Color[] sostituti)
    {
        // Bianco e nero puri sono nel tema per altri motivi (testo sui pulsanti, bordi): mai accenti.
        if (colore.R == colore.G && colore.G == colore.B && colore.R is 0 or 255)
            return null;

        var i = Array.FindIndex(vecchie, v => v.R == colore.R && v.G == colore.G && v.B == colore.B);
        return i < 0 ? null : Color.FromArgb(colore.A, sostituti[i].R, sostituti[i].G, sostituti[i].B);
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
