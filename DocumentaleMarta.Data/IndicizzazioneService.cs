using System.Threading.Channels;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;
using Microsoft.EntityFrameworkCore;

namespace DocumentaleMarta.Data;

/// <summary>
/// Legge in background il testo dei documenti appena allegati e lo mette nell'indice di ricerca.
/// Un documento alla volta (l'OCR è pesante), senza mai bloccare l'interfaccia.
/// </summary>
public sealed class IndicizzazioneService(
    IDbContextFactory<AppDbContext> dbFactory,
    IArchivioFileService files,
    IEnumerable<IEstrattoreTesto> estrattori) : IIndicizzatore, IMonitorIndicizzazione, IDisposable
{
    /// <summary>Del testo di un documento se ne indicizza al massimo questa quantità (circa mille pagine di romanzo).</summary>
    private const int CaratteriMassimi = 2_000_000;

    private readonly IReadOnlyList<IEstrattoreTesto> _estrattori = estrattori.ToList();
    private readonly Channel<int> _canale = Channel.CreateUnbounded<int>(new UnboundedChannelOptions { SingleReader = true });
    private readonly object _blocco = new();
    private readonly HashSet<int> _inCoda = [];
    private readonly HashSet<int> _inAttesaOcr = [];

    /// <summary>Documenti che si stavano leggendo quando è stato chiesto di rifarli: finita la lettura si leggono di nuovo.</summary>
    private readonly HashSet<int> _daRifare = [];

    private string? _corrente;
    private int _idCorrente;
    private int _eliminato;
    private CancellationTokenSource? _annullamento;
    private Task? _lavoratore;
    private TaskCompletionSource _inattivo = NuovoInattivo(giaCompletato: true);

    public StatoCodaIndicizzazione Stato
    {
        get
        {
            lock (_blocco)
                return new StatoCodaIndicizzazione(_inCoda.Count, _corrente, _inAttesaOcr.Count);
        }
    }

    public event Action? Cambiato;

    /// <summary>Fa partire il lavoratore in background. Da chiamare una volta, all'avvio dell'app.</summary>
    public void Avvia()
    {
        if (_lavoratore is not null)
            return;

        _annullamento = new CancellationTokenSource();
        var token = _annullamento.Token;
        _lavoratore = Task.Run(() => CicloAsync(token));
    }

    /// <summary>
    /// Mette dei documenti in coda per la lettura del testo (quelli già in coda non si ripetono) e avvisa chi ascolta.
    /// </summary>
    public void Accoda(IEnumerable<int> documentiIds)
    {
        var aggiunti = false;
        lock (_blocco)
        {
            foreach (var id in documentiIds)
            {
                // Uno già in coda non si accoda due volte.
                if (!_inCoda.Add(id))
                    continue;

                _inAttesaOcr.Remove(id);
                _canale.Writer.TryWrite(id);
                aggiunti = true;
            }

            if (aggiunti && _inattivo.Task.IsCompleted)
                _inattivo = NuovoInattivo(giaCompletato: false);
        }

        if (aggiunti)
            Cambiato?.Invoke();
    }

    /// <summary>
    /// Rimette in coda tutti i documenti di un tipo (es. ".xml"), anche quelli già letti, perché il testo nell'indice va rifatto:
    /// cambia il modo di leggerli, oppure non si leggono più (allora, rielaborati, vengono tolti dall'indice). Un documento che si
    /// sta leggendo proprio adesso si rilegge appena ha finito, così nell'indice resta il testo del modo nuovo.
    /// </summary>
    public async Task RiaccodaPerEstensioneAsync(string estensione)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var ids = await db.Documenti.AsNoTracking()
            .Where(d => d.Estensione == estensione)
            .Select(d => d.Id)
            .ToListAsync();

        lock (_blocco)
        {
            if (ids.Contains(_idCorrente))
                _daRifare.Add(_idCorrente);
        }

        Accoda(ids);
    }

    /// <summary>
    /// Accoda i documenti non ancora letti o la cui lettura era fallita, e quelli marcati "non supportati" quando per il loro
    /// formato non c'era ancora un lettore (es. OpenOffice, aggiunto dopo). Da chiamare all'avvio dell'app.
    /// </summary>
    public async Task AccodaPendentiAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var ids = await db.Documenti.AsNoTracking()
            .Where(d => d.StatoIndicizzazione == StatoIndicizzazione.DaIndicizzare
                        || d.StatoIndicizzazione == StatoIndicizzazione.Errore)
            .Select(d => d.Id)
            .ToListAsync();

        var nonSupportati = await db.Documenti.AsNoTracking()
            .Where(d => d.StatoIndicizzazione == StatoIndicizzazione.NonSupportato)
            .Select(d => new { d.Id, d.Estensione })
            .ToListAsync();
        ids.AddRange(nonSupportati.Where(d => _estrattori.Any(e => e.Supporta(d.Estensione))).Select(d => d.Id));

        Accoda(ids);
    }

    /// <summary>Completa quando la coda è vuota e nessun documento è in lavorazione. Serve soprattutto ai test.</summary>
    public Task AttendiSvuotamentoAsync()
    {
        lock (_blocco)
            return _inattivo.Task;
    }

    /// <summary>
    /// Il lavoratore in background: prende un documento alla volta dalla coda e lo legge; un errore su un documento non
    /// ferma gli altri.
    /// </summary>
    private async Task CicloAsync(CancellationToken cancellation)
    {
        try
        {
            await foreach (var id in _canale.Reader.ReadAllAsync(cancellation))
            {
                try
                {
                    await ElaboraAsync(id, cancellation);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception)
                {
                    // Un errore imprevisto su un documento (es. il database è occupato) non deve fermare gli altri:
                    // il documento resta "da indicizzare" e si riprova al prossimo avvio.
                }
                finally
                {
                    Concludi(id);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Chiusura dell'app.
        }
    }

    /// <summary>
    /// Legge un documento: sceglie il lettore adatto al formato, estrae il testo e lo scrive nell'indice (o segna il
    /// documento come non supportato, in errore o in attesa del riconoscimento del testo).
    /// </summary>
    private async Task ElaboraAsync(int id, CancellationToken cancellation)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellation);
        var documento = await db.Documenti.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, cancellation);
        if (documento is null)
            return; // eliminato mentre aspettava in coda

        lock (_blocco)
        {
            _corrente = documento.NomeFile;
            _idCorrente = id;
        }
        Cambiato?.Invoke();

        var estrattore = _estrattori.FirstOrDefault(e => e.Supporta(documento.Estensione));
        if (estrattore is null)
        {
            await ScriviAsync(db, id, StatoIndicizzazione.NonSupportato, null, cancellation);
            return;
        }

        string testo;
        try
        {
            var percorso = files.PercorsoAssoluto(documento.PercorsoRelativo);
            testo = await estrattore.EstraiAsync(percorso, cancellation);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OcrNonDisponibileException)
        {
            // Il documento resta "da indicizzare": si leggerà appena l'OCR sarà utilizzabile (al prossimo avvio).
            lock (_blocco)
                _inAttesaOcr.Add(id);
            return;
        }
        catch (Exception)
        {
            // File rovinato, protetto da password o sparito dal disco.
            await ScriviAsync(db, id, StatoIndicizzazione.Errore, null, cancellation);
            return;
        }

        if (testo.Length > CaratteriMassimi)
            testo = testo[..CaratteriMassimi];
        await ScriviAsync(db, id, StatoIndicizzazione.Indicizzato, testo, cancellation);
    }

    /// <summary>Aggiorna stato del documento e riga dell'indice insieme: o tutto o niente.</summary>
    private static async Task ScriviAsync(
        AppDbContext db, int id, StatoIndicizzazione stato, string? testo, CancellationToken cancellation)
    {
        await using var transazione = await db.Database.BeginTransactionAsync(cancellation);

        var aggiornati = await db.Documenti.Where(d => d.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.StatoIndicizzazione, stato), cancellation);
        if (aggiornati == 0)
            return; // eliminato mentre lo si leggeva: niente da scrivere (la transazione non confermata si annulla)

        await db.Database.ExecuteSqlRawAsync("DELETE FROM documenti_fts WHERE rowid = {0}", [id], cancellation);
        if (!string.IsNullOrWhiteSpace(testo))
            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO documenti_fts(rowid, contenuto) VALUES ({0}, {1})", [id, testo], cancellation);

        await transazione.CommitAsync(cancellation);
    }

    /// <summary>
    /// Un documento ha finito: lo toglie dalla coda e, se la coda è vuota, avvisa chi aspetta. Se nel frattempo era stato chiesto
    /// di rifarlo, resta in coda e si rilegge.
    /// </summary>
    private void Concludi(int id)
    {
        lock (_blocco)
        {
            _corrente = null;
            _idCorrente = 0;

            if (_daRifare.Remove(id))
                _canale.Writer.TryWrite(id);
            else
            {
                _inCoda.Remove(id);
                if (_inCoda.Count == 0)
                    _inattivo.TrySetResult();
            }
        }
        Cambiato?.Invoke();
    }

    /// <summary>
    /// Ferma il lavoratore in background (aspettando al massimo qualche secondo); si può chiamare più volte.
    /// </summary>
    public void Dispose()
    {
        // Si può chiamare più volte (es. dalla chiusura dell'app e dal contenitore dei servizi).
        if (Interlocked.Exchange(ref _eliminato, 1) == 1)
            return;

        _annullamento?.Cancel();
        _canale.Writer.TryComplete();
        try { _lavoratore?.Wait(TimeSpan.FromSeconds(3)); }
        catch (AggregateException) { /* annullato */ }
        _annullamento?.Dispose();
    }

    /// <summary>Un segnale «la coda è vuota», già scattato o da far scattare più tardi.</summary>
    private static TaskCompletionSource NuovoInattivo(bool giaCompletato)
    {
        var t = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (giaCompletato)
            t.SetResult();
        return t;
    }
}
