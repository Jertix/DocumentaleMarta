using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;
using Microsoft.EntityFrameworkCore;

namespace DocumentaleMarta.Tests;

/// <summary>Un estrattore di testo finto: per ogni file risponde quello che il test gli ha detto.</summary>
public class FintoEstrattore(params string[] estensioni) : IEstrattoreTesto
{
    private readonly object _blocco = new();
    private readonly List<string> _letti = [];

    /// <summary>I nomi dei file letti, nell'ordine.</summary>
    public IReadOnlyList<string> Letti { get { lock (_blocco) return [.. _letti]; } }

    /// <summary>Cosa rispondere per un file; di base "contenuto di {nome}".</summary>
    public Func<string, CancellationToken, Task<string>> Logica { get; set; } =
        (percorso, _) => Task.FromResult("contenuto di " + Path.GetFileName(percorso));

    public bool Supporta(string estensione) => estensioni.Contains(estensione);

    public Task<string> EstraiAsync(string percorsoFile, CancellationToken cancellation)
    {
        lock (_blocco)
            _letti.Add(Path.GetFileName(percorsoFile));
        return Logica(percorsoFile, cancellation);
    }
}

/// <summary>Un archivio vero (database SQLite e cartelle) in una cartella temporanea, con dialoghi e shell finti.</summary>
public sealed class ArchivioDiProva : IDisposable
{
    public CartellaTemporanea Tmp { get; } = new();
    public string Radice { get; }
    public AppDbContextFactory Factory { get; }
    public ArchivioFileService Files { get; }
    public ArchivioService Servizio { get; }
    public RicercaService Ricerca { get; }
    public FintoDialogService Dialog { get; } = new();
    public FintoShellService Shell { get; } = new();

    /// <summary>Presente solo se il test ha indicato degli estrattori: i documenti allegati vengono letti in background.</summary>
    public IndicizzazioneService? Indicizzazione { get; }

    public ArchivioDiProva(params IEstrattoreTesto[] estrattori)
    {
        Radice = Tmp.Combina("Documentale");
        var percorsoDb = ArchivioDatabase.Inizializza(Radice);
        Factory = new AppDbContextFactory(ArchivioDatabase.CreaOpzioni(percorsoDb));
        Files = new ArchivioFileService(Radice, usaCestino: false);

        if (estrattori.Length > 0)
        {
            Indicizzazione = new IndicizzazioneService(Factory, Files, estrattori);
            Indicizzazione.Avvia();
        }

        Servizio = new ArchivioService(Factory, Files, Indicizzazione);
        Ricerca = new RicercaService(Factory);
    }

    public string Fisico(params string[] parti) => Files.PercorsoAssoluto(Path.Combine(parti));

    /// <summary>Crea un'area e una cartella con i file indicati (creati al volo fuori dall'archivio).</summary>
    public async Task<CartellaDettaglio> CreaCartellaAsync(string area, string titolo, params string[] nomiFile) =>
        await CreaCartellaInAreaAsync(await Servizio.CreaAreaAsync(area), titolo, nomiFile);

    /// <summary>Crea una cartella in un'area già esistente, con i file indicati (creati al volo fuori dall'archivio).</summary>
    public async Task<CartellaDettaglio> CreaCartellaInAreaAsync(
        int areaId, string titolo, string[] nomiFile, DateOnly? scadenza = null, string? descrizione = null)
    {
        var sorgenti = nomiFile.Select(n => Tmp.CreaFile(Path.Combine("sorgenti", n), "contenuto di " + n)).ToList();
        return await Servizio.CreaCartellaConDatiAsync(
            areaId, new DatiCartella(titolo, descrizione, scadenza, false, null), sorgenti);
    }

    // ---------- Indice del testo (per i test) ----------

    /// <summary>Il testo indicizzato di un documento, o null se nell'indice non c'è.</summary>
    public string? TestoIndicizzato(int documentoId)
    {
        using var db = Factory.CreateDbContext();
        return db.Database
            .SqlQueryRaw<string>("SELECT contenuto AS Value FROM documenti_fts WHERE rowid = {0}", documentoId)
            .ToList().SingleOrDefault();
    }

    public int RigheNellIndice()
    {
        using var db = Factory.CreateDbContext();
        return (int)db.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM documenti_fts").ToList().Single();
    }

    public StatoIndicizzazione StatoDi(int documentoId)
    {
        using var db = Factory.CreateDbContext();
        return db.Documenti.Single(d => d.Id == documentoId).StatoIndicizzazione;
    }

    public void Dispose()
    {
        Indicizzazione?.Dispose();
        Tmp.Dispose();
    }
}
