using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using DocumentaleMarta.App.Grafica;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace DocumentaleMarta.App;

/// <summary>
/// Il programma: all'avvio legge le impostazioni, prepara archivio e database, collega i servizi, applica il tema e apre la
/// finestra principale.
/// </summary>
public partial class App : Application
{
    private const string Titolo = "Documentale";

    private ServiceProvider? _servizi;
    private GestoreAspetto? _aspetto;

    /// <summary>
    /// Avvio del programma: imposta la lingua dei formati, prepara archivio e servizi, applica il tema scelto e apre la
    /// finestra principale (se qualcosa non va avvisa e chiude).
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnErroreNonGestito;

        // Senza questo WPF formatta date e numeri all'americana invece che secondo le impostazioni di Windows.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        var servizi = Avvia();
        if (servizi is null)
        {
            Shutdown(1);
            return;
        }

        _servizi = servizi;

        // Prima di creare qualsiasi finestra: così nascono già con i colori scelti. Se Windows cambia tema e si segue Windows, ci si adegua.
        _aspetto = servizi.GetRequiredService<GestoreAspetto>();
        _aspetto.Applica(servizi.GetRequiredService<ImpostazioniApp>().Aspetto);
        SystemEvents.UserPreferenceChanged += OnPreferenzeDiWindowsCambiate;

        AvviaIndicizzazione(servizi);
        MainWindow = servizi.GetRequiredService<MainWindow>();
        MainWindow.Show();
    }

    /// <summary>
    /// Fa partire la lettura dei documenti in background e riprende quelli rimasti in sospeso dall'ultima volta
    /// (non ancora letti, andati in errore o in attesa del riconoscimento del testo).
    /// </summary>
    private static void AvviaIndicizzazione(ServiceProvider servizi)
    {
        var indicizzazione = servizi.GetRequiredService<IndicizzazioneService>();
        indicizzazione.Avvia();

        _ = Task.Run(async () =>
        {
            try { await indicizzazione.AccodaPendentiAsync(); }
            catch (Exception) { /* si riproverà al prossimo avvio: i documenti restano da leggere */ }
        });
    }

    /// <summary>Windows avvisa da un altro thread: si passa a quello del programma.</summary>
    private void OnPreferenzeDiWindowsCambiate(object? mittente, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or UserPreferenceCategory.VisualStyle)
            Dispatcher.BeginInvoke(() => _aspetto?.SistemaCambiato());
    }

    /// <summary>Chiusura del programma: smette di ascoltare i cambi di tema di Windows e libera i servizi.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= OnPreferenzeDiWindowsCambiate;
        _servizi?.Dispose();
        base.OnExit(e);
    }

    /// <summary>Legge le impostazioni, prepara archivio e database e compone i servizi. Restituisce null (dopo aver avvisato l'utente) se non può partire.</summary>
    private static ServiceProvider? Avvia()
    {
        ImpostazioniApp impostazioni;
        var servizioImpostazioni = new ImpostazioniService();
        try
        {
            impostazioni = servizioImpostazioni.Carica();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Errore($"Impossibile leggere le impostazioni.\n\n{ex.Message}");
            return null;
        }

        var problemi = impostazioni.Valida();
        if (problemi.Count > 0)
        {
            Errore($"Le impostazioni non sono valide:\n\n- {string.Join("\n- ", problemi)}\n\nCorreggi il file {ImpostazioniService.PercorsoPredefinito()} e riavvia l'applicazione.");
            return null;
        }

        string percorsoDatabase;
        try
        {
            percorsoDatabase = ArchivioDatabase.Inizializza(impostazioni.PercorsoRadice);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbUpdateException
                                       or Microsoft.Data.Sqlite.SqliteException or ArgumentException or NotSupportedException)
        {
            Errore($"Impossibile aprire l'archivio in «{impostazioni.PercorsoRadice}».\n\n{ex.Message}");
            return null;
        }

        return Composizione.Crea(impostazioni, percorsoDatabase, servizioImpostazioni);
    }

    /// <summary>
    /// Un errore che nessuno ha gestito non deve far chiudere il programma di colpo: lo si mostra all'utente e si va
    /// avanti.
    /// </summary>
    private static void OnErroreNonGestito(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Errore($"Si è verificato un errore imprevisto.\n\n{e.Exception.Message}");
        e.Handled = true;
    }

    /// <summary>
    /// Mostra all'utente un messaggio d'errore con il titolo del programma (prima che esistano le finestre vere).
    /// </summary>
    private static void Errore(string messaggio) =>
        MessageBox.Show(messaggio, Titolo, MessageBoxButton.OK, MessageBoxImage.Error);
}
