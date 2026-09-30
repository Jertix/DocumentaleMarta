using System.Windows;
using System.Windows.Controls;

namespace DocumentaleMarta.App.Viste;

public partial class InputDialog : Window
{
    private readonly Func<string, string?> _validatore;

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

    private void CasellaTesto_TextChanged(object sender, TextChangedEventArgs e) => Aggiorna();

    private void Aggiorna()
    {
        // Il messaggio d'errore compare solo dopo che l'utente ha scritto qualcosa: un campo appena aperto non va rimproverato.
        var errore = _validatore(CasellaTesto.Text);
        PulsanteOk.IsEnabled = errore is null;
        Errore.Text = CasellaTesto.Text.Length > 0 ? errore ?? "" : "";
    }

    private void PulsanteOk_Click(object sender, RoutedEventArgs e)
    {
        Testo = CasellaTesto.Text.Trim();
        DialogResult = true;
    }
}
