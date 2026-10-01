using System.Windows;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

public partial class ImpostazioniDialog : Window
{
    private readonly ImpostazioniViewModel _modello;

    public ImpostazioniDialog(ImpostazioniViewModel modello)
    {
        InitializeComponent();
        _modello = modello;
        DataContext = modello;
    }

    private void Salva_Click(object sender, RoutedEventArgs e)
    {
        if (_modello.PuoSalvare)
            DialogResult = true;
    }
}
