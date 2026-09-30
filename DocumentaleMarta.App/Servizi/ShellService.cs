using System.Diagnostics;

namespace DocumentaleMarta.App.Servizi;

public class ShellService : IShellService
{
    public void ApriCartella(string percorso) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{percorso}\"") { UseShellExecute = true });
}
