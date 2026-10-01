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

    private static TreeViewItem? TrovaNodo(DependencyObject? origine) => AlberoVisuale.Antenato<TreeViewItem>(origine);

    // ---------- Trascinamento sull'albero ----------

    private TreeViewItem? _bersaglioEvidenziato;

    /// <summary>
    /// Su una cartella dell'albero si può rilasciare un documento preso da una griglia (lo si sposta lì, se non è già lì)
    /// o dei file presi da Esplora file (si allegano alla cartella). Altrove no.
    /// </summary>
    private void Albero_DragOver(object sender, DragEventArgs e)
    {
        var nodo = TrovaNodo(e.OriginalSource as DependencyObject);
        var cartella = nodo?.DataContext as NodoAlberoViewModel;

        var effetto = DragDropEffects.None;
        if (cartella is { Tipo: TipoNodo.Cartella })
        {
            if (DatiTrascinati.HaFile(e.Data))
                effetto = DragDropEffects.Copy;
            else if (DatiTrascinati.Documento(e.Data) is { } documento && documento.CartellaOrigineId != cartella.Id)
                effetto = DragDropEffects.Move;
        }

        e.Effects = effetto;
        Evidenzia(effetto == DragDropEffects.None ? null : nodo);
        e.Handled = true;
    }

    private void Albero_DragLeave(object sender, DragEventArgs e) => Evidenzia(null);

    private async void Albero_Drop(object sender, DragEventArgs e)
    {
        Evidenzia(null);
        e.Handled = true;

        if (TrovaNodo(e.OriginalSource as DependencyObject)?.DataContext is not NodoAlberoViewModel { Tipo: TipoNodo.Cartella } cartella
            || DataContext is not MainViewModel modello)
            return;

        if (DatiTrascinati.HaFile(e.Data))
            await modello.AllegaATrascinatiAsync(cartella.Id, DatiTrascinati.File(e.Data));
        else if (DatiTrascinati.Documento(e.Data) is { } documento && documento.CartellaOrigineId != cartella.Id)
            await modello.SpostaDocumentoAsync(documento.DocumentoId, cartella.Id);
    }

    private void Evidenzia(TreeViewItem? nodo)
    {
        if (ReferenceEquals(_bersaglioEvidenziato, nodo))
            return;

        if (_bersaglioEvidenziato is not null)
            BersaglioTrascinamento.SetAttivo(_bersaglioEvidenziato, false);
        _bersaglioEvidenziato = nodo;
        if (nodo is not null)
            BersaglioTrascinamento.SetAttivo(nodo, true);
    }
}
