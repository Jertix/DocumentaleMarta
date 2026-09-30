using System.Diagnostics;

namespace DocumentaleMarta.App.Servizi;

public class ShellService : IShellService
{
    public void ApriCartella(string percorso) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{percorso}\"") { UseShellExecute = true });

    public void ApriFile(string percorso) =>
        Process.Start(new ProcessStartInfo(percorso) { UseShellExecute = true });

    public void MostraFileInEsplora(string percorso) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{percorso}\"") { UseShellExecute = true });
}
