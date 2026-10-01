namespace DocumentaleMarta.Core.Modelli;

/// <summary>
/// I formati di OpenOffice e LibreOffice (OpenDocument): testi, fogli di calcolo, presentazioni e disegni, con i loro modelli.
/// Sono archivi ZIP con dentro un file <c>content.xml</c>. La lista è una sola, usata da chi legge il testo,
/// dall'anteprima e dai filtri della ricerca.
/// </summary>
public static class FormatiOpenDocument
{
    public static readonly IReadOnlyList<string> Testo = [".odt", ".ott"];
    public static readonly IReadOnlyList<string> FoglioDiCalcolo = [".ods", ".ots"];
    public static readonly IReadOnlyList<string> Presentazione = [".odp", ".otp"];
    public static readonly IReadOnlyList<string> Disegno = [".odg", ".otg"];

    public static readonly IReadOnlyList<string> Tutti = [.. Testo, .. FoglioDiCalcolo, .. Presentazione, .. Disegno];

    /// <summary>Dove il programma salva la miniatura della prima pagina, dentro l'archivio ZIP.</summary>
    public const string PercorsoMiniatura = "Thumbnails/thumbnail.png";

    /// <param name="estensione">Con il punto, in qualsiasi forma di maiuscole/minuscole.</param>
    public static bool Contiene(string? estensione) =>
        estensione is not null && Tutti.Contains(estensione, StringComparer.OrdinalIgnoreCase);
}
