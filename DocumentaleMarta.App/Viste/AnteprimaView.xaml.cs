using System.Windows.Controls;
using System.Windows.Input;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

/// <summary>Il pannello a destra con l'anteprima del documento selezionato.</summary>
public partial class AnteprimaView : UserControl
{
    /// <summary>Crea il pannello dell'anteprima.</summary>
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
