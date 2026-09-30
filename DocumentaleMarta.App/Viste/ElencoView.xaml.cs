using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

public partial class ElencoView : UserControl
{
    public ElencoView()
    {
        InitializeComponent();

        // La colonna "Area" ha senso solo nell'elenco di tutto l'archivio: in quello di un'area sarebbe sempre uguale.
        DataContextChanged += (_, e) =>
            ColonnaArea.Visibility = e.NewValue is ElencoDocumentiViewModel { MostraArea: false }
                ? Visibility.Collapsed
                : Visibility.Visible;

        // Si ascolta il clic (non l'evento "doppio clic" della riga, che non dice su cosa si è cliccato) anche se
        // qualcuno l'ha già gestito: così si sa se il clic era su un pulsante o sull'intestazione.
        // Si usa MouseDown perché, a differenza di MouseLeftButtonDown, risale dagli elementi interni fino alla griglia.
        Griglia.AddHandler(MouseDownEvent, new MouseButtonEventHandler(Griglia_Clic), handledEventsToo: true);
    }

    /// <summary>Doppio clic su una riga: si va alla cartella del documento.</summary>
    private void Griglia_Clic(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || e.ClickCount != 2)
            return;

        var origine = e.OriginalSource as DependencyObject;

        // Un doppio clic veloce su un pulsante (es. "Apri") non deve anche cambiare cartella.
        if (Antenato<ButtonBase>(origine) is not null)
            return;

        // Sull'intestazione delle colonne o sullo spazio vuoto non c'è nessuna riga: nessun effetto.
        if (Antenato<DataGridRow>(origine) is { DataContext: DocumentoElencoViewModel documento })
            documento.VaiAllaCartellaCommand.Execute(null);
    }

    /// <summary>Risale dall'elemento cliccato (compreso lui) fino al primo elemento del tipo cercato.</summary>
    private static T? Antenato<T>(DependencyObject? origine) where T : DependencyObject
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
