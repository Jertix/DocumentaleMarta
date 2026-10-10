using System.IO;
using System.Windows;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Modelli;
using Microsoft.Win32;

namespace DocumentaleMarta.App.Servizi;

/// <summary>
/// Le finestre di dialogo vere del programma (messaggi, domande, scelta di file e cartelle, finestre delle Impostazioni e
/// simili), sempre aperte sopra la finestra principale.
/// </summary>
public class DialogService : IDialogService
{
    private const string TitoloApplicazione = "Documentale";

    private const string FiltroFile =
        "Documenti e immagini|*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.odt;*.ods;*.odp;*.odg;*.ott;*.ots;*.otp;*.otg;*.rtf;*.txt;*.csv;*.jpg;*.jpeg;*.png;*.tif;*.tiff;*.bmp;*.gif" +
        "|Tutti i file|*.*";

    private static Window? Proprietaria => Application.Current?.MainWindow;

    /// <summary>
    /// Apre la finestra che chiede un testo (nome di un'area, titolo di una cartella...). Restituisce il testo scritto, o
    /// null se l'utente annulla.
    /// </summary>
    public string? ChiediTesto(string titolo, string messaggio, string valoreIniziale, Func<string, string?> validatore)
    {
        var dialogo = new InputDialog(titolo, messaggio, valoreIniziale, validatore) { Owner = Proprietaria };
        return dialogo.ShowDialog() == true ? dialogo.Testo : null;
    }

    /// <summary>
    /// Domanda sì/no per un'azione che distrugge qualcosa (con il segno di avvertimento): la risposta preselezionata è
    /// «No».
    /// </summary>
    public bool Conferma(string titolo, string messaggio) =>
        Mostra(messaggio, titolo, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <summary>
    /// Domanda sì/no su una proposta (senza il segno di avvertimento): la risposta preselezionata è «Sì», o «No» se
    /// <paramref name="predefinitoSi"/> è falso.
    /// </summary>
    public bool Chiedi(string titolo, string messaggio, bool predefinitoSi = true) =>
        Mostra(messaggio, titolo, MessageBoxButton.YesNo, MessageBoxImage.Question,
            predefinitoSi ? MessageBoxResult.Yes : MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <summary>Mostra un messaggio d'errore con il solo pulsante OK.</summary>
    public void MostraErrore(string messaggio) =>
        Mostra(messaggio, TitoloApplicazione, MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.OK);

    /// <summary>Mostra un messaggio informativo con il solo pulsante OK.</summary>
    public void MostraMessaggio(string titolo, string messaggio) =>
        Mostra(messaggio, titolo, MessageBoxButton.OK, MessageBoxImage.Information, MessageBoxResult.OK);

    /// <summary>
    /// Apre la scelta di una cartella (partendo da quella indicata, se esiste). Restituisce il percorso scelto o null se
    /// l'utente annulla.
    /// </summary>
    public string? SelezionaCartella(string titolo, string? percorsoIniziale)
    {
        var dialogo = new OpenFolderDialog { Title = titolo, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(percorsoIniziale) && Directory.Exists(percorsoIniziale))
            dialogo.InitialDirectory = percorsoIniziale;
        var confermato = Proprietaria is { } finestra ? dialogo.ShowDialog(finestra) : dialogo.ShowDialog();
        return confermato == true ? dialogo.FolderName : null;
    }

    /// <summary>
    /// Apre la scelta di un file di backup (ZIP), partendo dalla cartella indicata se esiste. Restituisce il file scelto o
    /// null.
    /// </summary>
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

    /// <summary>
    /// Apre la scelta di uno o più documenti da allegare (documenti, fogli, PDF, immagini...). Lista vuota se l'utente
    /// annulla.
    /// </summary>
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

    /// <summary>Mostra i file che sono già nell'archivio e chiede se allegarli comunque, saltarli o annullare.</summary>
    public SceltaDuplicati ChiediDuplicati(IReadOnlyList<DuplicatoTrovato> duplicati, int totaleFile)
    {
        var dialogo = new DuplicatiDialog(new DuplicatiViewModel(duplicati, totaleFile)) { Owner = Proprietaria };
        return dialogo.ShowDialog() == true ? dialogo.Scelta : SceltaDuplicati.Annulla;
    }

    /// <summary>Mostra la finestra «Nuova cartella»; vero se l'utente conferma la creazione.</summary>
    public bool MostraNuovaCartella(NuovaCartellaViewModel modello) =>
        new NuovaCartellaDialog(modello) { Owner = Proprietaria }.ShowDialog() == true;

    /// <summary>Mostra la finestra con il nome (se richiesto) e le icone di un'area; vero se l'utente conferma.</summary>
    public bool MostraAreaDialog(AreaDialogViewModel modello) =>
        new AreaDialog(modello) { Owner = Proprietaria }.ShowDialog() == true;

    /// <summary>
    /// Mostra la finestra delle Impostazioni; vero se l'utente conferma (Salva o uno dei pulsanti del backup).
    /// </summary>
    public bool MostraImpostazioni(ImpostazioniViewModel modello) =>
        new ImpostazioniDialog(modello) { Owner = Proprietaria }.ShowDialog() == true;

    /// <summary>Mostra la finestra «Informazioni» e torna quando viene chiusa.</summary>
    public void MostraInformazioni(InformazioniViewModel modello) =>
        new InformazioniDialog(modello) { Owner = Proprietaria }.ShowDialog();

    /// <summary>Mostra la pagina di un documento in una finestra grande e torna quando viene chiusa.</summary>
    public void MostraAnteprimaIngrandita(AnteprimaViewModel modello) =>
        new AnteprimaIngranditaDialog(modello) { Owner = Proprietaria }.ShowDialog();

    /// <summary>
    /// Mostra una finestra di messaggio sopra la finestra principale (se c'è) e restituisce il pulsante premuto.
    /// </summary>
    private static MessageBoxResult Mostra(string messaggio, string titolo, MessageBoxButton pulsanti, MessageBoxImage icona, MessageBoxResult predefinito) =>
        Proprietaria is { } finestra
            ? MessageBox.Show(finestra, messaggio, titolo, pulsanti, icona, predefinito)
            : MessageBox.Show(messaggio, titolo, pulsanti, icona, predefinito);
}
