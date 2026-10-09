using System.Globalization;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;
using Microsoft.EntityFrameworkCore;

namespace DocumentaleMarta.Data;

/// <param name="indicizzatore">Riceve i documenti appena allegati per leggerne il testo in background; senza, non si indicizza nulla.</param>
public class ArchivioService(
    IDbContextFactory<AppDbContext> dbFactory, IArchivioFileService files, IIndicizzatore? indicizzatore = null) : IArchivioService
{
    private static readonly StringComparer OrdineAlfabetico =
        StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), ignoreCase: true);

    /// <summary>
    /// Legge dal database l'albero: le aree con le loro cartelle (numero di documenti, scadenza, completata, archiviata),
    /// in ordine alfabetico italiano.
    /// </summary>
    public async Task<IReadOnlyList<AreaNodo>> CaricaAlberoAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var aree = await db.Aree.AsNoTracking()
            .Select(a => new
            {
                a.Id, a.Nome, a.PercorsoRelativo, a.Ordine, a.Icona,
                Cartelle = a.Cartelle.Select(c => new
                {
                    c.Id, c.Titolo, c.PercorsoRelativo, c.DataCreazione, c.DataScadenza, c.Completato, c.Archiviata,
                    NumeroDocumenti = c.Documenti.Count
                }).ToList()
            })
            .ToListAsync();

        // L'ordinamento si fa in memoria: SQLite confronterebbe i caratteri accentati e le maiuscole in modo grezzo.
        return aree
            .OrderBy(a => a.Ordine)
            .ThenBy(a => a.Nome, OrdineAlfabetico)
            .Select(a => new AreaNodo(
                a.Id, a.Nome, a.PercorsoRelativo,
                a.Cartelle
                    .OrderBy(c => c.Titolo, OrdineAlfabetico)
                    .ThenBy(c => c.DataCreazione)
                    .Select(c => new CartellaNodo(
                        c.Id, c.Titolo, c.PercorsoRelativo, c.NumeroDocumenti, c.DataScadenza, c.Completato, c.Archiviata))
                    .ToList(),
                a.Icona))
            .ToList();
    }

    // ---------- Aree ----------

    /// <summary>
    /// Crea un'area: controlla il nome e l'icona, crea la cartella sul disco e la riga nel database (se il database
    /// fallisce toglie la cartella). Restituisce il suo numero.
    /// </summary>
    public async Task<int> CreaAreaAsync(string nome, string? icona = null)
    {
        nome = Valida(nome, ValidazioneNomi.LunghezzaMassimaArea);
        var codiceIcona = ValidaIcona(icona);
        await using var db = await dbFactory.CreateDbContextAsync();

        if (await db.Aree.AnyAsync(a => a.Nome == nome))
            throw new ArchivioException($"Esiste già un'area chiamata «{nome}».");

        var percorso = files.CreaCartella("", nome);
        try
        {
            var area = new Area { Nome = nome, PercorsoRelativo = percorso, Icona = codiceIcona };
            db.Aree.Add(area);
            await db.SaveChangesAsync();
            return area.Id;
        }
        catch
        {
            files.RimuoviCartellaVuota(percorso);
            throw;
        }
    }

    /// <summary>
    /// Rinomina un'area: rinomina la cartella sul disco e aggiorna i percorsi di tutte le sue cartelle e documenti; se il
    /// database fallisce rimette il vecchio nome.
    /// </summary>
    public async Task RinominaAreaAsync(int areaId, string nuovoNome)
    {
        nuovoNome = Valida(nuovoNome, ValidazioneNomi.LunghezzaMassimaArea);
        await using var db = await dbFactory.CreateDbContextAsync();

        var area = await db.Aree
            .Include(a => a.Cartelle).ThenInclude(c => c.Documenti)
            .SingleOrDefaultAsync(a => a.Id == areaId)
            ?? throw new ArchivioException("L'area non esiste più.");

        if (area.Nome == nuovoNome)
            return;
        if (await db.Aree.AnyAsync(a => a.Id != areaId && a.Nome == nuovoNome))
            throw new ArchivioException($"Esiste già un'area chiamata «{nuovoNome}».");

        var vecchioPercorso = area.PercorsoRelativo;
        var nuovoPercorso = RinominaOCrea(vecchioPercorso, nuovoNome);
        try
        {
            area.Nome = nuovoNome;
            area.PercorsoRelativo = nuovoPercorso;
            foreach (var cartella in area.Cartelle)
            {
                cartella.PercorsoRelativo = SostituisciPrefisso(cartella.PercorsoRelativo, vecchioPercorso, nuovoPercorso);
                foreach (var documento in cartella.Documenti)
                    documento.PercorsoRelativo = SostituisciPrefisso(documento.PercorsoRelativo, vecchioPercorso, nuovoPercorso);
            }
            await db.SaveChangesAsync();
        }
        catch
        {
            Ripristina(nuovoPercorso, vecchioPercorso);
            throw;
        }
    }

    /// <summary>Cambia l'icona di un'area (null per quella predefinita): è solo un dato del database, nessun file si muove.</summary>
    public async Task ImpostaIconaAreaAsync(int areaId, string? icona)
    {
        var codiceIcona = ValidaIcona(icona);
        await using var db = await dbFactory.CreateDbContextAsync();

        var area = await db.Aree.SingleOrDefaultAsync(a => a.Id == areaId)
                   ?? throw new ArchivioException("L'area non esiste più.");

        if (area.Icona == codiceIcona)
            return;

        area.Icona = codiceIcona;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Controlla l'icona scelta e restituisce il codice da salvare: null se non c'è o è quella predefinita,
    /// altrimenti il codice in maiuscolo. Un codice fuori dall'elenco delle icone proposte è un errore.
    /// </summary>
    private static string? ValidaIcona(string? icona)
    {
        if (string.IsNullOrWhiteSpace(icona))
            return null;
        if (!IconeArea.EValida(icona))
            throw new ArchivioException("L'icona scelta non è tra quelle disponibili.");
        return IconeArea.DaSalvare(icona);
    }

    /// <summary>Elimina un'area con tutte le sue cartelle e i suoi documenti (la cartella va nel Cestino).</summary>
    public async Task EliminaAreaAsync(int areaId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var area = await db.Aree.SingleOrDefaultAsync(a => a.Id == areaId)
                   ?? throw new ArchivioException("L'area non esiste più.");

        await EliminaConCestinoAsync(db, area, area.PercorsoRelativo);
    }

    // ---------- Cartelle ----------

    /// <summary>
    /// Crea una cartella vuota dentro un'area (cartella sul disco e riga nel database) e restituisce il suo numero.
    /// </summary>
    public async Task<int> CreaCartellaAsync(int areaId, string titolo)
    {
        titolo = Valida(titolo, ValidazioneNomi.LunghezzaMassimaTitolo);
        await using var db = await dbFactory.CreateDbContextAsync();

        var area = await db.Aree.SingleOrDefaultAsync(a => a.Id == areaId)
                   ?? throw new ArchivioException("L'area non esiste più.");

        var percorso = files.CreaCartella(area.PercorsoRelativo, titolo);
        try
        {
            var cartella = new Cartella
            {
                AreaId = area.Id,
                Titolo = titolo,
                PercorsoRelativo = percorso,
                DataCreazione = DateTime.Now
            };
            db.Cartelle.Add(cartella);
            await db.SaveChangesAsync();
            return cartella.Id;
        }
        catch
        {
            files.RimuoviCartellaVuota(percorso);
            throw;
        }
    }

    /// <summary>Cambia il titolo di una cartella (e quindi il nome della cartella sul disco).</summary>
    public async Task RinominaCartellaAsync(int cartellaId, string nuovoTitolo)
    {
        var attuale = await CaricaCartellaAsync(cartellaId)
                      ?? throw new ArchivioException("La cartella non esiste più.");
        await AggiornaCartellaAsync(cartellaId, attuale.Dati with { Titolo = nuovoTitolo });
    }

    /// <summary>Legge una cartella con tutti i suoi dati e documenti; null se non esiste più.</summary>
    public async Task<CartellaDettaglio?> CaricaCartellaAsync(int cartellaId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await LeggiDettaglioAsync(db, cartellaId);
    }

    /// <summary>
    /// Crea in un colpo solo una cartella con i suoi dati (scadenza, ricorrenza...) e i documenti da allegare; se qualcosa
    /// fallisce disfa tutto (file copiati e cartella).
    /// </summary>
    public async Task<CartellaDettaglio> CreaCartellaConDatiAsync(
        int areaId, DatiCartella dati, IReadOnlyList<string> fileDaAllegare)
    {
        var titolo = Valida(dati.Titolo, ValidazioneNomi.LunghezzaMassimaTitolo);
        await using var db = await dbFactory.CreateDbContextAsync();

        var area = await db.Aree.SingleOrDefaultAsync(a => a.Id == areaId)
                   ?? throw new ArchivioException("L'area non esiste più.");

        var percorso = files.CreaCartella(area.PercorsoRelativo, titolo);
        var copiati = new List<FileArchiviato>();
        try
        {
            copiati = await CopiaFileAsync(fileDaAllegare, percorso);

            var cartella = new Cartella
            {
                AreaId = area.Id,
                Titolo = titolo,
                PercorsoRelativo = percorso,
                DataCreazione = DateTime.Now
            };
            ApplicaDati(cartella, dati with { Titolo = titolo });
            foreach (var copia in copiati)
                cartella.Documenti.Add(NuovoDocumento(copia));

            db.Cartelle.Add(cartella);
            await db.SaveChangesAsync();
            indicizzatore?.Accoda(cartella.Documenti.Select(d => d.Id));
            return (await LeggiDettaglioAsync(db, cartella.Id))!;
        }
        catch
        {
            foreach (var copia in copiati)
                files.RimuoviFileCopiato(copia.PercorsoRelativo);
            files.RimuoviCartellaVuota(percorso);
            throw;
        }
    }

    /// <summary>
    /// Salva i dati modificati di una cartella; se il titolo cambia rinomina la cartella sul disco e aggiorna i percorsi
    /// dei documenti (rimettendo tutto com'era se il database fallisce).
    /// </summary>
    public async Task<CartellaDettaglio> AggiornaCartellaAsync(int cartellaId, DatiCartella dati)
    {
        var titolo = Valida(dati.Titolo, ValidazioneNomi.LunghezzaMassimaTitolo);
        await using var db = await dbFactory.CreateDbContextAsync();

        var cartella = await db.Cartelle
            .Include(c => c.Documenti)
            .SingleOrDefaultAsync(c => c.Id == cartellaId)
            ?? throw new ArchivioException("La cartella non esiste più.");

        var vecchioPercorso = cartella.PercorsoRelativo;
        var nuovoPercorso = titolo == cartella.Titolo ? vecchioPercorso : RinominaOCrea(vecchioPercorso, titolo);
        var rinominata = nuovoPercorso != vecchioPercorso;
        try
        {
            ApplicaDati(cartella, dati with { Titolo = titolo });
            if (rinominata)
            {
                cartella.PercorsoRelativo = nuovoPercorso;
                foreach (var documento in cartella.Documenti)
                    documento.PercorsoRelativo = SostituisciPrefisso(documento.PercorsoRelativo, vecchioPercorso, nuovoPercorso);
            }
            await db.SaveChangesAsync();
        }
        catch
        {
            if (rinominata)
                Ripristina(nuovoPercorso, vecchioPercorso);
            throw;
        }

        return (await LeggiDettaglioAsync(db, cartellaId))!;
    }

    /// <summary>
    /// Copia dei file nella cartella e li registra come documenti (poi li mette in coda per la lettura del testo); se
    /// qualcosa fallisce toglie le copie già fatte.
    /// </summary>
    public async Task<IReadOnlyList<DocumentoDettaglio>> AllegaDocumentiAsync(int cartellaId, IReadOnlyList<string> percorsiFile)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var cartella = await db.Cartelle.SingleOrDefaultAsync(c => c.Id == cartellaId)
                       ?? throw new ArchivioException("La cartella non esiste più.");
        if (percorsiFile.Count == 0)
            return [];

        var copiati = await CopiaFileAsync(percorsiFile, cartella.PercorsoRelativo);
        try
        {
            var documenti = copiati.Select(NuovoDocumento).ToList();
            foreach (var documento in documenti)
                documento.CartellaId = cartella.Id;
            db.Documenti.AddRange(documenti);
            await db.SaveChangesAsync();
            indicizzatore?.Accoda(documenti.Select(d => d.Id));
            return documenti.Select(ADettaglio).ToList();
        }
        catch
        {
            foreach (var copia in copiati)
                files.RimuoviFileCopiato(copia.PercorsoRelativo);
            throw;
        }
    }

    /// <summary>
    /// Dice quali dei file indicati hanno lo stesso contenuto di documenti già archiviati (anche con nomi diversi) e dove
    /// si trovano.
    /// </summary>
    public async Task<IReadOnlyList<DuplicatoTrovato>> TrovaDuplicatiAsync(IReadOnlyList<string> percorsiFile)
    {
        if (percorsiFile.Count == 0)
            return [];

        // Prima le impronte, fuori dal thread dell'interfaccia: i file possono essere grandi.
        var impronte = await Task.Run(() =>
        {
            var risultato = new List<(string Percorso, string Hash)>();
            foreach (var percorso in percorsiFile)
            {
                try { risultato.Add((percorso, files.CalcolaHash(percorso))); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
            return risultato;
        });
        if (impronte.Count == 0)
            return [];

        await using var db = await dbFactory.CreateDbContextAsync();
        var hash = impronte.Select(i => i.Hash).Distinct().ToList();
        var presenti = await db.Documenti.AsNoTracking()
            .Where(d => hash.Contains(d.Hash))
            .Select(d => new
            {
                d.Hash, d.Id, d.NomeFile, d.CartellaId, TitoloCartella = d.Cartella.Titolo, NomeArea = d.Cartella.Area.Nome,
                d.DataCaricamento
            })
            .ToListAsync();

        return impronte
            .Select(i => new DuplicatoTrovato(
                i.Percorso,
                Path.GetFileName(i.Percorso),
                presenti.Where(p => p.Hash == i.Hash)
                    .OrderBy(p => p.DataCaricamento).ThenBy(p => p.Id)
                    .Select(p => new DocumentoGiaArchiviato(p.Id, p.NomeFile, p.CartellaId, p.TitoloCartella, p.NomeArea))
                    .ToList()))
            .Where(d => d.GiaArchiviati.Count > 0)
            .ToList();
    }

    /// <summary>Elimina un documento dal database e il suo file nel Cestino.</summary>
    public async Task EliminaDocumentoAsync(int documentoId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var documento = await db.Documenti.SingleOrDefaultAsync(d => d.Id == documentoId)
                        ?? throw new ArchivioException("Il documento non esiste più.");

        await EliminaConCestinoAsync(db, documento, documento.PercorsoRelativo, cartella: false);
    }

    /// <summary>Elimina una cartella con i suoi documenti (la cartella va nel Cestino).</summary>
    public async Task EliminaCartellaAsync(int cartellaId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var cartella = await db.Cartelle.SingleOrDefaultAsync(c => c.Id == cartellaId)
                       ?? throw new ArchivioException("La cartella non esiste più.");

        await EliminaConCestinoAsync(db, cartella, cartella.PercorsoRelativo);
    }

    /// <summary>
    /// Sposta un documento in un'altra cartella: aggiorna il database e sposta il file, in una transazione (se lo
    /// spostamento del file fallisce il database resta com'era).
    /// </summary>
    public async Task SpostaDocumentoAsync(int documentoId, int cartellaDestinazioneId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var documento = await db.Documenti.SingleOrDefaultAsync(d => d.Id == documentoId)
                        ?? throw new ArchivioException("Il documento non esiste più.");
        var destinazione = await db.Cartelle.SingleOrDefaultAsync(c => c.Id == cartellaDestinazioneId)
                           ?? throw new ArchivioException("La cartella di destinazione non esiste più.");

        if (documento.CartellaId == destinazione.Id)
            return;

        // Prima il database, dentro una transazione; poi il file. Se lo spostamento del file fallisce la transazione
        // non viene confermata e il database resta com'era.
        await using var transazione = await db.Database.BeginTransactionAsync();

        var vecchioPercorso = documento.PercorsoRelativo;
        var nome = NomiFileSicuri.RendiUnivoco(
            Path.GetFileName(vecchioPercorso),
            n => files.Esiste(Path.Combine(destinazione.PercorsoRelativo, n)));

        documento.CartellaId = destinazione.Id;
        documento.NomeFile = nome;
        documento.PercorsoRelativo = Path.Combine(destinazione.PercorsoRelativo, nome);
        await db.SaveChangesAsync();

        var effettivo = await Task.Run(() => files.SpostaFile(vecchioPercorso, destinazione.PercorsoRelativo));
        if (effettivo != documento.PercorsoRelativo)
        {
            // Nel frattempo qualcuno ha creato un file con quel nome: il file ha preso un altro nome, il database si allinea.
            documento.PercorsoRelativo = effettivo;
            documento.NomeFile = Path.GetFileName(effettivo);
            await db.SaveChangesAsync();
        }

        await transazione.CommitAsync();
    }

    /// <summary>L'elenco dei documenti di tutto l'archivio o di un'area, archiviati compresi.</summary>
    public Task<IReadOnlyList<DocumentoElenco>> CaricaDocumentiAsync(int? areaId) => CaricaElencoAsync(areaId, soloArchiviate: false);

    /// <summary>L'elenco dei soli documenti delle cartelle archiviate (di tutte le aree o di una).</summary>
    public Task<IReadOnlyList<DocumentoElenco>> CaricaDocumentiArchiviatiAsync(int? areaId) => CaricaElencoAsync(areaId, soloArchiviate: true);

    // ---------- Archivio completati ----------

    /// <summary>
    /// Mette una cartella completata nell'«Archivio completati» (non sposta nessun file); le cartelle non completate si
    /// rifiutano.
    /// </summary>
    public async Task ArchiviaCartellaAsync(int cartellaId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var cartella = await db.Cartelle.SingleOrDefaultAsync(c => c.Id == cartellaId)
                       ?? throw new ArchivioException("La cartella non esiste più.");

        if (!cartella.Completato)
            throw new ArchivioException($"La cartella «{cartella.Titolo}» non è completata: si archiviano solo le cartelle completate.");
        if (cartella.Archiviata)
            return;

        cartella.Archiviata = true;
        await db.SaveChangesAsync();
    }

    /// <summary>Toglie una cartella dall'«Archivio completati» e la rimette nella sua area.</summary>
    public async Task RipristinaCartellaAsync(int cartellaId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var cartella = await db.Cartelle.SingleOrDefaultAsync(c => c.Id == cartellaId)
                       ?? throw new ArchivioException("La cartella non esiste più.");

        if (!cartella.Archiviata)
            return;

        cartella.Archiviata = false;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Archivia in un colpo tutte le cartelle completate (di tutte le aree o di una) e restituisce quante sono.
    /// </summary>
    public async Task<int> ArchiviaCompletateAsync(int? areaId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Cartelle
            .Where(c => c.Completato && !c.Archiviata && (areaId == null || c.AreaId == areaId))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Archiviata, true));
    }

    /// <param name="soloArchiviate">Vero: solo i documenti delle cartelle archiviate; falso: tutti, archiviate comprese.</param>
    private async Task<IReadOnlyList<DocumentoElenco>> CaricaElencoAsync(int? areaId, bool soloArchiviate)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var righe = await db.Documenti.AsNoTracking()
            .Where(d => areaId == null || d.Cartella.AreaId == areaId)
            .Where(d => !soloArchiviate || d.Cartella.Archiviata)
            .Select(d => new
            {
                d.Id, d.NomeFile, d.Estensione, d.Dimensione, d.DataCaricamento, d.PercorsoRelativo,
                d.CartellaId,
                TitoloCartella = d.Cartella.Titolo,
                d.Cartella.AreaId,
                NomeArea = d.Cartella.Area.Nome,
                Scadenza = d.Cartella.DataScadenza,
                d.Cartella.Completato,
                d.Cartella.Archiviata
            })
            .ToListAsync();

        return righe
            .OrderByDescending(d => d.DataCaricamento).ThenByDescending(d => d.Id)
            .Select(d => new DocumentoElenco(
                d.Id, d.NomeFile, d.Estensione, d.Dimensione, d.DataCaricamento, d.PercorsoRelativo,
                d.CartellaId, d.TitoloCartella, d.AreaId, d.NomeArea, d.Scadenza, d.Completato, d.Archiviata))
            .ToList();
    }

    /// <summary>
    /// Le cartelle non completate con una scadenza, dalla più vicina; quali segnalare lo decide poi il servizio degli
    /// avvisi.
    /// </summary>
    public async Task<IReadOnlyList<CartellaScadenza>> CaricaScadenzeAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var righe = await db.Cartelle.AsNoTracking()
            .Where(c => c.DataScadenza != null && !c.Completato)
            .Select(c => new
            {
                c.Id, c.Titolo, c.AreaId, NomeArea = c.Area.Nome,
                Scadenza = c.DataScadenza!.Value,
                NumeroDocumenti = c.Documenti.Count
            })
            .ToListAsync();

        return righe
            .OrderBy(c => c.Scadenza).ThenBy(c => c.Titolo, OrdineAlfabetico)
            .Select(c => new CartellaScadenza(c.Id, c.Titolo, c.AreaId, c.NomeArea, c.Scadenza, c.NumeroDocumenti))
            .ToList();
    }

    // ---------- Supporto ----------

    /// <summary>
    /// Controlla un nome o un titolo (non vuoto, non troppo lungo, senza caratteri vietati) e lo restituisce senza spazi ai
    /// lati; altrimenti segnala l'errore.
    /// </summary>
    private static string Valida(string? testo, int lunghezzaMassima) =>
        ValidazioneNomi.Errore(testo, lunghezzaMassima) is { } errore
            ? throw new ArchivioException(errore)
            : testo!.Trim();

    /// <summary>
    /// Elimina la riga dal database (le righe figlie vanno a cascata) e poi la cartella fisica nel Cestino,
    /// dentro un'unica transazione: se il Cestino fallisce (file aperto altrove), il database resta com'era.
    /// </summary>
    private async Task EliminaConCestinoAsync(AppDbContext db, object entita, string percorsoRelativo, bool cartella = true)
    {
        await using var transazione = await db.Database.BeginTransactionAsync();
        db.Remove(entita);
        await db.SaveChangesAsync();
        await Task.Run(() =>
        {
            if (cartella)
                files.EliminaCartella(percorsoRelativo);
            else
                files.EliminaFile(percorsoRelativo);
        });
        await transazione.CommitAsync();
    }

    /// <summary>Copia i file uno dopo l'altro (fuori dal thread dell'interfaccia). Se uno fallisce rimuove quelli già copiati.</summary>
    private async Task<List<FileArchiviato>> CopiaFileAsync(IReadOnlyList<string> sorgenti, string cartellaRelativa)
    {
        var copiati = new List<FileArchiviato>();
        try
        {
            foreach (var sorgente in sorgenti)
                copiati.Add(await Task.Run(() => files.CopiaFile(sorgente, cartellaRelativa)));
            return copiati;
        }
        catch
        {
            foreach (var copia in copiati)
                files.RimuoviFileCopiato(copia.PercorsoRelativo);
            throw;
        }
    }

    /// <summary>Crea la riga del database per un file appena copiato, da leggere (indicizzare) in background.</summary>
    private static Documento NuovoDocumento(FileArchiviato copia) => new()
    {
        NomeFile = copia.NomeFile,
        PercorsoRelativo = copia.PercorsoRelativo,
        Estensione = Path.GetExtension(copia.NomeFile).ToLowerInvariant(),
        Dimensione = copia.Dimensione,
        Hash = copia.Hash,
        DataCaricamento = DateTime.Now,
        StatoIndicizzazione = StatoIndicizzazione.DaIndicizzare
    };

    /// <summary>Trasforma un documento del database nei dati che il programma mostra.</summary>
    private static DocumentoDettaglio ADettaglio(Documento d) =>
        new(d.Id, d.NomeFile, d.Estensione, d.Dimensione, d.DataCaricamento, d.PercorsoRelativo);

    /// <summary>
    /// Copia i dati modificabili sulla cartella. Una cartella non completata non ha data né note di completamento e non è
    /// archiviata; senza una scadenza la ricorrenza non ha senso e si azzera.
    /// </summary>
    private static void ApplicaDati(Cartella cartella, DatiCartella dati)
    {
        cartella.Titolo = dati.Titolo;
        cartella.Descrizione = string.IsNullOrWhiteSpace(dati.Descrizione) ? null : dati.Descrizione;
        cartella.DataScadenza = dati.DataScadenza;
        cartella.Ricorrenza = dati.DataScadenza is null ? Ricorrenza.Nessuna : dati.Ricorrenza;
        cartella.Completato = dati.Completato;
        cartella.DataCompletamento = dati.Completato ? dati.DataCompletamento : null;
        cartella.NoteCompletamento = dati.Completato && !string.IsNullOrWhiteSpace(dati.NoteCompletamento)
            ? dati.NoteCompletamento
            : null;

        // Nell'archivio ci sono solo cartelle completate: riaprendola torna a comparire nella sua area.
        if (!dati.Completato)
            cartella.Archiviata = false;
    }

    /// <summary>
    /// Legge dal database una cartella con i suoi documenti in ordine di caricamento; null se non esiste.
    /// </summary>
    private static async Task<CartellaDettaglio?> LeggiDettaglioAsync(AppDbContext db, int cartellaId)
    {
        var c = await db.Cartelle.AsNoTracking()
            .Where(c => c.Id == cartellaId)
            .Select(c => new
            {
                c.Id, c.AreaId, NomeArea = c.Area.Nome, c.PercorsoRelativo,
                c.Titolo, c.Descrizione, c.DataScadenza, c.Ricorrenza, c.Completato, c.DataCompletamento, c.NoteCompletamento,
                c.Archiviata,
                Documenti = c.Documenti
                    .OrderBy(d => d.DataCaricamento).ThenBy(d => d.Id)
                    .Select(d => new { d.Id, d.NomeFile, d.Estensione, d.Dimensione, d.DataCaricamento, d.PercorsoRelativo })
                    .ToList()
            })
            .SingleOrDefaultAsync();

        return c is null
            ? null
            : new CartellaDettaglio(
                c.Id, c.AreaId, c.NomeArea, c.PercorsoRelativo,
                new DatiCartella(
                    c.Titolo, c.Descrizione, c.DataScadenza, c.Completato, c.DataCompletamento, c.Ricorrenza, c.NoteCompletamento),
                c.Documenti.Select(d => new DocumentoDettaglio(d.Id, d.NomeFile, d.Estensione, d.Dimensione, d.DataCaricamento, d.PercorsoRelativo)).ToList(),
                c.Archiviata);
    }

    /// <summary>Rinomina la cartella fisica; se nel frattempo è sparita dal disco ne crea una nuova col nome richiesto.</summary>
    private string RinominaOCrea(string percorsoRelativo, string nuovoNome) =>
        files.Esiste(percorsoRelativo)
            ? files.RinominaCartella(percorsoRelativo, nuovoNome)
            : files.CreaCartella(Path.GetDirectoryName(percorsoRelativo) ?? "", nuovoNome);

    /// <summary>Annulla una rinomina fisica dopo un errore del database. Se non riesce, l'errore originale resta quello vero.</summary>
    private void Ripristina(string percorsoAttuale, string percorsoOriginale)
    {
        try { files.RinominaCartella(percorsoAttuale, Path.GetFileName(percorsoOriginale)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
    }

    /// <summary>Se <paramref name="percorso"/> sta dentro <paramref name="vecchio"/>, lo riscrive dentro <paramref name="nuovo"/>.</summary>
    private static string SostituisciPrefisso(string percorso, string vecchio, string nuovo)
    {
        if (percorso.Equals(vecchio, StringComparison.OrdinalIgnoreCase))
            return nuovo;
        if (percorso.StartsWith(vecchio + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return nuovo + percorso[vecchio.Length..];
        return percorso;
    }
}
