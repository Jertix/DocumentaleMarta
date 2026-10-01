using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace DocumentaleMarta.App.Viste;

/// <summary>
/// Il doppio clic su una riga di una griglia. Si ascolta il clic (non l'evento "doppio clic" della riga, che non dice
/// su cosa si è cliccato) anche se qualcuno l'ha già gestito: così si sa se il clic era su un pulsante o sull'intestazione.
/// </summary>
public static class DoppioClicGriglia
{
    /// <summary>Collega la griglia: al doppio clic su una riga esegue <paramref name="azione"/> con i dati della riga.</summary>
    public static void Collega<TRiga>(DataGrid griglia, Action<TRiga> azione) where TRiga : class
    {
        // Si usa MouseDown perché, a differenza di MouseLeftButtonDown, risale dagli elementi interni fino alla griglia.
        griglia.AddHandler(UIElement.MouseDownEvent, new MouseButtonEventHandler((_, e) => Gestisci(e, azione)), handledEventsToo: true);
    }

    /// <summary>
    /// Se il doppio clic è su una riga (non su un pulsante né sull'intestazione) esegue l'azione con i dati di quella riga.
    /// </summary>
    private static void Gestisci<TRiga>(MouseButtonEventArgs e, Action<TRiga> azione) where TRiga : class
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 2)
            return;

        var origine = e.OriginalSource as DependencyObject;

        // Un doppio clic veloce su un pulsante (es. "Apri") non deve anche cambiare cartella.
        if (AlberoVisuale.Antenato<ButtonBase>(origine) is not null)
            return;

        // Sull'intestazione delle colonne o sullo spazio vuoto non c'è nessuna riga: nessun effetto.
        if (AlberoVisuale.Antenato<DataGridRow>(origine) is { DataContext: TRiga riga })
            azione(riga);
    }
}
