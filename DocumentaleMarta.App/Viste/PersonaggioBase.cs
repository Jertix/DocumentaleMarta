using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace DocumentaleMarta.App.Viste;

/// <summary>
/// La base dei personaggi della striscia animata: un disegno vettoriale a pezzi che si muove con una sola animazione, quella della
/// <see cref="Fase"/> (da 0 a 1 a ogni passo, poi ricomincia). Le pose di zampe, braccia, ali e coda si calcolano dalla fase nei
/// personaggi: così una posa si può anche impostare a mano (per le prove e per le immagini di controllo) senza orologi.
/// </summary>
public class PersonaggioBase : UserControl
{
    /// <summary>
    /// Il punto del passo in cui si trova il personaggio: 0 è l'inizio, 1 la fine (e di nuovo l'inizio). Cambiandola si
    /// ricalcola la posa.
    /// </summary>
    public static readonly DependencyProperty FaseProperty = DependencyProperty.Register(
        nameof(Fase), typeof(double), typeof(PersonaggioBase),
        new PropertyMetadata(0.0, (d, e) => ((PersonaggioBase)d).AggiornaPosa((double)e.NewValue)));

    /// <summary>Il punto del passo, da 0 a 1; a ogni valore corrisponde una posa.</summary>
    public double Fase
    {
        get => (double)GetValue(FaseProperty);
        set => SetValue(FaseProperty, value);
    }

    /// <summary>Quanto dura un passo completo (zampe, ali…): più è breve più il personaggio sembra svelto.</summary>
    public virtual TimeSpan DurataPasso => TimeSpan.FromSeconds(0.6);

    /// <summary>Di quanto sta sopra il terreno (0 per chi cammina, di più per chi vola), in pixel.</summary>
    public virtual double AltezzaDaTerra => 0;

    /// <summary>Un disegno decorativo: non compare nei programmi per ipovedenti.</summary>
    protected override AutomationPeer? OnCreateAutomationPeer() => null;

    /// <summary>Ricalcola la posa del personaggio per la fase indicata (da 0 a 1). I personaggi lo ridefiniscono.</summary>
    protected virtual void AggiornaPosa(double fase)
    {
    }

    /// <summary>Fa partire il movimento: la fase gira da 0 a 1 ripetutamente (a 30 fotogrammi al secondo, basta e avanza).</summary>
    public void Avvia()
    {
        var passo = new DoubleAnimation(0, 1, DurataPasso) { RepeatBehavior = RepeatBehavior.Forever };
        Timeline.SetDesiredFrameRate(passo, 30);
        BeginAnimation(FaseProperty, passo);
    }

    /// <summary>Ferma il movimento: il personaggio resta nella posa in cui si trova.</summary>
    public void Ferma() => BeginAnimation(FaseProperty, null);

    /// <summary>Un'onda tra -1 e 1 che fa <paramref name="volte"/> oscillazioni complete in un passo (0 → 0, 1/4 → 1, 3/4 → -1).</summary>
    protected static double Onda(double fase, double volte = 1, double sfasamento = 0) =>
        Math.Sin(2 * Math.PI * (fase * volte + sfasamento));
}
