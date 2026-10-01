using System.Windows;

namespace DocumentaleMarta.App.Viste;

/// <summary>
/// Il calcolo dello zoom della finestra "Anteprima ingrandita". Lo zoom 1 è "pagina intera" (l'immagine sta tutta nella finestra,
/// come nel pannello); 2 vuol dire il doppio, e così via. Più in là c'è scorrimento.
/// </summary>
public static class Ingrandimento
{
    /// <summary>Pagina intera: l'immagine sta tutta nell'area visibile.</summary>
    public const double PaginaIntera = 1.0;

    public const double Massimo = 8.0;

    /// <summary>Di quanto cambia lo zoom a ogni pressione di + o −.</summary>
    public const double Passo = 1.25;

    /// <summary>Quanto spazio lasciare intorno alla pagina intera, in pixel.</summary>
    public const double Margine = 12;

    /// <summary>Lo spessore del bordo della pagina (un pixel per lato): conta, o la pagina intera sforerebbe e comparirebbe lo scorrimento.</summary>
    public const double Cornice = 2;

    /// <summary>
    /// La dimensione a cui disegnare l'immagine: la pagina intera nell'area visibile (senza mai deformarla) moltiplicata per lo zoom.
    /// </summary>
    /// <param name="naturale">Dimensione naturale dell'immagine (in unità WPF).</param>
    /// <param name="area">Lo spazio visibile nella finestra.</param>
    public static Size Dimensioni(Size naturale, Size area, double zoom)
    {
        if (naturale.Width <= 0 || naturale.Height <= 0)
            return Size.Empty;

        var utile = new Size(Math.Max(1, area.Width - 2 * Margine - Cornice), Math.Max(1, area.Height - 2 * Margine - Cornice));
        var scala = Math.Min(utile.Width / naturale.Width, utile.Height / naturale.Height) * Limita(zoom);
        return new Size(naturale.Width * scala, naturale.Height * scala);
    }

    /// <summary>Lo zoom dopo un passo in avanti (mai oltre il massimo).</summary>
    public static double Aumenta(double zoom) => Limita(Math.Round(zoom * Passo, 4));

    /// <summary>Lo zoom dopo un passo indietro (mai sotto la pagina intera).</summary>
    public static double Diminuisci(double zoom) => Limita(Math.Round(zoom / Passo, 4));

    /// <summary>Doppio clic: se si vede la pagina intera si passa al doppio, altrimenti si torna alla pagina intera.</summary>
    public static double AlternaDoppioClic(double zoom) => zoom <= PaginaIntera ? 2.0 : PaginaIntera;

    /// <summary>Tiene lo zoom tra la pagina intera e il massimo.</summary>
    public static double Limita(double zoom) => Math.Clamp(zoom, PaginaIntera, Massimo);

    /// <summary>"Pagina intera" oppure la percentuale rispetto alla pagina intera, es. "200%".</summary>
    public static string Testo(double zoom) =>
        Limita(zoom) <= PaginaIntera ? "Pagina intera" : $"{Limita(zoom) * 100:0}%";
}
