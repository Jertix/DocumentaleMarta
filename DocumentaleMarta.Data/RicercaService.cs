using System.Globalization;
using System.Text;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;
using Microsoft.EntityFrameworkCore;

namespace DocumentaleMarta.Data;

public class RicercaService(IDbContextFactory<AppDbContext> dbFactory) : IRicercaService
{
    private const int TerminiMassimi = 10;
    private static readonly CompareInfo Confronto = CultureInfo.InvariantCulture.CompareInfo;
    private const CompareOptions IgnoraMaiuscoleEAccenti = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    private record Riga(
        int Id, string NomeFile, string Estensione, long Dimensione, DateTime DataCaricamento, string PercorsoRelativo,
        int CartellaId, string TitoloCartella, string? DescrizioneCartella, int AreaId, string NomeArea,
        DateOnly? Scadenza, bool Completata);

    private record RigaEstratto(long Id, string Estratto);

    public async Task<EsitoRicerca> CercaAsync(string testo, int massimo = 1000)
    {
        var termini = Termini(testo);
        if (termini.Count == 0)
            return EsitoRicerca.Vuoto;

        await using var db = await dbFactory.CreateDbContextAsync();

        var righe = await db.Documenti.AsNoTracking()
            .Select(d => new Riga(
                d.Id, d.NomeFile, d.Estensione, d.Dimensione, d.DataCaricamento, d.PercorsoRelativo,
                d.CartellaId, d.Cartella.Titolo, d.Cartella.Descrizione, d.Cartella.AreaId, d.Cartella.Area.Nome,
                d.Cartella.DataScadenza, d.Cartella.Completato))
            .ToListAsync();

        // Per ogni parola: i documenti il cui testo ne contiene una che inizia così.
        var nelTesto = new List<HashSet<long>>();
        foreach (var termine in termini)
            nelTesto.Add(await DocumentiConParolaAsync(db, termine));

        // Un documento va bene se ogni parola si trova nei nomi oppure nel testo.
        var trovati = new List<(Riga Riga, string Dove, bool NelTesto)>();
        foreach (var riga in righe)
        {
            var dove = "";
            var qualcunaNelTesto = false;
            var tutte = true;

            for (var i = 0; i < termini.Count && tutte; i++)
            {
                var nome = DoveNeiNomi(riga, termini[i]);
                var testoDoc = nelTesto[i].Contains(riga.Id);
                if (nome is null && !testoDoc)
                    tutte = false;

                qualcunaNelTesto |= testoDoc;
                if (dove.Length == 0 && nome is not null)
                    dove = nome;
            }

            if (tutte)
                trovati.Add((riga, dove, qualcunaNelTesto));
        }

        trovati = trovati.OrderByDescending(t => t.Riga.DataCaricamento).ThenByDescending(t => t.Riga.Id).ToList();
        var troncato = trovati.Count > massimo;
        if (troncato)
            trovati = trovati[..massimo];

        var estratti = await EstrattiAsync(db, termini, trovati.Where(t => t.NelTesto).Select(t => t.Riga.Id).ToList());

        var risultati = trovati.Select(t =>
        {
            var r = t.Riga;
            var documento = new DocumentoElenco(
                r.Id, r.NomeFile, r.Estensione, r.Dimensione, r.DataCaricamento, r.PercorsoRelativo,
                r.CartellaId, r.TitoloCartella, r.AreaId, r.NomeArea, r.Scadenza, r.Completata);
            var trovato = estratti.TryGetValue(r.Id, out var estratto) ? estratto : t.Dove;
            return new RisultatoRicerca(documento, trovato);
        }).ToList();

        return new EsitoRicerca(risultati, troncato);
    }

    /// <summary>Le parole da cercare: sequenze di lettere e cifre, senza doppioni (maiuscole e accenti non contano).</summary>
    internal static IReadOnlyList<string> Termini(string? testo)
    {
        var termini = new List<string>();
        var corrente = new StringBuilder();

        void Chiudi()
        {
            if (corrente.Length == 0)
                return;
            var parola = corrente.ToString();
            corrente.Clear();
            if (!termini.Any(t => Confronto.Compare(t, parola, IgnoraMaiuscoleEAccenti) == 0))
                termini.Add(parola);
        }

        foreach (var c in testo ?? "")
        {
            if (char.IsLetterOrDigit(c))
                corrente.Append(c);
            else
                Chiudi();
        }
        Chiudi();

        return termini.Take(TerminiMassimi).ToList();
    }

    private static string? DoveNeiNomi(Riga riga, string termine)
    {
        if (Contiene(riga.NomeFile, termine)) return "Nel nome del file";
        if (Contiene(riga.TitoloCartella, termine)) return "Nel titolo della cartella";
        if (Contiene(riga.DescrizioneCartella, termine)) return "Nella descrizione della cartella";
        if (Contiene(riga.NomeArea, termine)) return "Nel nome dell'area";
        return null;
    }

    private static bool Contiene(string? testo, string termine) =>
        !string.IsNullOrEmpty(testo) && Confronto.IndexOf(testo, termine, IgnoraMaiuscoleEAccenti) >= 0;

    /// <summary>Una parola tra virgolette con l'asterisco: "fattu"* trova "fattura". Le virgolette neutralizzano ogni operatore di FTS5.</summary>
    private static string ComeParolaFts(string termine) => $"\"{termine}\"*";

    private static async Task<HashSet<long>> DocumentiConParolaAsync(AppDbContext db, string termine) =>
        (await db.Database
            .SqlQueryRaw<long>("SELECT rowid AS Value FROM documenti_fts WHERE documenti_fts MATCH {0}", ComeParolaFts(termine))
            .ToListAsync())
        .ToHashSet();

    /// <summary>L'estratto del testo attorno alle parole trovate, con le parole tra i segnaposto di evidenziazione.</summary>
    private static async Task<Dictionary<int, string>> EstrattiAsync(AppDbContext db, IReadOnlyList<string> termini, List<int> ids)
    {
        var estratti = new Dictionary<int, string>();
        if (ids.Count == 0)
            return estratti;

        var query = string.Join(" OR ", termini.Select(ComeParolaFts));

        // Gli Id vengono dal database e sono numeri: si possono scrivere nella query senza rischi.
        // Si procede a blocchi per non superare i limiti di SQLite.
        foreach (var blocco in ids.Chunk(500))
        {
            var elenco = string.Join(",", blocco);
            var sql =
                "SELECT rowid AS Id, snippet(documenti_fts, 0, char(1), char(2), '…', 16) AS Estratto " +
                "FROM documenti_fts WHERE documenti_fts MATCH {0} AND rowid IN (" + elenco + ")";
            var righe = await db.Database.SqlQueryRaw<RigaEstratto>(sql, query).ToListAsync();
            foreach (var riga in righe)
                estratti[(int)riga.Id] = riga.Estratto.ReplaceLineEndings(" ").Trim();
        }
        return estratti;
    }
}
