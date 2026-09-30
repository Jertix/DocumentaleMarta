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
    public static ServiceProvider Crea(ImpostazioniApp impostazioni, string percorsoDatabase)
    {
        var collezione = new ServiceCollection();
        collezione.AddSingleton(impostazioni);
        collezione.AddSingleton(new AlertService(impostazioni));
        collezione.AddSingleton<IArchivioFileService>(new ArchivioFileService(impostazioni.PercorsoRadice));
        collezione.AddSingleton<IDbContextFactory<AppDbContext>>(new AppDbContextFactory(ArchivioDatabase.CreaOpzioni(percorsoDatabase)));
        collezione.AddSingleton<IArchivioService, ArchivioService>();

        // Lettura del testo dei documenti in background e ricerca nel testo.
        var ocr = new OcrWindows();
        collezione.AddSingleton<IOcr>(ocr);
        collezione.AddSingleton<IEstrattoreTesto>(new EstrattoreTestoSemplice());
        collezione.AddSingleton<IEstrattoreTesto>(new EstrattoreOfficeOpenXml());
        collezione.AddSingleton<IEstrattoreTesto>(new EstrattorePdf(ocr));
        collezione.AddSingleton<IEstrattoreTesto>(new EstrattoreImmagine(ocr));
        collezione.AddSingleton<IndicizzazioneService>();
        collezione.AddSingleton<IIndicizzatore>(p => p.GetRequiredService<IndicizzazioneService>());
        collezione.AddSingleton<IMonitorIndicizzazione>(p => p.GetRequiredService<IndicizzazioneService>());
        collezione.AddSingleton<IRicercaService, RicercaService>();

        collezione.AddSingleton<IDialogService, DialogService>();
        collezione.AddSingleton<IShellService, ShellService>();
        collezione.AddSingleton<MainViewModel>();
        collezione.AddSingleton<MainWindow>();
        return collezione.BuildServiceProvider();
    }
}
