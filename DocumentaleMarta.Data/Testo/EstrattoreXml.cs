using System.Text;
using System.Xml;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Data.Testo;

/// <summary>
/// File XML (.xml). Come si leggono lo decidono le impostazioni (<see cref="ModoRicercaXml"/>), chieste a ogni lettura:
/// <list type="bullet">
/// <item>solo testo: i valori degli elementi, i blocchi CDATA e i valori degli attributi, non i nomi dei tag (cercando «Rossi» si
/// trova il documento, cercando «LogEntry» no); se l'XML è rovinato si legge come testo semplice, così resta ricercabile;</item>
/// <item>tutto il file: il testo com'è scritto, tag compresi;</item>
/// <item>ricerca spenta: l'estensione non è supportata, quindi un XML non si legge e nell'indice non c'è.</item>
/// </list>
/// </summary>
/// <param name="modo">
/// Come leggere gli XML adesso (null = la ricerca nei file XML è spenta); senza, si leggono sempre come solo testo.
/// </param>
public class EstrattoreXml(Func<ModoRicercaXml?>? modo = null) : IEstrattoreTesto
{
    /// <summary>Si smette di leggere dopo tanto testo: l'indice ne tiene comunque solo una parte.</summary>
    private const int CaratteriMassimi = 2_000_000;

    /// <summary>Un XML fatto quasi solo di tag può essere enorme: dopo questi byte letti si smette.</summary>
    private const long ByteMassimi = 50L * 1024 * 1024;

    private readonly EstrattoreTestoSemplice _testoSemplice = new();

    /// <summary>Come si leggono gli XML in questo momento; null se la ricerca nei file XML è spenta.</summary>
    private ModoRicercaXml? ModoAttuale() => modo is null ? ModoRicercaXml.SoloTesto : modo();

    /// <summary>Vero per .xml, ma solo se la ricerca nei file XML è attiva.</summary>
    public bool Supporta(string estensione) => estensione == ".xml" && ModoAttuale() is not null;

    /// <summary>
    /// Legge il testo dell'XML in background, nel modo scelto nelle impostazioni (tutto il file, oppure solo il testo; se non è
    /// un XML valido lo legge come testo semplice).
    /// </summary>
    public async Task<string> EstraiAsync(string percorsoFile, CancellationToken cancellation)
    {
        switch (ModoAttuale())
        {
            // Spenta mentre il documento aspettava in coda: non si legge nulla (le impostazioni appena salvate
            // fanno rimettere in coda gli XML e allora vengono tolti dall'indice).
            case null:
                return "";
            case ModoRicercaXml.TuttoIlFile:
                return await _testoSemplice.EstraiAsync(percorsoFile, cancellation);
        }

        try
        {
            return await Task.Run(() => Estrai(percorsoFile, cancellation), cancellation);
        }
        catch (XmlException)
        {
            return await _testoSemplice.EstraiAsync(percorsoFile, cancellation);
        }
    }

    /// <summary>
    /// Scorre l'XML un pezzo alla volta (senza caricarlo tutto in memoria) e raccoglie il testo. Lancia
    /// <see cref="XmlException"/> se il file non è un XML valido.
    /// </summary>
    private static string Estrai(string percorsoFile, CancellationToken cancellation)
    {
        // Condivisione in lettura e scrittura: il file può essere aperto in un altro programma nello stesso momento.
        using var flusso = new FileStream(percorsoFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        // Le definizioni di tipo (DTD) si ignorano e non si va a cercare nulla fuori dal file; la codifica (UTF-8, ISO-8859-1...)
        // la riconosce da sola la lettura.
        var impostazioni = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true
        };
        using var lettore = XmlReader.Create(flusso, impostazioni);

        var testo = new StringBuilder();
        while (testo.Length < CaratteriMassimi && flusso.Position < ByteMassimi && lettore.Read())
        {
            cancellation.ThrowIfCancellationRequested();

            switch (lettore.NodeType)
            {
                case XmlNodeType.Element when lettore.HasAttributes:
                    while (lettore.MoveToNextAttribute())
                        if (!EDichiarazioneDiNamespace(lettore))
                            Aggiungi(testo, lettore.Value);
                    lettore.MoveToElement();
                    break;

                case XmlNodeType.Text or XmlNodeType.CDATA:
                    Aggiungi(testo, lettore.Value);
                    break;
            }
        }

        return testo.ToString().Trim();
    }

    /// <summary>L'attributo è una dichiarazione di namespace (xmlns="…" o xmlns:p="…"): sono indirizzi, non testo del documento.</summary>
    private static bool EDichiarazioneDiNamespace(XmlReader lettore) =>
        lettore.Prefix == "xmlns" || lettore.Name == "xmlns";

    /// <summary>Aggiunge un valore al testo raccolto, separato dagli altri da uno spazio (i valori vuoti si saltano).</summary>
    private static void Aggiungi(StringBuilder testo, string valore)
    {
        if (string.IsNullOrWhiteSpace(valore))
            return;

        if (testo.Length > 0)
            testo.Append(' ');
        testo.Append(valore.Trim());
    }
}
