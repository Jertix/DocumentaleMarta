using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Data;

namespace DocumentaleMarta.Tests;

/// <summary>Un archivio vero (database SQLite e cartelle) in una cartella temporanea, con dialoghi e shell finti.</summary>
public sealed class ArchivioDiProva : IDisposable
{
    public CartellaTemporanea Tmp { get; } = new();
    public string Radice { get; }
    public AppDbContextFactory Factory { get; }
    public ArchivioFileService Files { get; }
    public ArchivioService Servizio { get; }
    public FintoDialogService Dialog { get; } = new();
    public FintoShellService Shell { get; } = new();

    public ArchivioDiProva()
    {
        Radice = Tmp.Combina("Documentale");
        var percorsoDb = ArchivioDatabase.Inizializza(Radice);
        Factory = new AppDbContextFactory(ArchivioDatabase.CreaOpzioni(percorsoDb));
        Files = new ArchivioFileService(Radice, usaCestino: false);
        Servizio = new ArchivioService(Factory, Files);
    }

    public string Fisico(params string[] parti) => Files.PercorsoAssoluto(Path.Combine(parti));

    /// <summary>Crea un'area e una cartella con i file indicati (creati al volo fuori dall'archivio).</summary>
    public async Task<CartellaDettaglio> CreaCartellaAsync(string area, string titolo, params string[] nomiFile) =>
        await CreaCartellaInAreaAsync(await Servizio.CreaAreaAsync(area), titolo, nomiFile);

    /// <summary>Crea una cartella in un'area già esistente, con i file indicati (creati al volo fuori dall'archivio).</summary>
    public async Task<CartellaDettaglio> CreaCartellaInAreaAsync(
        int areaId, string titolo, string[] nomiFile, DateOnly? scadenza = null)
    {
        var sorgenti = nomiFile.Select(n => Tmp.CreaFile(Path.Combine("sorgenti", n), "contenuto di " + n)).ToList();
        return await Servizio.CreaCartellaConDatiAsync(areaId, new DatiCartella(titolo, null, scadenza, false, null), sorgenti);
    }

    public void Dispose() => Tmp.Dispose();
}
