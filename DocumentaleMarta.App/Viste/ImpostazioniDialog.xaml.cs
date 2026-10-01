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

    /// <summary>Salva e poi fa il backup: la finestra si chiude confermando e chi l'ha aperta esegue l'operazione.</summary>
    private void FaiBackup_Click(object sender, RoutedEventArgs e) => ChiudiConAzione(AzioneDaImpostazioni.Backup);

    private void Ripristina_Click(object sender, RoutedEventArgs e) => ChiudiConAzione(AzioneDaImpostazioni.Ripristino);

    private void ChiudiConAzione(AzioneDaImpostazioni azione)
    {
        if (!_modello.PuoSalvare)
            return;

        _modello.AzioneRichiesta = azione;
        DialogResult = true;
    }

    private void Salva_Click(object sender, RoutedEventArgs e)
    {
        if (_modello.PuoSalvare)
            DialogResult = true;
    }
}
