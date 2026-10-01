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
}
