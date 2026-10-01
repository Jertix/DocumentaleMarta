using System.Windows;
using DocumentaleMarta.App.Grafica;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.App.Viste;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;
using DocumentaleMarta.Data.Testo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DocumentaleMarta.App.Servizi;

/// <summary>Il collegamento di tutti i servizi dell'applicazione. A parte, così si può provare che si costruiscano senza aprire nulla.</summary>
public static class Composizione
{
    /// <param name="servizioImpostazioni">Serve alla finestra "Impostazioni" per salvare le modifiche; senza, la finestra non compare.</param>
    public static ServiceProvider Crea(
        ImpostazioniApp impostazioni, string percorsoDatabase, ImpostazioniService? servizioImpostazioni = null)
    {
        var collezione = new ServiceCollection();
        collezione.AddSingleton(impostazioni);
        if (servizioImpostazioni is not null)
            collezione.AddSingleton(servizioImpostazioni);
        collezione.AddSingleton(new AlertService(impostazioni));
        collezione.AddSingleton<IArchivioFileService>(new ArchivioFileService(impostazioni.PercorsoRadice));
        collezione.AddSingleton<IDbContextFactory<AppDbContext>>(new AppDbContextFactory(ArchivioDatabase.CreaOpzioni(percorsoDatabase)));
        collezione.AddSingleton<IArchivioService, ArchivioService>();
        collezione.AddSingleton<IBackupService, BackupService>();

        // Lettura del testo dei documenti in background e ricerca nel testo.
        var ocr = new OcrWindows();
        collezione.AddSingleton<IOcr>(ocr);
        collezione.AddSingleton<IEstrattoreTesto>(new EstrattoreTestoSemplice());
        collezione.AddSingleton<IEstrattoreTesto>(new EstrattoreOfficeOpenXml());
        collezione.AddSingleton<IEstrattoreTesto>(new EstrattoreOpenDocument());
        collezione.AddSingleton<IEstrattoreTesto>(new EstrattorePdf(ocr));
        collezione.AddSingleton<IEstrattoreTesto>(new EstrattoreImmagine(ocr));
        collezione.AddSingleton<IndicizzazioneService>();
        collezione.AddSingleton<IIndicizzatore>(p => p.GetRequiredService<IndicizzazioneService>());
        collezione.AddSingleton<IMonitorIndicizzazione>(p => p.GetRequiredService<IndicizzazioneService>());
        collezione.AddSingleton<IRicercaService, RicercaService>();

        // L'aspetto (tema chiaro o scuro) vale per tutto il programma; senza un'applicazione (nelle prove) non fa nulla.
        collezione.AddSingleton(_ => new GestoreAspetto(
            Application.Current is { } applicazione ? new OspiteApplicazione(applicazione) : new OspiteNullo()));
        collezione.AddSingleton<IAspettoService>(p => p.GetRequiredService<GestoreAspetto>());

        collezione.AddSingleton<IDialogService, DialogService>();
        collezione.AddSingleton<IShellService, ShellService>();
        collezione.AddSingleton<IGeneratoreAnteprima, GeneratoreAnteprima>();
        collezione.AddSingleton<MainViewModel>();
        collezione.AddSingleton<MainWindow>();
        return collezione.BuildServiceProvider();
    }
}
