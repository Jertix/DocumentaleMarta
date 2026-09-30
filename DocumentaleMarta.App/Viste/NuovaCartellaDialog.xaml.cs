using System.Windows;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

public partial class NuovaCartellaDialog : Window
{
    private readonly NuovaCartellaViewModel _modello;

    public NuovaCartellaDialog(NuovaCartellaViewModel modello)
    {
        InitializeComponent();
        _modello = modello;
        DataContext = modello;
    }

    private void Crea_Click(object sender, RoutedEventArgs e)
    {
        if (_modello.Convalida())
            DialogResult = true;
    }
}
