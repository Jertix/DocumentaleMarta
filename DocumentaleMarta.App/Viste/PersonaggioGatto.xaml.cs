namespace DocumentaleMarta.App.Viste;

/// <summary>Un gattino che cammina: passo felpato (zampe in diagonale, piccole oscillazioni) e coda che ondeggia piano.</summary>
public partial class PersonaggioGatto : PersonaggioBase
{
    /// <summary>Crea il gattino, fermo nella posa iniziale.</summary>
    public PersonaggioGatto()
    {
        InitializeComponent();
        AggiornaPosa(0);
    }

    /// <summary>Un passo completo dura più di mezzo secondo: cammina senza fretta.</summary>
    public override TimeSpan DurataPasso => TimeSpan.FromSeconds(0.6);

    /// <summary>Zampe in diagonale con piccola ampiezza, corpo che dondola appena, coda che si muove più lenta del passo.</summary>
    protected override void AggiornaPosa(double fase)
    {
        var onda = Onda(fase);

        ZampaAnterioreVicina.Angle = 24 * onda;
        ZampaPosterioreLontana.Angle = 24 * onda;
        ZampaAnterioreLontana.Angle = -24 * onda;
        ZampaPosterioreVicina.Angle = -24 * onda;

        Rimbalzo.Y = -0.7 * Math.Abs(Math.Cos(2 * Math.PI * fase));
        Coda.Angle = 12 * Onda(fase, 1, 0.25);
    }
}
