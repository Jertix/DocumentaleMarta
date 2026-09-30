using System.Globalization;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;
using Microsoft.EntityFrameworkCore;

namespace DocumentaleMarta.Data;

public class ArchivioService(IDbContextFactory<AppDbContext> dbFactory, IArchivioFileService files) : IArchivioService
{
    private static readonly StringComparer OrdineAlfabetico =
        StringComparer.Create(CultureInfo.GetCultureInfo("it-IT"), ignoreCase: true);

    public async Task<IReadOnlyList<AreaNodo>> CaricaAlberoAsync()
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var aree = await db.Aree.AsNoTracking()
            .Select(a => new
            {
                a.Id, a.Nome, a.PercorsoRelativo, a.Ordine,
                Cartelle = a.Cartelle.Select(c => new
                {
                    c.Id, c.Titolo, c.PercorsoRelativo, c.DataCreazione,
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
                    .Select(c => new CartellaNodo(c.Id, c.Titolo, c.PercorsoRelativo, c.NumeroDocumenti))
                    .ToList()))
            .ToList();
    }

    // ---------- Aree ----------

    public async Task<int> CreaAreaAsync(string nome)
    {
        nome = Valida(nome, ValidazioneNomi.LunghezzaMassimaArea);
        await using var db = await dbFactory.CreateDbContextAsync();

        if (await db.Aree.AnyAsync(a => a.Nome == nome))
            throw new ArchivioException($"Esiste già un'area chiamata «{nome}».");

        var percorso = files.CreaCartella("", nome);
        try
        {
            var area = new Area { Nome = nome, PercorsoRelativo = percorso };
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

    public async Task EliminaAreaAsync(int areaId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var area = await db.Aree.SingleOrDefaultAsync(a => a.Id == areaId)
                   ?? throw new ArchivioException("L'area non esiste più.");

        await EliminaConCestinoAsync(db, area, area.PercorsoRelativo);
    }

    // ---------- Cartelle ----------

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

    public async Task RinominaCartellaAsync(int cartellaId, string nuovoTitolo)
    {
        var attuale = await CaricaCartellaAsync(cartellaId)
                      ?? throw new ArchivioException("La cartella non esiste più.");
        await AggiornaCartellaAsync(cartellaId, attuale.Dati with { Titolo = nuovoTitolo });
    }

    public async Task<CartellaDettaglio?> CaricaCartellaAsync(int cartellaId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await LeggiDettaglioAsync(db, cartellaId);
    }

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
            return documenti.Select(ADettaglio).ToList();
        }
        catch
        {
            foreach (var copia in copiati)
                files.RimuoviFileCopiato(copia.PercorsoRelativo);
            throw;
        }
    }

    public async Task EliminaDocumentoAsync(int documentoId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var documento = await db.Documenti.SingleOrDefaultAsync(d => d.Id == documentoId)
                        ?? throw new ArchivioException("Il documento non esiste più.");

        await EliminaConCestinoAsync(db, documento, documento.PercorsoRelativo, cartella: false);
    }

    public async Task EliminaCartellaAsync(int cartellaId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var cartella = await db.Cartelle.SingleOrDefaultAsync(c => c.Id == cartellaId)
                       ?? throw new ArchivioException("La cartella non esiste più.");

        await EliminaConCestinoAsync(db, cartella, cartella.PercorsoRelativo);
    }

    public async Task<IReadOnlyList<DocumentoElenco>> CaricaDocumentiAsync(int? areaId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        var righe = await db.Documenti.AsNoTracking()
            .Where(d => areaId == null || d.Cartella.AreaId == areaId)
            .Select(d => new
            {
                d.Id, d.NomeFile, d.Estensione, d.Dimensione, d.DataCaricamento, d.PercorsoRelativo,
                d.CartellaId,
                TitoloCartella = d.Cartella.Titolo,
                d.Cartella.AreaId,
                NomeArea = d.Cartella.Area.Nome,
                Scadenza = d.Cartella.DataScadenza
            })
            .ToListAsync();

        return righe
            .OrderByDescending(d => d.DataCaricamento).ThenByDescending(d => d.Id)
            .Select(d => new DocumentoElenco(
                d.Id, d.NomeFile, d.Estensione, d.Dimensione, d.DataCaricamento, d.PercorsoRelativo,
                d.CartellaId, d.TitoloCartella, d.AreaId, d.NomeArea, d.Scadenza))
            .ToList();
    }

    // ---------- Supporto ----------

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

    private static DocumentoDettaglio ADettaglio(Documento d) =>
        new(d.Id, d.NomeFile, d.Estensione, d.Dimensione, d.DataCaricamento, d.PercorsoRelativo);

    /// <summary>Copia i dati modificabili sulla cartella. Una cartella non completata non ha data di completamento.</summary>
    private static void ApplicaDati(Cartella cartella, DatiCartella dati)
    {
        cartella.Titolo = dati.Titolo;
        cartella.Descrizione = string.IsNullOrWhiteSpace(dati.Descrizione) ? null : dati.Descrizione;
        cartella.DataScadenza = dati.DataScadenza;
        cartella.Completato = dati.Completato;
        cartella.DataCompletamento = dati.Completato ? dati.DataCompletamento : null;
    }

    private static async Task<CartellaDettaglio?> LeggiDettaglioAsync(AppDbContext db, int cartellaId)
    {
        var c = await db.Cartelle.AsNoTracking()
            .Where(c => c.Id == cartellaId)
            .Select(c => new
            {
                c.Id, c.AreaId, NomeArea = c.Area.Nome, c.PercorsoRelativo,
                c.Titolo, c.Descrizione, c.DataScadenza, c.Completato, c.DataCompletamento,
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
                new DatiCartella(c.Titolo, c.Descrizione, c.DataScadenza, c.Completato, c.DataCompletamento),
                c.Documenti.Select(d => new DocumentoDettaglio(d.Id, d.NomeFile, d.Estensione, d.Dimensione, d.DataCaricamento, d.PercorsoRelativo)).ToList());
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
