using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;
using DocumentaleMarta.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DocumentaleMarta.Tests;

/// <summary>L'"Archivio completati": una cartella completata si può archiviare (resta dov'è su disco) e ripristinare.</summary>
public class ArchivioCompletatiServiceTests : IDisposable
{
    private static readonly DateOnly Oggi = new(2026, 10, 1);

    private readonly ArchivioDiProva _a = new();

    public void Dispose() => _a.Dispose();

    private async Task<int> AreaAsync(string nome) => await _a.Servizio.CreaAreaAsync(nome);

    /// <summary>Una cartella con un file, completata se richiesto.</summary>
    private async Task<CartellaDettaglio> CartellaAsync(int areaId, string titolo, bool completata, params string[] file)
    {
        var creata = await _a.CreaCartellaInAreaAsync(areaId, titolo, file.Length == 0 ? ["doc.pdf"] : file);
        return completata
            ? await _a.Servizio.AggiornaCartellaAsync(creata.Id, creata.Dati with { Completato = true, DataCompletamento = Oggi })
            : creata;
    }

    private async Task<CartellaNodo> NodoAsync(int cartellaId) =>
        (await _a.Servizio.CaricaAlberoAsync()).SelectMany(a => a.Cartelle).Single(c => c.Id == cartellaId);

    // ---------- Archiviare ----------

    [Fact]
    public async Task UnaCartellaCompletata_SiArchivia_ELoDiceLAlbero()
    {
        var c = await CartellaAsync(await AreaAsync("Fatture"), "Pagata", completata: true);

        await _a.Servizio.ArchiviaCartellaAsync(c.Id);

        Assert.True((await NodoAsync(c.Id)).Archiviata);
        Assert.True((await _a.Servizio.CaricaCartellaAsync(c.Id))!.Archiviata);
    }

    [Fact]
    public async Task ArchiviareNonSpostaNulla_NeSuDiscoNeNelDatabase()
    {
        var areaId = await AreaAsync("Fatture");
        var c = await CartellaAsync(areaId, "Pagata", completata: true, "uno.pdf", "due.pdf");
        var prima = await _a.Servizio.CaricaCartellaAsync(c.Id);
        var filePrima = Directory.GetFiles(_a.Fisico("Fatture", "Pagata")).Order().ToList();

        await _a.Servizio.ArchiviaCartellaAsync(c.Id);

        var dopo = (await _a.Servizio.CaricaCartellaAsync(c.Id))!;
        Assert.Equal(prima!.PercorsoRelativo, dopo.PercorsoRelativo);
        Assert.Equal(prima.AreaId, dopo.AreaId);
        Assert.Equal(prima.Documenti.Select(d => d.PercorsoRelativo), dopo.Documenti.Select(d => d.PercorsoRelativo));
        Assert.Equal(filePrima, Directory.GetFiles(_a.Fisico("Fatture", "Pagata")).Order());
        Assert.All(dopo.Documenti, d => Assert.True(_a.Files.Esiste(d.PercorsoRelativo)));
    }

    [Fact]
    public async Task UnaCartellaNonCompletata_NonSiArchivia()
    {
        var c = await CartellaAsync(await AreaAsync("Fatture"), "Aperta", completata: false);

        var errore = await Assert.ThrowsAsync<ArchivioException>(() => _a.Servizio.ArchiviaCartellaAsync(c.Id));

        Assert.Contains("non è completata", errore.Message);
        Assert.Contains("«Aperta»", errore.Message);
        Assert.False((await NodoAsync(c.Id)).Archiviata);
    }

    [Fact]
    public async Task ArchiviareDueVolte_NonDaErrori()
    {
        var c = await CartellaAsync(await AreaAsync("Fatture"), "Pagata", completata: true);

        await _a.Servizio.ArchiviaCartellaAsync(c.Id);
        await _a.Servizio.ArchiviaCartellaAsync(c.Id);

        Assert.True((await NodoAsync(c.Id)).Archiviata);
    }

    [Fact]
    public async Task UnaCartellaCheNonEsistePiu_DaUnMessaggio()
    {
        await Assert.ThrowsAsync<ArchivioException>(() => _a.Servizio.ArchiviaCartellaAsync(999));
        await Assert.ThrowsAsync<ArchivioException>(() => _a.Servizio.RipristinaCartellaAsync(999));
    }

    // ---------- Ripristinare ----------

    [Fact]
    public async Task Ripristinando_LaCartellaTornaNellaSuaArea_ResteCompletata()
    {
        var c = await CartellaAsync(await AreaAsync("Fatture"), "Pagata", completata: true);
        await _a.Servizio.ArchiviaCartellaAsync(c.Id);

        await _a.Servizio.RipristinaCartellaAsync(c.Id);

        var nodo = await NodoAsync(c.Id);
        Assert.False(nodo.Archiviata);
        Assert.True(nodo.Completato); // ripristinare non riapre la cartella
    }

