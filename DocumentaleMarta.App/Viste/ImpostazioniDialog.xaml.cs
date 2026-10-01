using System.IO;
using System.Windows;
using DocumentaleMarta.App.ViewModels;
using Microsoft.Win32;

namespace DocumentaleMarta.App.Viste;

/// <summary>La finestra delle Impostazioni: aspetto, avvisi di scadenza, dati della ditta, backup e archivio.</summary>
public partial class ImpostazioniDialog : Window
{
    private readonly ImpostazioniViewModel _modello;

    /// <summary>Crea la finestra delle Impostazioni collegata ai suoi valori.</summary>
    public ImpostazioniDialog(ImpostazioniViewModel modello)
    {
        InitializeComponent();
        _modello = modello;
        DataContext = modello;
    }

    /// <summary>Pulsante «Scegli…»: apre la scelta della cartella dei backup e ne scrive il percorso nel campo.</summary>
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

    /// <summary>Pulsante «Ripristina da un backup…»: salva le impostazioni e poi parte il ripristino.</summary>
    private void Ripristina_Click(object sender, RoutedEventArgs e) => ChiudiConAzione(AzioneDaImpostazioni.Ripristino);

    /// <summary>
    /// Chiude la finestra confermando e ricorda quale operazione (backup o ripristino) eseguire dopo il salvataggio; non fa
    /// nulla se i valori non sono validi.
    /// </summary>
    private void ChiudiConAzione(AzioneDaImpostazioni azione)
    {
        if (!_modello.PuoSalvare)
            return;

        _modello.AzioneRichiesta = azione;
        DialogResult = true;
    }

    /// <summary>Pulsante «Salva»: chiude la finestra confermando, se i valori sono validi.</summary>
    private void Salva_Click(object sender, RoutedEventArgs e)
    {
        if (_modello.PuoSalvare)
            DialogResult = true;
    }
}
