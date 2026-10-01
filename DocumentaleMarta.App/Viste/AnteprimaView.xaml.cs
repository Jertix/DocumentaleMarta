using System.Windows.Controls;
using System.Windows.Input;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

public partial class AnteprimaView : UserControl
{
    public AnteprimaView() => InitializeComponent();

    /// <summary>Doppio clic sull'immagine: la pagina si apre ingrandita in una finestra a parte.</summary>
    private void Immagine_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || DataContext is not AnteprimaViewModel modello || !modello.IngrandisciCommand.CanExecute(null))
            return;

        modello.IngrandisciCommand.Execute(null);
        e.Handled = true;
    }
}
