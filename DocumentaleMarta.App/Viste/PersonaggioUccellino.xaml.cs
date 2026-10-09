namespace DocumentaleMarta.App.Viste;

/// <summary>Un uccellino che vola: batte l'ala e il corpo sale e scende a ogni battito.</summary>
public partial class PersonaggioUccellino : PersonaggioBase
{
    /// <summary>Crea l'uccellino, fermo con l'ala alzata.</summary>
    public PersonaggioUccellino()
    {
        InitializeComponent();
        AggiornaPosa(0);
    }

    /// <summary>Un battito d'ala completo dura meno di un terzo di secondo.</summary>
    public override TimeSpan DurataPasso => TimeSpan.FromSeconds(0.3);

    /// <summary>Vola più in alto di chi cammina: la striscia lo mette qualche pixel sopra il terreno.</summary>
    public override double AltezzaDaTerra => 7;

    /// <summary>L'ala va da alzata (scala 1) ad abbassata (scala -0,7) e ritorna; il corpo si solleva quando l'ala spinge.</summary>
    protected override void AggiornaPosa(double fase)
    {
        var battito = Math.Cos(2 * Math.PI * fase); // 1 = ala alzata, -1 = ala abbassata
        Ala.ScaleY = battito >= 0 ? battito : 0.7 * battito;
        Rimbalzo.Y = -1.5 * Math.Sin(2 * Math.PI * fase);
    }
}
