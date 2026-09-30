using System.Windows.Controls;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

public partial class ScadenzeView : UserControl
{
    public ScadenzeView()
    {
        InitializeComponent();

        // Doppio clic su una riga: si va alla cartella.
        DoppioClicGriglia.Collega<ScadenzaViewModel>(Griglia, riga => riga.VaiAllaCartellaCommand.Execute(null));
    }
}
