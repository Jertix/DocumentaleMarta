using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

/// <summary>
/// La striscia in fondo all'albero: ogni tanto un personaggio animato (cagnolino, omino, gattino, uccellino, operaio) la
/// attraversa. Passa solo a finestra attiva (non ridotta a icona), mai durante una pausa di Windows delle animazioni, e sparisce
/// del tutto se l'impostazione «Animazioni» è spenta. Non prende il cursore e non disturba la tastiera.
/// </summary>
public partial class StriscaAnimata : UserControl
{
    /// <summary>La striscia è accesa (impostazione «Animazioni»). Spenta, sparisce e l'albero riprende lo spazio.</summary>
    public static readonly DependencyProperty AttivaProperty = DependencyProperty.Register(
        nameof(Attiva), typeof(bool), typeof(StriscaAnimata),
        new PropertyMetadata(true, (d, _) => ((StriscaAnimata)d).AggiornaStato()));

    /// <summary>Sotto questa larghezza la striscia è troppo stretta perché ci si possa vedere passare qualcuno.</summary>
    private const double LarghezzaMinima = 80;

    /// <summary>Dove sta il terreno, dall'alto della scena (i piedi dei personaggi poggiano qui).</summary>
    private const double QuotaTerreno = 37;

    private readonly DispatcherTimer _timer;
    private PersonaggioBase? _inCorso;
    private TranslateTransform? _spostamento;
    private Func<bool> _animazioniDiSistema = () => SystemParameters.ClientAreaAnimation;
    private Window? _finestra;
    private bool _caricata;
    private bool _guasta;

    /// <summary>Crea la striscia. Il timer parte solo quando la striscia è nella finestra (evento Loaded).</summary>
    public StriscaAnimata()
    {
        InitializeComponent();

        _timer = new DispatcherTimer(DispatcherPriority.Background);
        _timer.Tick += Timer_Tick;
        Loaded += (_, _) => Carica();
        Unloaded += (_, _) => Scarica();
    }

    /// <summary>La striscia è accesa (impostazione «Animazioni»).</summary>
    public bool Attiva
    {
        get => (bool)GetValue(AttivaProperty);
        set => SetValue(AttivaProperty, value);
    }

    /// <summary>
    /// Se in Windows le animazioni sono attive (le prove lo sostituiscono). Spente in Windows, la striscia non compare.
    /// </summary>
    public Func<bool> AnimazioniDiSistema
    {
        get => _animazioniDiSistema;
        set
        {
            _animazioniDiSistema = value;
            AggiornaStato();
        }
    }

    /// <summary>Chi decide quando passa qualcuno e chi (le prove lo sostituiscono con uno dal seme noto).</summary>
    public PianificatoreStriscia Pianificatore { get; set; } = new();

    /// <summary>Chi sta attraversando la striscia adesso; null se non c'è nessuno.</summary>
    public PersonaggioStriscia? PersonaggioInCorso { get; private set; }

    /// <summary>Il disegno di chi sta attraversando la striscia adesso; null se non c'è nessuno.</summary>
    public PersonaggioBase? Figura => _inCorso;

    /// <summary>Quanti personaggi ci sono sulla scena (0 o 1).</summary>
    public int PersonaggiPresenti => Scena.Children.Count;

    /// <summary>La striscia è solo decorazione: non compare nei programmi per ipovedenti (che leggerebbero un controllo vuoto).</summary>
    protected override AutomationPeer? OnCreateAutomationPeer() => null;

    // ---------- Vita della striscia ----------

    /// <summary>La striscia è entrata nella finestra: si ascolta la finestra e, se serve, parte l'attesa del primo passaggio.</summary>
    private void Carica()
    {
        _caricata = true;
        _finestra = Window.GetWindow(this);
        if (_finestra is not null)
        {
            _finestra.Deactivated += Finestra_Disattivata;
            _finestra.StateChanged += Finestra_StatoCambiato;
            _finestra.Activated += Finestra_Attivata;
        }
        AggiornaStato();
    }

    /// <summary>La striscia esce dalla finestra: si ferma tutto e non resta nulla di vivo.</summary>
    private void Scarica()
    {
        _caricata = false;
        if (_finestra is not null)
        {
            _finestra.Deactivated -= Finestra_Disattivata;
            _finestra.StateChanged -= Finestra_StatoCambiato;
            _finestra.Activated -= Finestra_Attivata;
            _finestra = null;
        }
        _timer.Stop();
        Interrompi();
    }

    /// <summary>
    /// Ricalcola se la striscia si vede e se deve passare qualcuno: accesa e con le animazioni di Windows attive la striscia
    /// compare e parte l'attesa; altrimenti sparisce e si ferma tutto.
    /// </summary>
    public void AggiornaStato()
    {
        var abilitata = Attiva && !_guasta && _animazioniDiSistema();
        Visibility = abilitata ? Visibility.Visible : Visibility.Collapsed;

        if (!abilitata)
        {
            _timer.Stop();
            Interrompi();
            return;
        }

        if (_caricata && !_timer.IsEnabled && _inCorso is null)
            Pianifica(primaVolta: true);
    }

    /// <summary>Tornando nella finestra si ricontrolla l'impostazione delle animazioni di Windows (può essere cambiata nel frattempo).</summary>
    private void Finestra_Attivata(object? sender, EventArgs e) => AggiornaStato();

    /// <summary>La finestra perde il cursore: chi sta passando sparisce subito, e il prossimo passaggio si aspetta di nuovo.</summary>
    private void Finestra_Disattivata(object? sender, EventArgs e) => InterrompiEAspetta();

