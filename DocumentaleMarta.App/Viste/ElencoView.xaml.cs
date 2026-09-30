using System.Windows;
using System.Windows.Controls;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

public partial class ElencoView : UserControl
{
    public ElencoView()
    {
        InitializeComponent();

        DataContextChanged += (_, e) =>
        {
            var elenco = e.NewValue as ElencoDocumentiViewModel;

            // La colonna "Area" ha senso solo nell'elenco di tutto l'archivio: in quello di un'area sarebbe sempre uguale.
            ColonnaArea.Visibility = elenco is { MostraArea: false } ? Visibility.Collapsed : Visibility.Visible;

            // Nei risultati di una ricerca c'è la colonna "Trovato"; per fargli posto si tolgono le date, meno utili lì.
            var inRicerca = elenco is { MostraTrovato: true };
            ColonnaTrovato.Visibility = inRicerca ? Visibility.Visible : Visibility.Collapsed;
            ColonnaScadenza.Visibility = inRicerca ? Visibility.Collapsed : Visibility.Visible;
            ColonnaCaricato.Visibility = inRicerca ? Visibility.Collapsed : Visibility.Visible;
        };

        // Doppio clic su una riga: si va alla cartella del documento.
        DoppioClicGriglia.Collega<DocumentoElencoViewModel>(Griglia, riga => riga.VaiAllaCartellaCommand.Execute(null));
    }
}
