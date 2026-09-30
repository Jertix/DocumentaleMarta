using System.Windows;
using DocumentaleMarta.App.Viste;

namespace DocumentaleMarta.App.Servizi;

public class DialogService : IDialogService
{
    private const string TitoloApplicazione = "Documentale";

    private static Window? Proprietaria => Application.Current?.MainWindow;

    public string? ChiediTesto(string titolo, string messaggio, string valoreIniziale, Func<string, string?> validatore)
    {
        var dialogo = new InputDialog(titolo, messaggio, valoreIniziale, validatore) { Owner = Proprietaria };
        return dialogo.ShowDialog() == true ? dialogo.Testo : null;
    }

    public bool Conferma(string titolo, string messaggio) =>
        Mostra(messaggio, titolo, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    public void MostraErrore(string messaggio) =>
        Mostra(messaggio, TitoloApplicazione, MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK);

    private static MessageBoxResult Mostra(string messaggio, string titolo, MessageBoxButton pulsanti, MessageBoxImage icona, MessageBoxResult predefinito) =>
        Proprietaria is { } finestra
            ? MessageBox.Show(finestra, messaggio, titolo, pulsanti, icona, predefinito)
            : MessageBox.Show(messaggio, titolo, pulsanti, icona, predefinito);
}
