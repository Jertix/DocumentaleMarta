namespace DocumentaleMarta.Core.Modelli;

/// <summary>I tipi di file tra cui scegliere nella ricerca avanzata. Si possono combinare: PDF e Word insieme.</summary>
[Flags]
public enum CategoriaFile
{
    /// <summary>Nessuna scelta: tutti i tipi.</summary>
    Nessuna = 0,
    Pdf = 1,
    Word = 2,
    Excel = 4,
    Immagini = 8,

    /// <summary>Tutto ciò che non rientra negli altri (testo semplice, PowerPoint, file sconosciuti...).</summary>
    Altro = 16
}

public static class CategorieFile
{
    private static readonly HashSet<string> Word = [".doc", ".docx", ".dot", ".dotx", ".odt", ".rtf"];
    private static readonly HashSet<string> Excel = [".xls", ".xlsx", ".xlsm", ".ods", ".csv"];
    private static readonly HashSet<string> Immagini =
        [".jpg", ".jpeg", ".jpe", ".png", ".tif", ".tiff", ".bmp", ".gif", ".webp", ".heic"];

    /// <param name="estensione">Con il punto, in qualsiasi forma di maiuscole/minuscole (es. ".PDF").</param>
    public static CategoriaFile Di(string? estensione)
    {
        var e = (estensione ?? "").ToLowerInvariant();
        if (e == ".pdf") return CategoriaFile.Pdf;
        if (Word.Contains(e)) return CategoriaFile.Word;
        if (Excel.Contains(e)) return CategoriaFile.Excel;
        if (Immagini.Contains(e)) return CategoriaFile.Immagini;
        return CategoriaFile.Altro;
    }
}

public enum StatoCartella
{
    Qualsiasi = 0,

    /// <summary>La cartella non è completata.</summary>
    Aperta = 1,

    Completata = 2
}

/// <summary>
/// I filtri della ricerca avanzata. Si combinano tutti tra loro (devono valere tutti insieme) e con le parole cercate.
/// </summary>
/// <param name="Categorie">I tipi di file ammessi; nessuna scelta = tutti.</param>
/// <param name="AreaId">Solo i documenti di quest'area.</param>
/// <param name="ScadenzaDal">Solo cartelle che hanno una scadenza da questa data in poi (compresa).</param>
/// <param name="ScadenzaAl">Solo cartelle che hanno una scadenza fino a questa data (compresa).</param>
/// <param name="CercaNelContenuto">Se falso le parole si cercano solo nei nomi, non dentro i documenti.</param>
public record FiltriRicerca(
    CategoriaFile Categorie = CategoriaFile.Nessuna,
    int? AreaId = null,
    StatoCartella Stato = StatoCartella.Qualsiasi,
    DateOnly? ScadenzaDal = null,
    DateOnly? ScadenzaAl = null,
    bool CercaNelContenuto = true)
{
    /// <summary>C'è almeno un filtro che restringe i documenti (l'interruttore sul contenuto non conta).</summary>
    public bool HaFiltri =>
        Categorie != CategoriaFile.Nessuna || AreaId is not null || Stato != StatoCartella.Qualsiasi
        || ScadenzaDal is not null || ScadenzaAl is not null;
}
