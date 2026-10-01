using System.Windows;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

public partial class InformazioniDialog : Window
{
    public InformazioniDialog(InformazioniViewModel modello)
    {
        InitializeComponent();
        DataContext = modello;
    }
}
