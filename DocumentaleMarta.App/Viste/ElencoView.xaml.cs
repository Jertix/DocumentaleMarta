using System.Windows;
using System.Windows.Controls;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

public partial class ElencoView : UserControl
{
    public ElencoView()
    {
        InitializeComponent();

        // La colonna "Area" ha senso solo nell'elenco di tutto l'archivio: in quello di un'area sarebbe sempre uguale.
        DataContextChanged += (_, e) =>
            ColonnaArea.Visibility = e.NewValue is ElencoDocumentiViewModel { MostraArea: false }
                ? Visibility.Collapsed
                : Visibility.Visible;

        // Doppio clic su una riga: si va alla cartella del documento.
        DoppioClicGriglia.Collega<DocumentoElencoViewModel>(Griglia, riga => riga.VaiAllaCartellaCommand.Execute(null));
    }
}
