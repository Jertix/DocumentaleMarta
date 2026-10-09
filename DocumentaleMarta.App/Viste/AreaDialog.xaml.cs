using System.Windows;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

/// <summary>
/// La finestra con la griglia delle icone di un'area: per una nuova area sopra c'è anche il nome, per cambiare l'icona di
/// un'area che c'è già c'è solo la griglia.
/// </summary>
public partial class AreaDialog : Window
{
    private readonly AreaDialogViewModel _modello;

    /// <summary>Crea la finestra collegata ai suoi dati.</summary>
    public AreaDialog(AreaDialogViewModel modello)
    {
        InitializeComponent();
        _modello = modello;
        DataContext = modello;
    }

    /// <summary>All'apertura il cursore va sul nome (nuova area) oppure sulla griglia delle icone.</summary>
    private void Finestra_Loaded(object sender, RoutedEventArgs e)
    {
        if (_modello.ChiedeNome)
        {
            CasellaNome.Focus();
            CasellaNome.SelectAll();
        }
        else
        {
            ElencoIcone.Focus();
        }
    }

    /// <summary>Pulsante «OK»: conferma (il pulsante è attivo solo se il nome è valido) e chiude la finestra.</summary>
    private void PulsanteOk_Click(object sender, RoutedEventArgs e)
    {
        if (_modello.PuoConfermare)
            DialogResult = true;
    }
}
