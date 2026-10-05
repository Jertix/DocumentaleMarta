namespace DocumentaleMarta.Core.Servizi;

/// <summary>Sa leggere il testo contenuto in un certo tipo di file (PDF, Word, Excel...).</summary>
public interface IEstrattoreTesto
{
    /// <param name="estensione">In minuscolo e con il punto, es. ".pdf".</param>
    bool Supporta(string estensione);

    /// <summary>
    /// Restituisce il testo del file (vuoto se non ne contiene).
    /// </summary>
    /// <exception cref="OcrNonDisponibileException">Il file è una scansione e il riconoscimento del testo non è utilizzabile.</exception>
    Task<string> EstraiAsync(string percorsoFile, CancellationToken cancellation);
}

/// <summary>Riconoscimento del testo (OCR) da immagini e PDF scansionati.</summary>
public interface IOcr
{
    bool Disponibile { get; }

    /// <summary>Perché non è disponibile (es. manca il pacchetto della lingua), da mostrare all'utente.</summary>
    string? MotivoNonDisponibile { get; }

    /// <summary>La lingua con cui si legge il testo (es. "italiano"), se l'OCR è disponibile.</summary>
    string? Lingua { get; }

    /// <summary>Legge il testo di un'immagine o di una scansione.</summary>
    Task<string> RiconosciImmagineAsync(string percorsoFile, CancellationToken cancellation);

    /// <summary>Legge le pagine del PDF come immagini e ne riconosce il testo.</summary>
    Task<string> RiconosciPdfAsync(string percorsoFile, CancellationToken cancellation);
}

/// <summary>Serve il riconoscimento del testo (OCR) ma non è utilizzabile su questo PC.</summary>
public class OcrNonDisponibileException(string messaggio) : Exception(messaggio);

/// <summary>Chi riceve i documenti appena allegati per leggerne il testo in background.</summary>
public interface IIndicizzatore
{
    /// <summary>Mette dei documenti in coda per la lettura del testo.</summary>
    void Accoda(IEnumerable<int> documentiIds);

    /// <summary>
    /// Rimette in coda tutti i documenti di un tipo (es. ".xml"), anche quelli già letti: serve quando cambia il modo di leggerli
    /// (o si smette di leggerli) e il testo nell'indice va rifatto.
    /// </summary>
    Task RiaccodaPerEstensioneAsync(string estensione);
}

/// <param name="InCoda">Documenti ancora da leggere, compreso quello in lavorazione.</param>
/// <param name="InElaborazione">Nome del file che si sta leggendo in questo momento.</param>
/// <param name="InAttesaOcr">Documenti che richiedono l'OCR, non utilizzabile: restano da leggere al prossimo avvio.</param>
public record StatoCodaIndicizzazione(int InCoda, string? InElaborazione, int InAttesaOcr)
{
    public static readonly StatoCodaIndicizzazione Inattivo = new(0, null, 0);
}

/// <summary>Lo stato della lettura in background, per mostrarlo nella barra in fondo alla finestra.</summary>
public interface IMonitorIndicizzazione
{
    StatoCodaIndicizzazione Stato { get; }

    /// <summary>Scatta ogni volta che lo stato cambia, su un thread qualsiasi.</summary>
    event Action? Cambiato;
}
