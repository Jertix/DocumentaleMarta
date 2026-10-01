using System.Windows;
using System.Windows.Media;

namespace DocumentaleMarta.App.Viste;

/// <summary>Piccoli aiuti per risalire dall'elemento su cui si è cliccato o rilasciato qualcosa al controllo che lo contiene.</summary>
public static class AlberoVisuale
{
    /// <summary>Risale dall'elemento (compreso lui) fino al primo elemento del tipo cercato.</summary>
    public static T? Antenato<T>(DependencyObject? origine) where T : DependencyObject
    {
        while (origine is not null)
        {
            if (origine is T trovato)
                return trovato;
            origine = origine is Visual ? VisualTreeHelper.GetParent(origine) : LogicalTreeHelper.GetParent(origine);
        }
        return null;
    }

    /// <summary>Il primo elemento del tipo cercato tra i discendenti visivi (in ordine di profondità), o null.</summary>
    public static T? Discendente<T>(DependencyObject? radice) where T : DependencyObject
    {
        if (radice is null)
            return null;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(radice); i++)
        {
            var figlio = VisualTreeHelper.GetChild(radice, i);
            if (figlio is T trovato)
                return trovato;
            if (Discendente<T>(figlio) is { } piuIn)
                return piuIn;
        }
        return null;
    }
}
