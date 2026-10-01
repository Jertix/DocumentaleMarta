using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

/// <summary>
/// La pagina del documento in una finestra grande, disegnata a una risoluzione più alta di quella del pannello: si parte dalla
/// pagina intera, si ingrandisce con + e − (o Ctrl e rotellina, o doppio clic) e ci si sposta trascinando.
/// </summary>
public partial class AnteprimaIngranditaDialog : Window
{
    private readonly AnteprimaViewModel _modello;
    private double _zoom = Ingrandimento.PaginaIntera;

    // Trascinamento della pagina per spostarsi quando è ingrandita.
    private Point? _inizioTrascinamento;
    private double _offsetOrizzontaleIniziale;
    private double _offsetVerticaleIniziale;

    public AnteprimaIngranditaDialog(AnteprimaViewModel modello)
    {
        InitializeComponent();
        _modello = modello;
        DataContext = modello;

        // Si apre grande ma dentro lo schermo: gran parte dell'area di lavoro.
        var area = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, Math.Min(area.Width * 0.85, 1500));
        Height = Math.Max(MinHeight, area.Height * 0.9);

        modello.PropertyChanged += ModelloCambiato;
        Closed += (_, _) =>
        {
            modello.PropertyChanged -= ModelloCambiato;
            modello.Visibile = false; // se si sta ancora disegnando, si smette
        };

