using System.Windows;
using System.Windows.Controls;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

public partial class CartellaView : UserControl
{
    public CartellaView()
    {
        InitializeComponent();

        // Le righe dei documenti si possono trascinare su un'altra cartella dell'albero.
        AvvioTrascinamento.Collega(Griglia);
    }

    /// <summary>Mentre si trascinano dei file sopra il form: se si possono allegare compare il riquadro "Rilascia qui".</summary>
    private void Cartella_DragOver(object sender, DragEventArgs e)
    {
        var accettabile = DatiTrascinati.HaFile(e.Data) && DataContext is CartellaFormViewModel;
        e.Effects = accettabile ? DragDropEffects.Copy : DragDropEffects.None;
        SuggerimentoRilascio.Visibility = accettabile ? Visibility.Visible : Visibility.Collapsed;
        e.Handled = true;
    }

    private void Cartella_DragLeave(object sender, DragEventArgs e) =>
        SuggerimentoRilascio.Visibility = Visibility.Collapsed;

    /// <summary>File rilasciati sul form: si allegano come con il pulsante "Allega".</summary>
    private void Cartella_Drop(object sender, DragEventArgs e)
    {
        SuggerimentoRilascio.Visibility = Visibility.Collapsed;
        if (DataContext is CartellaFormViewModel form && DatiTrascinati.HaFile(e.Data))
            form.AllegaCommand.Execute(DatiTrascinati.File(e.Data));
        e.Handled = true;
    }
}
