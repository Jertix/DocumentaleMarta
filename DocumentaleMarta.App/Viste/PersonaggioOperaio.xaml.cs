namespace DocumentaleMarta.App.Viste;

/// <summary>Un operaio col casco che cammina portando il martello: braccia e gambe in opposizione, passo pesante.</summary>
public partial class PersonaggioOperaio : PersonaggioBase
{
    /// <summary>Crea l'operaio, fermo nella posa iniziale.</summary>
    public PersonaggioOperaio()
    {
        InitializeComponent();
        AggiornaPosa(0);
    }

    /// <summary>Cammina con passo deciso ma non svelto.</summary>
    public override TimeSpan DurataPasso => TimeSpan.FromSeconds(0.85);

    /// <summary>Gamba avanti e braccio opposto indietro; il braccio col martello oscilla meno, per non rovesciare il martello.</summary>
    protected override void AggiornaPosa(double fase)
    {
        var onda = Onda(fase);

        GambaVicina.Angle = 24 * onda;
        GambaLontana.Angle = -24 * onda;
        BraccioVicino.Angle = -14 * onda - 8; // il martello resta sempre un po' avanti
        BraccioLontano.Angle = 20 * onda;

        Rimbalzo.Y = -1 * Math.Abs(Math.Cos(2 * Math.PI * fase));
    }
}