        AggiornaTitolo();
        AggiornaStatoZoom();
    }

    /// <summary>Lo zoom attuale (1 = pagina intera).</summary>
    public double Zoom => _zoom;

    private void ModelloCambiato(object? mittente, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AnteprimaViewModel.Titolo))
            AggiornaTitolo();
    }

    /// <summary>
    /// È arrivata un'immagine (la prima, o la pagina successiva): si porta alla dimensione dello zoom attuale e si mostra dall'inizio.
    /// Si parte da qui e non dal cambio di proprietà del modello perché il collegamento dell'immagine avviene durante il primo disegno.
    /// </summary>
    private void Immagine_TargetUpdated(object? sender, System.Windows.Data.DataTransferEventArgs e)
    {
        AggiornaDimensioni();
        Scorrimento.ScrollToHome();
    }

    private void AggiornaTitolo() =>
        Title = string.IsNullOrWhiteSpace(_modello.Titolo) ? "Anteprima ingrandita" : $"{_modello.Titolo}  —  anteprima ingrandita";

    // ---------- Dimensioni e zoom ----------

    private void Scorrimento_SizeChanged(object sender, SizeChangedEventArgs e) => AggiornaDimensioni();

    /// <summary>Rimette l'immagine alla dimensione che spetta: la pagina intera nella finestra, per lo zoom scelto.</summary>
    private void AggiornaDimensioni()
    {
        if (Immagine.Source is not { } sorgente)
            return;

        var dimensioni = Ingrandimento.Dimensioni(
            new Size(sorgente.Width, sorgente.Height), new Size(Scorrimento.ActualWidth, Scorrimento.ActualHeight), _zoom);
        if (dimensioni.IsEmpty)
            return;

        Immagine.Width = dimensioni.Width;
        Immagine.Height = dimensioni.Height;
    }

    private void ImpostaZoom(double nuovo)
    {
        nuovo = Ingrandimento.Limita(nuovo);
        if (nuovo == _zoom)
            return;

        // Si ingrandisce restando sul punto che si sta guardando (il centro dell'area visibile).
        var centroX = (Scorrimento.HorizontalOffset + Scorrimento.ViewportWidth / 2) / Math.Max(1, Scorrimento.ExtentWidth);
        var centroY = (Scorrimento.VerticalOffset + Scorrimento.ViewportHeight / 2) / Math.Max(1, Scorrimento.ExtentHeight);

        _zoom = nuovo;
        AggiornaDimensioni();
        Scorrimento.UpdateLayout();
        Scorrimento.ScrollToHorizontalOffset(centroX * Scorrimento.ExtentWidth - Scorrimento.ViewportWidth / 2);
        Scorrimento.ScrollToVerticalOffset(centroY * Scorrimento.ExtentHeight - Scorrimento.ViewportHeight / 2);
        AggiornaStatoZoom();
    }

    private void AggiornaStatoZoom()
    {
        TestoZoom.Text = Ingrandimento.Testo(_zoom);
        PulsanteMeno.IsEnabled = _zoom > Ingrandimento.PaginaIntera;
        PulsantePiu.IsEnabled = _zoom < Ingrandimento.Massimo;
        PulsanteIntera.IsEnabled = _zoom > Ingrandimento.PaginaIntera;
        Pagina.Cursor = _zoom > Ingrandimento.PaginaIntera ? Cursors.Hand : Cursors.Arrow;
    }

    private void Piu_Click(object sender, RoutedEventArgs e) => ImpostaZoom(Ingrandimento.Aumenta(_zoom));

    private void Meno_Click(object sender, RoutedEventArgs e) => ImpostaZoom(Ingrandimento.Diminuisci(_zoom));

    private void PaginaIntera_Click(object sender, RoutedEventArgs e) => ImpostaZoom(Ingrandimento.PaginaIntera);

    // ---------- Mouse ----------

    /// <summary>Doppio clic sulla pagina: ingrandisce o torna alla pagina intera. Un clic solo, a pagina ingrandita: inizia il trascinamento.</summary>
    private void Pagina_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ImpostaZoom(Ingrandimento.AlternaDoppioClic(_zoom));
            e.Handled = true;
            return;
        }

        if (_zoom > Ingrandimento.PaginaIntera)
        {
            _inizioTrascinamento = e.GetPosition(Scorrimento);
            _offsetOrizzontaleIniziale = Scorrimento.HorizontalOffset;
            _offsetVerticaleIniziale = Scorrimento.VerticalOffset;
            Pagina.CaptureMouse();
            Pagina.Cursor = Cursors.SizeAll;
            e.Handled = true;
        }
    }

    private void Pagina_MouseMove(object sender, MouseEventArgs e)
    {
        if (_inizioTrascinamento is not { } inizio || e.LeftButton != MouseButtonState.Pressed)
            return;

        var posizione = e.GetPosition(Scorrimento);
        Scorrimento.ScrollToHorizontalOffset(_offsetOrizzontaleIniziale - (posizione.X - inizio.X));
        Scorrimento.ScrollToVerticalOffset(_offsetVerticaleIniziale - (posizione.Y - inizio.Y));
    }

    private void Pagina_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => FineTrascinamento();

    private void FineTrascinamento()
    {
        if (_inizioTrascinamento is null)
            return;

        _inizioTrascinamento = null;
        Pagina.ReleaseMouseCapture();
        AggiornaStatoZoom(); // rimette il cursore
    }

    private void Finestra_PreviewMouseWheel(object sender, MouseWheelEventArgs e) =>
        e.Handled = GestisciRotellina(e.Delta, Keyboard.Modifiers);

    /// <summary>Ctrl e rotellina ingrandiscono e rimpiccioliscono; la rotellina da sola scorre la pagina. Vero se ha gestito il movimento.</summary>
    public bool GestisciRotellina(int delta, ModifierKeys modificatori)
    {
        if (modificatori != ModifierKeys.Control)
            return false;

        ImpostaZoom(delta > 0 ? Ingrandimento.Aumenta(_zoom) : Ingrandimento.Diminuisci(_zoom));
        return true;
    }

    // ---------- Tastiera ----------

    private void Finestra_PreviewKeyDown(object sender, KeyEventArgs e) =>
        e.Handled = GestisciTasto(e.Key, Keyboard.Modifiers);

    /// <summary>Esc chiude, le frecce sfogliano le pagine (a pagina intera), + e − cambiano lo zoom, Ctrl+0 torna alla pagina intera. Vero se il tasto è gestito.</summary>
    public bool GestisciTasto(Key tasto, ModifierKeys modificatori)
    {
        var ctrl = modificatori == ModifierKeys.Control;
        var nessuno = modificatori == ModifierKeys.None;
        var paginaIntera = _zoom <= Ingrandimento.PaginaIntera;

        switch (tasto)
        {
            case Key.Escape when nessuno:
                Close();
                break;

            // Le pagine si sfogliano con le frecce solo quando la pagina è intera: ingrandita, le frecce fanno scorrere.
            case Key.Left or Key.PageUp when nessuno && paginaIntera && _modello.PaginaPrecedenteCommand.CanExecute(null):
                _modello.PaginaPrecedenteCommand.Execute(null);
                break;
            case Key.Right or Key.PageDown when nessuno && paginaIntera && _modello.PaginaSuccessivaCommand.CanExecute(null):
                _modello.PaginaSuccessivaCommand.Execute(null);
                break;

            case Key.Add or Key.OemPlus when nessuno || ctrl:
                ImpostaZoom(Ingrandimento.Aumenta(_zoom));
                break;
            case Key.Subtract or Key.OemMinus when nessuno || ctrl:
                ImpostaZoom(Ingrandimento.Diminuisci(_zoom));
                break;
            case Key.D0 or Key.NumPad0 when ctrl:
                ImpostaZoom(Ingrandimento.PaginaIntera);
                break;

            default:
                return false;
        }

        return true;
    }
}
