using System.Windows;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using Microsoft.Win32;

namespace DocumentaleMarta.App.Servizi;

public class DialogService : IDialogService
{
    private const string TitoloApplicazione = "Documentale";

    private const string FiltroFile =
        "Documenti e immagini|*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.odt;*.ods;*.rtf;*.txt;*.csv;*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.gif" +
        "|Tutti i file|*.*";

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

    public IReadOnlyList<string> SelezionaFile(string titolo)
    {
        var dialogo = new OpenFileDialog
        {
            Title = titolo,
            Multiselect = true,
            CheckFileExists = true,
            Filter = FiltroFile
        };
        var confermato = Proprietaria is { } finestra ? dialogo.ShowDialog(finestra) : dialogo.ShowDialog();
        return confermato == true ? dialogo.FileNames : [];
    }

    public bool MostraNuovaCartella(NuovaCartellaViewModel modello) =>
        new NuovaCartellaDialog(modello) { Owner = Proprietaria }.ShowDialog() == true;

    public bool MostraImpostazioni(ImpostazioniViewModel modello) =>
        new ImpostazioniDialog(modello) { Owner = Proprietaria }.ShowDialog() == true;

    public void MostraInformazioni(InformazioniViewModel modello) =>
        new InformazioniDialog(modello) { Owner = Proprietaria }.ShowDialog();

    private static MessageBoxResult Mostra(string messaggio, string titolo, MessageBoxButton pulsanti, MessageBoxImage icona, MessageBoxResult predefinito) =>
        Proprietaria is { } finestra
            ? MessageBox.Show(finestra, messaggio, titolo, pulsanti, icona, predefinito)
            : MessageBox.Show(messaggio, titolo, pulsanti, icona, predefinito);
}
