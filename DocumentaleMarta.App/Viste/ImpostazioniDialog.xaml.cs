using System.IO;
using System.Windows;
using DocumentaleMarta.App.ViewModels;
using Microsoft.Win32;

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

    private void ScegliCartellaBackup_Click(object sender, RoutedEventArgs e)
    {
        var dialogo = new OpenFolderDialog { Title = "Cartella in cui salvare i backup", Multiselect = false };
        if (Directory.Exists(_modello.CartellaBackup))
            dialogo.InitialDirectory = _modello.CartellaBackup;
        if (dialogo.ShowDialog(this) == true)
            _modello.CartellaBackup = dialogo.FolderName;
    }

    private void Salva_Click(object sender, RoutedEventArgs e)
    {
        if (_modello.PuoSalvare)
            DialogResult = true;
    }
}
