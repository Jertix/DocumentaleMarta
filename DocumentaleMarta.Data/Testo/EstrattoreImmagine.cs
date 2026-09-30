using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Data.Testo;

/// <summary>Foto e scansioni (JPG, PNG, TIFF...): non contengono testo vero, si legge con l'OCR.</summary>
public class EstrattoreImmagine(IOcr? ocr = null) : IEstrattoreTesto
{
    private static readonly HashSet<string> Estensioni = [".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".gif"];

    public bool Supporta(string estensione) => Estensioni.Contains(estensione);

    public Task<string> EstraiAsync(string percorsoFile, CancellationToken cancellation) =>
        ocr is { Disponibile: true }
            ? ocr.RiconosciImmagineAsync(percorsoFile, cancellation)
            : throw new OcrNonDisponibileException(ocr?.MotivoNonDisponibile ?? "Il riconoscimento del testo (OCR) non è disponibile.");
}
