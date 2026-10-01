using System.Windows;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

/// <summary>
/// La finestra «File già presenti nell'archivio»: dice quali file scelti ci sono già e chiede se allegarli comunque o
/// saltarli.
/// </summary>
public partial class DuplicatiDialog : Window
{
    /// <summary>
    /// Prepara la finestra dei duplicati: sceglie come pulsante di invio l'opzione più prudente (non allegare i doppioni).
    /// </summary>
    public DuplicatiDialog(DuplicatiViewModel modello)
    {
        InitializeComponent();
        DataContext = modello;

        // Invio sceglie l'opzione più prudente: non allegare doppioni.
        if (modello.PuoSaltare)
            PulsanteSalta.IsDefault = true;
        else
            PulsanteRifiuto.IsDefault = true;
    }

    /// <summary>Ciò che l'utente ha scelto; chiudendo la finestra con la X o con Esc vale "Annulla".</summary>
    public SceltaDuplicati Scelta { get; private set; } = SceltaDuplicati.Annulla;

    /// <summary>Pulsante «Allega comunque»: chiude la finestra scegliendo di allegare anche i duplicati.</summary>
    private void AllegaComunque_Click(object sender, RoutedEventArgs e)
    {
        Scelta = SceltaDuplicati.AllegaComunque;
        DialogResult = true;
    }

    /// <summary>Pulsante «Salta i duplicati»: chiude la finestra scegliendo di allegare solo i file nuovi.</summary>
    private void Salta_Click(object sender, RoutedEventArgs e)
    {
        Scelta = SceltaDuplicati.SaltaDuplicati;
        DialogResult = true;
    }
}
