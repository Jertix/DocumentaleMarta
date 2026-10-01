using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentaleMarta.App;

public partial class App : Application
{
    private const string Titolo = "Documentale";

    private ServiceProvider? _servizi;

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

    protected override void OnExit(ExitEventArgs e)
    {
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

    private static void OnErroreNonGestito(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Errore($"Si è verificato un errore imprevisto.\n\n{e.Exception.Message}");
        e.Handled = true;
    }

    private static void Errore(string messaggio) =>
        MessageBox.Show(messaggio, Titolo, MessageBoxButton.OK, MessageBoxImage.Error);
}
