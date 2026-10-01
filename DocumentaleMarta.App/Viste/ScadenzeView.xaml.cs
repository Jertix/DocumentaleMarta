using System.Windows.Controls;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

/// <summary>L'elenco delle cartelle in scadenza o scadute, dalla più urgente.</summary>
public partial class ScadenzeView : UserControl
{
    /// <summary>Crea l'elenco delle scadenze e collega il doppio clic su una riga che porta alla cartella.</summary>
    public ScadenzeView()
    {
        InitializeComponent();

        // Doppio clic su una riga: si va alla cartella.
        DoppioClicGriglia.Collega<ScadenzaViewModel>(Griglia, riga => riga.VaiAllaCartellaCommand.Execute(null));
    }
}
