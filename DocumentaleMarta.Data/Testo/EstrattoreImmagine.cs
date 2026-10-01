using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Data.Testo;

/// <summary>Foto e scansioni (JPG, PNG, TIFF...): non contengono testo vero, si legge con l'OCR.</summary>
public class EstrattoreImmagine(IOcr? ocr = null) : IEstrattoreTesto
{
    private static readonly HashSet<string> Estensioni = [".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".gif"];

    /// <summary>Vero per i formati di immagine (JPG, PNG, TIFF...).</summary>
    public bool Supporta(string estensione) => Estensioni.Contains(estensione);

    /// <summary>
    /// Legge il testo dell'immagine con il riconoscimento del testo; se non è disponibile lo segnala (il documento resterà
    /// da leggere).
    /// </summary>
    public Task<string> EstraiAsync(string percorsoFile, CancellationToken cancellation) =>
        ocr is { Disponibile: true }
            ? ocr.RiconosciImmagineAsync(percorsoFile, cancellation)
            : throw new OcrNonDisponibileException(ocr?.MotivoNonDisponibile ?? "Il riconoscimento del testo (OCR) non è disponibile.");
}
