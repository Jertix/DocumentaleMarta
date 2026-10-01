using System.Windows;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

/// <summary>
/// La finestra «Informazioni»: versione, dati della ditta, dove sono archivio, database e impostazioni, stato del
/// riconoscimento del testo.
/// </summary>
public partial class InformazioniDialog : Window
{
    /// <summary>Crea la finestra «Informazioni» con i dati del programma e della ditta.</summary>
    public InformazioniDialog(InformazioniViewModel modello)
    {
        InitializeComponent();
        DataContext = modello;
    }
}