    [Fact]
    public async Task RipristinareUnaCartellaNonArchiviata_NonFaNulla()
    {
        var c = await CartellaAsync(await AreaAsync("Fatture"), "Pagata", completata: true);

        await _a.Servizio.RipristinaCartellaAsync(c.Id);

        Assert.False((await NodoAsync(c.Id)).Archiviata);
    }

    [Fact]
    public async Task RiaprendoUnaCartellaArchiviata_TornaNellaSuaArea()
    {
        var c = await CartellaAsync(await AreaAsync("Fatture"), "Pagata", completata: true);
        await _a.Servizio.ArchiviaCartellaAsync(c.Id);
        var dettaglio = (await _a.Servizio.CaricaCartellaAsync(c.Id))!;

        var riaperta = await _a.Servizio.AggiornaCartellaAsync(c.Id, dettaglio.Dati with { Completato = false, DataCompletamento = null });

        Assert.False(riaperta.Archiviata); // nell'archivio ci sono solo cartelle completate
        Assert.False((await NodoAsync(c.Id)).Archiviata);
    }

    [Fact]
    public async Task ModificandoAltroInUnaCartellaArchiviata_RestaArchiviata()
    {
        var c = await CartellaAsync(await AreaAsync("Fatture"), "Pagata", completata: true);
        await _a.Servizio.ArchiviaCartellaAsync(c.Id);
        var dettaglio = (await _a.Servizio.CaricaCartellaAsync(c.Id))!;

        var aggiornata = await _a.Servizio.AggiornaCartellaAsync(c.Id, dettaglio.Dati with { Descrizione = "una nota" });

        Assert.True(aggiornata.Archiviata);
        Assert.Equal("una nota", aggiornata.Dati.Descrizione);
    }

    [Fact]
    public async Task RinominandoUnaCartellaArchiviata_RestaArchiviata_ESpostaLaCartellaFisica()
    {
        var c = await CartellaAsync(await AreaAsync("Fatture"), "Pagata", completata: true);
        await _a.Servizio.ArchiviaCartellaAsync(c.Id);

        await _a.Servizio.RinominaCartellaAsync(c.Id, "Pagata a settembre");

        Assert.True((await NodoAsync(c.Id)).Archiviata);
        Assert.True(Directory.Exists(_a.Fisico("Fatture", "Pagata a settembre")));
    }

    // ---------- Tutte le completate ----------

    [Fact]
    public async Task ArchiviaCompletate_DiUnArea_ArchiviaSoloLeCompletateDiQuellArea()
    {
        var fatture = await AreaAsync("Fatture");
        var inps = await AreaAsync("INPS");
        var chiusa1 = await CartellaAsync(fatture, "Chiusa 1", completata: true);
        var chiusa2 = await CartellaAsync(fatture, "Chiusa 2", completata: true);
        var aperta = await CartellaAsync(fatture, "Aperta", completata: false);
        var chiusaInps = await CartellaAsync(inps, "Chiusa INPS", completata: true);

        var n = await _a.Servizio.ArchiviaCompletateAsync(fatture);

        Assert.Equal(2, n);
        Assert.True((await NodoAsync(chiusa1.Id)).Archiviata);
        Assert.True((await NodoAsync(chiusa2.Id)).Archiviata);
        Assert.False((await NodoAsync(aperta.Id)).Archiviata);
        Assert.False((await NodoAsync(chiusaInps.Id)).Archiviata);
    }

    [Fact]
    public async Task ArchiviaCompletate_DiTutto_ArchiviaLeCompletateDiOgniArea_EContaSoloLeNuove()
    {
        var fatture = await AreaAsync("Fatture");
        var inps = await AreaAsync("INPS");
        var a = await CartellaAsync(fatture, "A", completata: true);
        var b = await CartellaAsync(inps, "B", completata: true);
        await CartellaAsync(inps, "C", completata: false);
        await _a.Servizio.ArchiviaCartellaAsync(a.Id); // già archiviata: non si ricontano

        var n = await _a.Servizio.ArchiviaCompletateAsync(null);

        Assert.Equal(1, n);
        Assert.True((await NodoAsync(b.Id)).Archiviata);
    }

    [Fact]
    public async Task ArchiviaCompletate_SenzaNulla_DaZero() =>
        Assert.Equal(0, await _a.Servizio.ArchiviaCompletateAsync(null));

    // ---------- Gli elenchi ----------

    [Fact]
    public async Task DocumentiArchiviati_SonoSoloQuelliDelleCartelleArchiviate_DellAreaOTutti()
    {
        var fatture = await AreaAsync("Fatture");
        var inps = await AreaAsync("INPS");
        var arch1 = await CartellaAsync(fatture, "Archiviata Fatture", completata: true, "a.pdf");
        var arch2 = await CartellaAsync(inps, "Archiviata INPS", completata: true, "b.pdf");
        await CartellaAsync(fatture, "Chiusa ma non archiviata", completata: true, "c.pdf");
        await CartellaAsync(fatture, "Aperta", completata: false, "d.pdf");
        await _a.Servizio.ArchiviaCartellaAsync(arch1.Id);
        await _a.Servizio.ArchiviaCartellaAsync(arch2.Id);

        Assert.Equal(["a.pdf", "b.pdf"], (await _a.Servizio.CaricaDocumentiArchiviatiAsync(null)).Select(d => d.NomeFile).Order());
        Assert.Equal(["a.pdf"], (await _a.Servizio.CaricaDocumentiArchiviatiAsync(fatture)).Select(d => d.NomeFile));
        Assert.Equal(["b.pdf"], (await _a.Servizio.CaricaDocumentiArchiviatiAsync(inps)).Select(d => d.NomeFile));
    }