    /// <summary>La finestra viene ridotta a icona: stesso effetto.</summary>
    private void Finestra_StatoCambiato(object? sender, EventArgs e)
    {
        if (_finestra?.WindowState == WindowState.Minimized)
            InterrompiEAspetta();
    }

    private void InterrompiEAspetta()
    {
        if (_inCorso is null)
            return;

        Interrompi();
        if (Visibility == Visibility.Visible)
            Pianifica(primaVolta: false);
    }

    // ---------- Quando passa qualcuno ----------

    /// <summary>Mette in attesa il prossimo passaggio (più breve per il primo).</summary>
    private void Pianifica(bool primaVolta) => Attendi(Pianificatore.ProssimaAttesa(primaVolta));

    private void Attendi(TimeSpan attesa)
    {
        _timer.Stop();
        _timer.Interval = attesa;
        _timer.Start();
    }

    /// <summary>
    /// È ora: se la finestra è attiva e la striscia si vede passa qualcuno, altrimenti si riprova tra poco. Un errore qui non deve
    /// mai arrivare all'utente come finestra di errore a ogni tick: la striscia si ferma e basta.
    /// </summary>
    private void Timer_Tick(object? sender, EventArgs e)
    {
        _timer.Stop();
        try
        {
            if (PuoPassare())
            {
                if (!FaiPassare())
                    Attendi(PianificatoreStriscia.NuovoTentativo);
            }
            else
            {
                Attendi(PianificatoreStriscia.NuovoTentativo);
            }
        }
        catch (Exception)
        {
            _guasta = true;
            Interrompi();
            Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>La finestra è attiva, non ridotta a icona, e la striscia è abbastanza larga.</summary>
    private bool PuoPassare() =>
        Attiva && _animazioniDiSistema() && IsVisible && ActualWidth >= LarghezzaMinima
        && _finestra is { IsActive: true, WindowState: not WindowState.Minimized };

    /// <summary>Doppio clic sulla striscia: passa qualcuno subito (senza aspettare i minuti).</summary>
    private void Striscia_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && Attiva && _animazioniDiSistema())
            FaiPassare();
    }

    // ---------- Il passaggio ----------

    /// <summary>
    /// Fa passare un personaggio da un lato all'altro (quello indicato, altrimenti uno a caso diverso dall'ultimo), a meno che
    /// non ce ne sia già uno o la striscia sia troppo stretta. Restituisce vero se è partito.
    /// </summary>
    public bool FaiPassare(PersonaggioStriscia? personaggio = null)
    {
        if (_inCorso is not null || ActualWidth < LarghezzaMinima)
            return false;

        _timer.Stop();
        var passaggio = personaggio is { } scelto ? Pianificatore.Passaggio(scelto) : Pianificatore.ProssimoPassaggio();
        var figura = Crea(passaggio.Personaggio);

        // Parte fuori da un lato e finisce fuori dall'altro; da destra a sinistra è rivolto a sinistra.
        var larghezza = ActualWidth;
        var da = passaggio.DaDestra ? larghezza : -figura.Width;
        var a = passaggio.DaDestra ? -figura.Width : larghezza;
        var spostamento = new TranslateTransform(da, 0);

        figura.RenderTransformOrigin = new Point(0.5, 0.5);
        figura.RenderTransform = new TransformGroup
        {
            Children = { new ScaleTransform(passaggio.DaDestra ? -1 : 1, 1), spostamento }
        };
        Canvas.SetTop(figura, QuotaTerreno - figura.Height - figura.AltezzaDaTerra);
        Canvas.SetLeft(figura, 0);

        var durata = PianificatoreStriscia.DurataAttraversamento(passaggio.Personaggio, larghezza, figura.Width);
        var traversata = new DoubleAnimation(da, a, durata);
        Timeline.SetDesiredFrameRate(traversata, 30);
        traversata.Completed += (_, _) =>
        {
            if (ReferenceEquals(_inCorso, figura))
                TerminaPassaggio();
        };

        _inCorso = figura;
        _spostamento = spostamento;
        PersonaggioInCorso = passaggio.Personaggio;
        Scena.Children.Add(figura);

        spostamento.BeginAnimation(TranslateTransform.XProperty, traversata);
        figura.Avvia();
        return true;
    }

    /// <summary>Il personaggio è uscito dalla scena: si toglie e si aspetta il prossimo.</summary>
    public void TerminaPassaggio()
    {
        Interrompi();
        if (_caricata && Visibility == Visibility.Visible)
            Pianifica(primaVolta: false);
    }

    /// <summary>Toglie subito il personaggio dalla scena (fermandone le animazioni), senza programmare altro.</summary>
    private void Interrompi()
    {
        if (_inCorso is null)
            return;

        _spostamento?.BeginAnimation(TranslateTransform.XProperty, null);
        _inCorso.Ferma();
        Scena.Children.Remove(_inCorso);
        _inCorso = null;
        _spostamento = null;
        PersonaggioInCorso = null;
    }

    /// <summary>Costruisce il disegno del personaggio.</summary>
    public static PersonaggioBase Crea(PersonaggioStriscia personaggio) => personaggio switch
    {
        PersonaggioStriscia.Cane => new PersonaggioCane(),
        PersonaggioStriscia.Omino => new PersonaggioOmino(),
        PersonaggioStriscia.Gatto => new PersonaggioGatto(),
        PersonaggioStriscia.Uccellino => new PersonaggioUccellino(),
        PersonaggioStriscia.Operaio => new PersonaggioOperaio(),
        _ => throw new ArgumentOutOfRangeException(nameof(personaggio))
    };
}
