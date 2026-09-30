namespace DocumentaleMarta.Core.Servizi;

/// <summary>Titolo proposto quando si allegano documenti a una cartella senza titolo.</summary>
public static class TitoloAutomatico
{
    /// <summary>Un file: il nome senza estensione. Più file: "Primo (+N)", con N = file restanti.</summary>
    public static string Genera(IReadOnlyList<string> nomiFile)
    {
        if (nomiFile.Count == 0)
            return "";

        var primo = Path.GetFileNameWithoutExtension(nomiFile[0]);
        return nomiFile.Count == 1 ? primo : $"{primo} (+{nomiFile.Count - 1})";
    }
}
