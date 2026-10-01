using System.Windows;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

/// <summary>
/// La finestra «Nuova cartella»: titolo, descrizione, scadenza, ricorrenza e documenti da allegare (anche trascinati).
/// </summary>
public partial class NuovaCartellaDialog : Window
{
    private readonly NuovaCartellaViewModel _modello;

    /// <summary>Crea la finestra «Nuova cartella» collegata ai suoi dati.</summary>
    public NuovaCartellaDialog(NuovaCartellaViewModel modello)
    {
        InitializeComponent();
        _modello = modello;
        DataContext = modello;
    }

    /// <summary>File trascinati da Esplora file sulla finestra: si aggiungono all'elenco da allegare.</summary>
    private void Finestra_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DatiTrascinati.HaFile(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>File rilasciati sulla finestra: si aggiungono all'elenco da allegare.</summary>
    private void Finestra_Drop(object sender, DragEventArgs e)
    {
        if (DatiTrascinati.HaFile(e.Data))
            _modello.AggiungiAllegati(DatiTrascinati.File(e.Data));
        e.Handled = true;
    }

    /// <summary>Pulsante «Crea»: conferma e chiude la finestra solo se i dati sono validi.</summary>
    private void Crea_Click(object sender, RoutedEventArgs e)
    {
        if (_modello.Convalida())
            DialogResult = true;
    }
}
