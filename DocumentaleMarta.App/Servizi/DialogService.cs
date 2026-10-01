using System.IO;
using System.Windows;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Modelli;
using Microsoft.Win32;

namespace DocumentaleMarta.App.Servizi;

public class DialogService : IDialogService
{
    private const string TitoloApplicazione = "Documentale";

    private const string FiltroFile =
        "Documenti e immagini|*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.odt;*.ods;*.odp;*.odg;*.ott;*.ots;*.otp;*.otg;*.rtf;*.txt;*.csv;*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.gif" +
        "|Tutti i file|*.*";

    private static Window? Proprietaria => Application.Current?.MainWindow;

    public string? ChiediTesto(string titolo, string messaggio, string valoreIniziale, Func<string, string?> validatore)
    {
        var dialogo = new InputDialog(titolo, messaggio, valoreIniziale, validatore) { Owner = Proprietaria };
        return dialogo.ShowDialog() == true ? dialogo.Testo : null;
    }

    public bool Conferma(string titolo, string messaggio) =>
        Mostra(messaggio, titolo, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    public bool Chiedi(string titolo, string messaggio) =>
        Mostra(messaggio, titolo, MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes) == MessageBoxResult.Yes;

    public void MostraErrore(string messaggio) =>
        Mostra(messaggio, TitoloApplicazione, MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK);

    public void MostraMessaggio(string titolo, string messaggio) =>
        Mostra(messaggio, titolo, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);

    public string? SelezionaCartella(string titolo, string? percorsoIniziale)
    {
        var dialogo = new OpenFolderDialog { Title = titolo, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(percorsoIniziale) && Directory.Exists(percorsoIniziale))
            dialogo.InitialDirectory = percorsoIniziale;
        var confermato = Proprietaria is { } finestra ? dialogo.ShowDialog(finestra) : dialogo.ShowDialog();
        return confermato == true ? dialogo.FolderName : null;
    }

    public string? SelezionaFileBackup(string? cartellaIniziale)
    {
        var dialogo = new OpenFileDialog
        {
            Title = "Scegli il backup da ripristinare",
            CheckFileExists = true,
            Multiselect = false,
            Filter = "Backup di Documentale (*.zip)|*.zip|Tutti i file|*.*"
        };
        if (!string.IsNullOrWhiteSpace(cartellaIniziale) && Directory.Exists(cartellaIniziale))
            dialogo.InitialDirectory = cartellaIniziale;
        var confermato = Proprietaria is { } finestra ? dialogo.ShowDialog(finestra) : dialogo.ShowDialog();
        return confermato == true ? dialogo.FileName : null;
    }

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

    public SceltaDuplicati ChiediDuplicati(IReadOnlyList<DuplicatoTrovato> duplicati, int totaleFile)
    {
        var dialogo = new DuplicatiDialog(new DuplicatiViewModel(duplicati, totaleFile)) { Owner = Proprietaria };
        return dialogo.ShowDialog() == true ? dialogo.Scelta : SceltaDuplicati.Annulla;
    }

    public bool MostraNuovaCartella(NuovaCartellaViewModel modello) =>
        new NuovaCartellaDialog(modello) { Owner = Proprietaria }.ShowDialog() == true;

    public bool MostraImpostazioni(ImpostazioniViewModel modello) =>
        new ImpostazioniDialog(modello) { Owner = Proprietaria }.ShowDialog() == true;

    public void MostraInformazioni(InformazioniViewModel modello) =>
        new InformazioniDialog(modello) { Owner = Proprietaria }.ShowDialog();

    public void MostraAnteprimaIngrandita(AnteprimaViewModel modello) =>
        new AnteprimaIngranditaDialog(modello) { Owner = Proprietaria }.ShowDialog();

    private static MessageBoxResult Mostra(string messaggio, string titolo, MessageBoxButton pulsanti, MessageBoxImage icona, MessageBoxResult predefinito) =>
        Proprietaria is { } finestra
            ? MessageBox.Show(finestra, messaggio, titolo, pulsanti, icona, predefinito)
            : MessageBox.Show(messaggio, titolo, pulsanti, icona, predefinito);
}
