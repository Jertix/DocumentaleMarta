namespace DocumentaleMarta.App.Viste;

/// <summary>Un omino che passeggia: braccia e gambe oscillano in opposizione e il corpo sale e scende a ogni passo.</summary>
public partial class PersonaggioOmino : PersonaggioBase
{
    /// <summary>Crea l'omino, fermo nella posa iniziale.</summary>
    public PersonaggioOmino()
    {
        InitializeComponent();
        AggiornaPosa(0);
    }

    /// <summary>Un passo doppio (sinistra e destra) dura tre quarti di secondo circa: passeggia.</summary>
    public override TimeSpan DurataPasso => TimeSpan.FromSeconds(0.8);

    /// <summary>Gamba avanti con il braccio opposto indietro, come si cammina davvero.</summary>
    protected override void AggiornaPosa(double fase)
    {
        var onda = Onda(fase);

        GambaVicina.Angle = 26 * onda;
        GambaLontana.Angle = -26 * onda;
        BraccioVicino.Angle = -22 * onda;
        BraccioLontano.Angle = 22 * onda;

        Rimbalzo.Y = -0.9 * Math.Abs(Math.Cos(2 * Math.PI * fase));
    }
}
