using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InizializzaAsync();

        // Ctrl+F porta il cursore nel campo di ricerca, come in tutti i programmi.
        InputBindings.Add(new KeyBinding(
            new RelayCommand(() =>
            {
                CasellaRicerca.Focus();
                CasellaRicerca.SelectAll();
            }),
            Key.F, ModifierKeys.Control));

        // Se il PC è rimasto acceso durante la notte, al ritorno nella finestra gli avvisi vanno rifatti per la nuova data.
        Activated += (_, _) => viewModel.ControllaCambioData();

        // I campi del form si salvano quando perdono il cursore: chiudendo la finestra mentre si scrive
        // l'ultima modifica andrebbe persa, quindi si toglie il cursore dal campo prima di chiudere.
        Closing += (_, _) => Keyboard.ClearFocus();
    }

    /// <summary>Il tasto destro in un TreeView non seleziona il nodo: lo facciamo noi, così il menu agisce su quello cliccato.</summary>
    private void Albero_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TrovaNodo(e.OriginalSource as DependencyObject) is { } nodo)
        {
            nodo.Focus();
            nodo.IsSelected = true;
        }
    }

    /// <summary>Dopo una creazione o una ricarica il nodo selezionato può essere fuori vista: lo si porta in vista.</summary>
    private void NodoAlbero_Selected(object sender, RoutedEventArgs e)
    {
        // L'evento risale ai nodi padre: si reagisce solo per il nodo che l'ha generato.
        if (ReferenceEquals(sender, e.OriginalSource) && sender is TreeViewItem nodo)
            nodo.BringIntoView();
    }

    private static TreeViewItem? TrovaNodo(DependencyObject? origine)
    {
        while (origine is not null and not TreeViewItem)
            origine = origine is Visual ? VisualTreeHelper.GetParent(origine) : LogicalTreeHelper.GetParent(origine);
        return origine as TreeViewItem;
    }
}
