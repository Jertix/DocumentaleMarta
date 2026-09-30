using DocumentaleMarta.Core.Modelli;

namespace DocumentaleMarta.Core.Servizi;

/// <param name="Documento">Il documento trovato, con cartella e area.</param>
/// <param name="Trovato">
/// Dove e cosa è stato trovato: un estratto del testo con le parole cercate tra <see cref="RisultatoRicerca.InizioEvidenza"/>
/// e <see cref="RisultatoRicerca.FineEvidenza"/>, oppure (se è stato trovato nei nomi) "Nel nome del file" e simili.
/// </param>
public record RisultatoRicerca(DocumentoElenco Documento, string Trovato)
{
    public const char InizioEvidenza = '\u0001';
    public const char FineEvidenza = '\u0002';
}

/// <param name="Troncato">Ci sono altri risultati oltre a quelli elencati: conviene restringere la ricerca.</param>
public record EsitoRicerca(IReadOnlyList<RisultatoRicerca> Risultati, bool Troncato)
{
    public static readonly EsitoRicerca Vuoto = new([], false);
}

public interface IRicercaService
{
    /// <summary>
    /// Cerca i documenti che contengono TUTTE le parole scritte, ciascuna in almeno uno di: nome del file, titolo o descrizione
    /// della cartella, nome dell'area, testo del documento. Maiuscole e accenti non contano.
    /// </summary>
    Task<EsitoRicerca> CercaAsync(string testo, int massimo = 1000);
}
