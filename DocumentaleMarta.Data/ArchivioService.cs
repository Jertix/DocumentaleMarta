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
        nuovoTitolo = Valida(nuovoTitolo, ValidazioneNomi.LunghezzaMassimaTitolo);
        await using var db = await dbFactory.CreateDbContextAsync();

        var cartella = await db.Cartelle
            .Include(c => c.Documenti)
            .SingleOrDefaultAsync(c => c.Id == cartellaId)
            ?? throw new ArchivioException("La cartella non esiste più.");

        if (cartella.Titolo == nuovoTitolo)
            return;

        var vecchioPercorso = cartella.PercorsoRelativo;
        var nuovoPercorso = RinominaOCrea(vecchioPercorso, nuovoTitolo);
        try
        {
            cartella.Titolo = nuovoTitolo;
            cartella.PercorsoRelativo = nuovoPercorso;
            foreach (var documento in cartella.Documenti)
                documento.PercorsoRelativo = SostituisciPrefisso(documento.PercorsoRelativo, vecchioPercorso, nuovoPercorso);
            await db.SaveChangesAsync();
        }
        catch
        {
            Ripristina(nuovoPercorso, vecchioPercorso);
            throw;
        }
    }

    public async Task EliminaCartellaAsync(int cartellaId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var cartella = await db.Cartelle.SingleOrDefaultAsync(c => c.Id == cartellaId)
                       ?? throw new ArchivioException("La cartella non esiste più.");

        await EliminaConCestinoAsync(db, cartella, cartella.PercorsoRelativo);
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
    private async Task EliminaConCestinoAsync(AppDbContext db, object entita, string percorsoRelativo)
    {
        await using var transazione = await db.Database.BeginTransactionAsync();
        db.Remove(entita);
        await db.SaveChangesAsync();
        await Task.Run(() => files.EliminaCartella(percorsoRelativo));
        await transazione.CommitAsync();
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
