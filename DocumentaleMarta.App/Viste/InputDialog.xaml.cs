using System.Windows;
using System.Windows.Controls;

namespace DocumentaleMarta.App.Viste;

/// <summary>
/// La piccola finestra che chiede un testo (per esempio il nome di una nuova area) e lo controlla mentre si scrive.
/// </summary>
public partial class InputDialog : Window
{
    private readonly Func<string, string?> _validatore;

    /// <summary>Prepara la finestra che chiede un testo: titolo, messaggio e valore di partenza già selezionato.</summary>
    public InputDialog(string titolo, string messaggio, string valoreIniziale, Func<string, string?> validatore)
    {
        InitializeComponent();
        _validatore = validatore;

        Title = titolo;
        Messaggio.Text = messaggio;
        CasellaTesto.Text = valoreIniziale;
        CasellaTesto.SelectAll();
        Aggiorna();
    }

    /// <summary>Testo confermato, senza spazi ai lati.</summary>
    public string Testo { get; private set; } = "";

    /// <summary>Ogni volta che si scrive si ricontrolla il testo.</summary>
    private void CasellaTesto_TextChanged(object sender, TextChangedEventArgs e) => Aggiorna();

    /// <summary>
    /// Controlla il testo con il validatore: attiva o spegne «OK» e mostra il motivo dell'errore (solo dopo che l'utente ha
    /// scritto qualcosa).
    /// </summary>
    private void Aggiorna()
    {
        // Il messaggio d'errore compare solo dopo che l'utente ha scritto qualcosa: un campo appena aperto non va rimproverato.
        var errore = _validatore(CasellaTesto.Text);
        PulsanteOk.IsEnabled = errore is null;
        Errore.Text = CasellaTesto.Text.Length > 0 ? errore ?? "" : "";
    }

    /// <summary>Pulsante «OK»: conferma il testo, senza spazi ai lati, e chiude la finestra.</summary>
    private void PulsanteOk_Click(object sender, RoutedEventArgs e)
    {
        Testo = CasellaTesto.Text.Trim();
        DialogResult = true;
    }
}
