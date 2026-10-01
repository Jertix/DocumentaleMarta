using System.Diagnostics;

namespace DocumentaleMarta.App.Servizi;

/// <summary>
/// Le azioni su Windows: aprire un file con il suo programma, aprire Esplora file, riavviare il programma.
/// </summary>
public class ShellService : IShellService
{
    /// <summary>Apre Esplora file dentro la cartella indicata.</summary>
    public void ApriCartella(string percorso) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{percorso}\"") { UseShellExecute = true });

    /// <summary>Apre il file con il programma che Windows associa a quel tipo di file.</summary>
    public void ApriFile(string percorso) =>
        Process.Start(new ProcessStartInfo(percorso) { UseShellExecute = true });

    /// <summary>Apre Esplora file nella cartella del file e lo mette in evidenza.</summary>
    public void MostraFileInEsplora(string percorso) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{percorso}\"") { UseShellExecute = true });

    /// <summary>
    /// Fa partire una nuova copia di Documentale e chiude quella attuale (serve a ricominciare con un archivio diverso).
    /// </summary>
    public void RiavviaApplicazione()
    {
        // La nuova copia parte subito e legge le impostazioni appena salvate; quella attuale si chiude pulita (servizi compresi).
        if (Environment.ProcessPath is { } programma)
            Process.Start(new ProcessStartInfo(programma) { UseShellExecute = true });
        System.Windows.Application.Current?.Shutdown();
    }
}
