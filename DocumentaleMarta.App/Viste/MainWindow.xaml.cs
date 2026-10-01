using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

/// <summary>
/// La finestra principale: barra degli strumenti con la ricerca, albero a sinistra, contenuto al centro (elenco o form) e
/// anteprima a destra.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>
    /// Crea la finestra principale: carica l'archivio all'apertura, collega Ctrl+F alla ricerca, rifà gli avvisi se è
    /// cambiato il giorno, salva l'ultima modifica alla chiusura e adatta la colonna dell'albero ai nomi lunghi.
    /// </summary>
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

        // Aprendo o chiudendo un ramo, o rileggendo l'albero, la colonna dell'albero si adatta ai nomi più lunghi che si vedono.
        Albero.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler((_, _) => PianificaAdattamentoAlbero()));
        Albero.AddHandler(TreeViewItem.CollapsedEvent, new RoutedEventHandler((_, _) => PianificaAdattamentoAlbero()));
        viewModel.Radici.CollectionChanged += (_, _) => PianificaAdattamentoAlbero();
        Loaded += (_, _) => PianificaAdattamentoAlbero();
    }

    // ---------- Larghezza della colonna dell'albero ----------

    /// <summary>La larghezza di partenza della colonna dell'albero: la stessa scritta nell'XAML.</summary>
    private const double LarghezzaAlberoDiPartenza = 270;

    /// <summary>Quanto spazio lasciare a destra del nome più lungo, perché non finisca contro il bordo.</summary>
    private const double AriaADestraDelTesto = 10;

    /// <summary>Al massimo, l'albero può occupare questa parte della finestra: il resto serve al contenuto.</summary>
    private const double QuotaMassimaAlbero = 0.5;

    /// <summary>
    /// La larghezza scelta dall'utente (o quella di partenza): l'albero torna a questa quando i nomi lunghi non si vedono più,
    /// per non rimpicciolirsi sotto quello che l'utente ha deciso trascinando il divisore.
    /// </summary>
    private double _larghezzaScelta = LarghezzaAlberoDiPartenza;

    private bool _adattamentoPianificato;

    /// <summary>L'utente ha trascinato il divisore: la larghezza ottenuta è quella che vuole (finché non servono nomi più lunghi).</summary>
    private void Divisore_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        _larghezzaScelta = ColonnaAlbero.ActualWidth;

    /// <summary>
    /// Chiede di adattare la larghezza dell'albero quando WPF ha finito di creare e disporre i nodi (altrimenti si misurerebbe
    /// prima che compaiano i nomi del ramo appena aperto). Più richieste ravvicinate valgono una sola.
    /// </summary>
    private void PianificaAdattamentoAlbero()
    {
        if (_adattamentoPianificato)
            return;

        _adattamentoPianificato = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            _adattamentoPianificato = false;
            AdattaLarghezzaAlbero();
        });
    }

    /// <summary>
    /// Porta la colonna dell'albero alla larghezza che serve a vedere per intero i nomi dei nodi visibili, senza scendere sotto la
    /// larghezza scelta dall'utente né salire oltre metà finestra (lasciando al contenuto il suo spazio minimo).
    /// </summary>
    public void AdattaLarghezzaAlbero()
    {
        var obiettivo = Math.Max(_larghezzaScelta, LarghezzaNecessaria() ?? 0);
        obiettivo = Math.Max(ColonnaAlbero.MinWidth, Math.Min(obiettivo, LarghezzaMassimaAlbero()));

        if (Math.Abs(obiettivo - ColonnaAlbero.Width.Value) >= 1)
            ColonnaAlbero.Width = new GridLength(obiettivo);
    }

    /// <summary>Metà finestra, ma sempre lasciando al contenuto (e all'anteprima, se aperta) lo spazio che gli spetta.</summary>
    private double LarghezzaMassimaAlbero()
    {
        var totale = Corpo.ActualWidth;
        if (totale <= 0)
            return double.MaxValue; // la finestra non è ancora disposta

        var lasciatoAgliAltri = ColonnaContenuto.MinWidth + Divisore.ActualWidth + ColonnaAnteprima.ActualWidth;
        return Math.Min(totale * QuotaMassimaAlbero, totale - lasciatoAgliAltri);
    }

    /// <summary>
    /// La larghezza della colonna che serve per vedere per intero il nodo più largo tra quelli visibili: il suo rientro (che dipende
    /// dal livello), l'icona, il nome e l'icona di avviso. Null se non c'è ancora nulla di disposto da misurare.
    /// </summary>
    private double? LarghezzaNecessaria()
    {
        var scorrimento = AlberoVisuale.Discendente<ScrollViewer>(Albero);
        var spostamento = scorrimento?.HorizontalOffset ?? 0; // se l'albero è scorso di lato le posizioni sono spostate
        double? piuLargo = null;

        foreach (var nodo in NodiVisibili(Albero))
        {
            if (FineDelNome(nodo, spostamento) is { } fine)
                piuLargo = Math.Max(piuLargo ?? 0, fine);
        }
        if (piuLargo is null)
            return null;

        var barraVerticale = scorrimento?.ComputedVerticalScrollBarVisibility == Visibility.Visible
            ? SystemParameters.VerticalScrollBarWidth
            : 0;
        return piuLargo + AriaADestraDelTesto + Albero.Padding.Right + Albero.BorderThickness.Right + barraVerticale;
    }

    /// <summary>I nodi dell'albero che si vedono: quelli del primo livello e, per i rami aperti, i loro figli.</summary>
    private static IEnumerable<TreeViewItem> NodiVisibili(ItemsControl contenitore)
    {
        for (var i = 0; i < contenitore.Items.Count; i++)
        {
            if (contenitore.ItemContainerGenerator.ContainerFromIndex(i) is not TreeViewItem nodo)
                continue;

            yield return nodo;
            if (nodo.IsExpanded)
            {
                foreach (var figlio in NodiVisibili(nodo))
                    yield return figlio;
            }
        }
    }

    /// <summary>
    /// Dove finisce il contenuto (icona, nome, icona di avviso) di un nodo, rispetto al bordo sinistro dell'albero. Si sommano le
    /// larghezze volute dai singoli pezzi: quelle del contenitore sarebbero già tagliate alla larghezza attuale della colonna.
    /// </summary>
    private double? FineDelNome(TreeViewItem nodo, double spostamento)
    {
        if (nodo.Template?.FindName("PART_Header", nodo) is not ContentPresenter intestazione
            || VisualTreeHelper.GetChildrenCount(intestazione) == 0
            || VisualTreeHelper.GetChild(intestazione, 0) is not FrameworkElement contenuto)
            return null;

        try
        {
            var inizio = contenuto.TransformToAncestor(Albero).Transform(new Point(0, 0)).X + spostamento;
            var larghezza = contenuto is Panel pannello
                ? pannello.Children.OfType<UIElement>().Sum(figlio => figlio.DesiredSize.Width)
                : contenuto.DesiredSize.Width;
            return larghezza > 0 ? inizio + larghezza : null;
        }
        catch (InvalidOperationException)
        {
            return null; // il nodo non è (più) collegato all'albero visivo
        }
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

    /// <summary>Risale dall'elemento su cui è il mouse fino al nodo dell'albero che lo contiene.</summary>
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

    /// <summary>Il trascinamento esce dall'albero: si toglie l'evidenziazione del nodo.</summary>
    private void Albero_DragLeave(object sender, DragEventArgs e) => Evidenzia(null);

    /// <summary>
    /// Qualcosa è stato rilasciato su una cartella dell'albero: i file di Esplora file si allegano alla cartella, un
    /// documento di una griglia si sposta lì.
    /// </summary>
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

    /// <summary>
    /// Evidenzia il nodo su cui si sta per rilasciare qualcosa (e toglie l'evidenziazione al precedente).
    /// </summary>
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
