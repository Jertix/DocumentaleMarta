namespace DocumentaleMarta.App.Viste;

/// <summary>Un cagnolino che corre: le zampe oscillano a coppie in diagonale, il corpo rimbalza e la coda scodinzola.</summary>
public partial class PersonaggioCane : PersonaggioBase
{
    /// <summary>Crea il cagnolino, fermo nella posa iniziale.</summary>
    public PersonaggioCane()
    {
        InitializeComponent();
        AggiornaPosa(0);
    }

    /// <summary>Una falcata completa dura un terzo di secondo circa: corre.</summary>
    public override TimeSpan DurataPasso => TimeSpan.FromSeconds(0.34);

    /// <summary>Zampe in diagonale (anteriore vicina con posteriore lontana), corpo che rimbalza due volte per falcata.</summary>
    protected override void AggiornaPosa(double fase)
    {
        var onda = Onda(fase);

        ZampaAnterioreVicina.Angle = 38 * onda;
        ZampaPosterioreLontana.Angle = 38 * onda;
        ZampaAnterioreLontana.Angle = -38 * onda;
        ZampaPosterioreVicina.Angle = -38 * onda;

        Rimbalzo.Y = -1.5 * Math.Abs(Math.Cos(2 * Math.PI * fase));
        Inclinazione.Angle = 3 * onda;
        Coda.Angle = 6 + 18 * Onda(fase, 2);
    }
}
