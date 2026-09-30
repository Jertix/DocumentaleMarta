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
        MainWindow = servizi.GetRequiredService<MainWindow>();
        MainWindow.Show();
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
        try
        {
            impostazioni = new ImpostazioniService().Carica();
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

        var collezione = new ServiceCollection();
        collezione.AddSingleton(impostazioni);
        collezione.AddSingleton<IArchivioFileService>(new ArchivioFileService(impostazioni.PercorsoRadice));
        collezione.AddSingleton<IDbContextFactory<AppDbContext>>(new AppDbContextFactory(ArchivioDatabase.CreaOpzioni(percorsoDatabase)));
        collezione.AddSingleton<IArchivioService, ArchivioService>();
        collezione.AddSingleton<IDialogService, DialogService>();
        collezione.AddSingleton<IShellService, ShellService>();
        collezione.AddSingleton<MainViewModel>();
        collezione.AddSingleton<MainWindow>();
        return collezione.BuildServiceProvider();
    }

    private static void OnErroreNonGestito(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Errore($"Si è verificato un errore imprevisto.\n\n{e.Exception.Message}");
        e.Handled = true;
    }

    private static void Errore(string messaggio) =>
        MessageBox.Show(messaggio, Titolo, MessageBoxButton.OK, MessageBoxImage.Error);
}