    [Fact]
    public async Task GliElenchiDiRadiceEAree_ComprendonoAncheLeArchiviate()
    {
        var fatture = await AreaAsync("Fatture");
        var arch = await CartellaAsync(fatture, "Archiviata", completata: true, "a.pdf");
        await CartellaAsync(fatture, "Aperta", completata: false, "d.pdf");
        await _a.Servizio.ArchiviaCartellaAsync(arch.Id);

        Assert.Equal(["a.pdf", "d.pdf"], (await _a.Servizio.CaricaDocumentiAsync(null)).Select(d => d.NomeFile).Order());
        Assert.Equal(["a.pdf", "d.pdf"], (await _a.Servizio.CaricaDocumentiAsync(fatture)).Select(d => d.NomeFile).Order());
    }

    [Fact]
    public async Task LeScadenze_NonCambiano_LeArchiviateSonoCompletate()
    {
        var fatture = await AreaAsync("Fatture");
        var aperta = await _a.CreaCartellaInAreaAsync(fatture, "Aperta", ["x.pdf"], Oggi.AddDays(3));
        var chiusa = await CartellaAsync(fatture, "Chiusa", completata: true);
        await _a.Servizio.ArchiviaCartellaAsync(chiusa.Id);

        Assert.Equal([aperta.Id], (await _a.Servizio.CaricaScadenzeAsync()).Select(s => s.CartellaId));
    }

    [Fact]
    public async Task CompletareUnaCartellaNuova_NonLaArchiviaDaSola()
    {
        var c = await CartellaAsync(await AreaAsync("Fatture"), "Pagata", completata: true);

        Assert.False((await NodoAsync(c.Id)).Archiviata);
    }

    [Fact]
    public async Task EliminandoUnaCartellaArchiviata_SparisceConIDocumenti()
    {
        var c = await CartellaAsync(await AreaAsync("Fatture"), "Pagata", completata: true);
        await _a.Servizio.ArchiviaCartellaAsync(c.Id);

        await _a.Servizio.EliminaCartellaAsync(c.Id);

        Assert.Empty(await _a.Servizio.CaricaDocumentiArchiviatiAsync(null));
        Assert.Null(await _a.Servizio.CaricaCartellaAsync(c.Id));
    }
}

/// <summary>Un archivio creato prima di questa versione (senza la colonna) si aggiorna senza perdere nulla.</summary>
public class ArchivioCompletatiMigrazioneTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public async Task UnDatabaseVecchio_SiAggiorna_ELeCartelleEsistentiNonSonoArchiviate()
    {
        var percorso = _tmp.Combina("vecchio", "documentale.db");
        Directory.CreateDirectory(Path.GetDirectoryName(percorso)!);

        // Il database com'era prima: migrazioni fino all'indice del testo, con una cartella completata dentro.
        await using (var vecchio = new AppDbContext(ArchivioDatabase.CreaOpzioni(percorso)))
        {
            vecchio.GetService<IMigrator>().Migrate("IndiceTestoPerDocumento");
            await vecchio.Database.ExecuteSqlRawAsync("INSERT INTO Aree (Id, Nome, PercorsoRelativo, Ordine) VALUES (1, 'Fatture', 'Fatture', 0)");
            await vecchio.Database.ExecuteSqlRawAsync(
                "INSERT INTO Cartelle (Id, AreaId, Titolo, Ricorrenza, Completato, PercorsoRelativo, DataCreazione) " +
                "VALUES (7, 1, 'Pagata', 0, 1, 'Fatture/Pagata', '2026-09-01 10:00:00')");
        }

        // L'app si avvia con la nuova versione.
        await using (var nuovo = new AppDbContext(ArchivioDatabase.CreaOpzioni(percorso)))
            await nuovo.Database.MigrateAsync();

        await using var db = new AppDbContext(ArchivioDatabase.CreaOpzioni(percorso));
        var cartella = await db.Cartelle.SingleAsync();
        Assert.Equal("Pagata", cartella.Titolo);
        Assert.True(cartella.Completato);
        Assert.False(cartella.Archiviata);
        Assert.Contains(await db.Database.GetAppliedMigrationsAsync(), m => m.EndsWith("_CartelleArchiviate"));
    }

    [Fact]
    public void UnArchivioNuovo_HaLaColonna()
    {
        var percorso = ArchivioDatabase.Inizializza(_tmp.Combina("nuovo"));
        using var db = new AppDbContext(ArchivioDatabase.CreaOpzioni(percorso));

        Assert.False(db.Cartelle.Any(c => c.Archiviata));
    }
}
