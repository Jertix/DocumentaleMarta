using System.Windows;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

public partial class DuplicatiDialog : Window
{
    public DuplicatiDialog(DuplicatiViewModel modello)
    {
        InitializeComponent();
        DataContext = modello;

        // Invio sceglie l'opzione più prudente: non allegare doppioni.
        if (modello.PuoSaltare)
            PulsanteSalta.IsDefault = true;
        else
            PulsanteRifiuto.IsDefault = true;
    }

    /// <summary>Ciò che l'utente ha scelto; chiudendo la finestra con la X o con Esc vale "Annulla".</summary>
    public SceltaDuplicati Scelta { get; private set; } = SceltaDuplicati.Annulla;

    private void AllegaComunque_Click(object sender, RoutedEventArgs e)
    {
        Scelta = SceltaDuplicati.AllegaComunque;
        DialogResult = true;
    }

    private void Salta_Click(object sender, RoutedEventArgs e)
    {
        Scelta = SceltaDuplicati.SaltaDuplicati;
        DialogResult = true;
    }
}
