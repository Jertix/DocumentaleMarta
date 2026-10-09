using System.Globalization;

namespace DocumentaleMarta.Core.Modelli;

/// <summary>Un'icona che si può dare a un'area: il codice salvato nel database e il nome con cui la si descrive.</summary>
/// <param name="Codice">Il numero del simbolo nel font delle icone di Windows, in esadecimale (es. «E8F1»).</param>
/// <param name="Descrizione">Il nome in italiano: serve da suggerimento e ai programmi per ipovedenti.</param>
public record IconaArea(string Codice, string Descrizione)
{
    /// <summary>Il simbolo vero e proprio (un solo carattere del font delle icone).</summary>
    public string Glifo => char.ConvertFromUtf32(int.Parse(Codice, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
}

/// <summary>
/// Le icone tra cui scegliere per un'area. È un elenco chiuso di simboli di Segoe Fluent Icons / Segoe MDL2 Assets
/// (presenti in entrambi i font, così si vedono uguali anche su Windows 10): nel database si salva solo il codice.
/// Un'area senza icona (o con un codice sconosciuto, per esempio da un backup di una versione futura) usa la <see cref="Predefinita"/>.
/// </summary>
public static class IconeArea
{
    /// <summary>L'icona delle aree che non ne hanno scelta una: la libreria.</summary>
    public const string Predefinita = "E8F1";

    /// <summary>Tutte le icone proposte, nell'ordine in cui compaiono nella finestra di scelta (nove per riga).</summary>
    public static IReadOnlyList<IconaArea> Disponibili { get; } =
    [
        // Documenti e pratiche
        new("E8F1", "Libreria"), new("E8A5", "Documento"), new("EADF", "Controlli e verifiche"),
        new("E9F9", "Rapporti"), new("EB95", "Certificati"), new("E736", "Manuali"),
        new("E723", "Allegati"), new("E8EC", "Listini ed etichette"), new("E81E", "Pratiche"),

        // Conti e acquisti
        new("E8EF", "Contabilità"), new("E94C", "Tasse e IVA"), new("E8C7", "Pagamenti"),
        new("E825", "Banca ed enti"), new("E9D2", "Bilanci"), new("EB05", "Statistiche"),
        new("EAFC", "Andamento"), new("E7BF", "Acquisti"), new("E719", "Ordini"),

        // Persone e azienda
        new("E716", "Personale"), new("E77B", "Persona"), new("E731", "Sede"),
        new("E821", "Lavoro"), new("E7BE", "Formazione"), new("E8F2", "Comunicazioni"),
        new("E715", "Posta"), new("E717", "Telefono"), new("E774", "Internet"),

        // Officina, mezzi e sicurezza
        new("E822", "Cantieri"), new("E90F", "Manutenzione"), new("E912", "Attrezzature"),
        new("E713", "Macchinari"), new("EB3C", "Progetti e disegni"), new("EA5E", "Automezzi"),
        new("EA18", "Sicurezza"), new("E72E", "Riservato"), new("E95E", "Salute"),

        // Varie
        new("E82F", "Energia e utenze"), new("E734", "Importanti"), new("E7C1", "Da seguire"),
        new("E8D7", "Licenze e chiavi"), new("EB44", "Obiettivi"), new("E707", "Luoghi"),
        new("E722", "Foto"), new("E8AE", "Presentazioni"), new("E749", "Stampe")
    ];

    /// <summary>True se il codice è uno di quelli proposti (senza badare alle maiuscole).</summary>
    public static bool EValida(string? codice) => Trova(codice) is not null;

    /// <summary>
    /// L'icona col codice indicato; se il codice manca o non è tra quelli proposti, quella predefinita
    /// (così un dato strano nel database non impedisce di aprire l'archivio).
    /// </summary>
    public static IconaArea Risolvi(string? codice) =>
        Trova(codice) ?? Trova(Predefinita) ?? Disponibili[0];

    /// <summary>Il simbolo di un'icona, o quello dell'icona predefinita se il codice non è valido.</summary>
    public static string Glifo(string? codice) => Risolvi(codice).Glifo;

    /// <summary>
    /// Il codice da salvare per l'icona scelta: null per quella predefinita (come per un'area che non ne ha mai scelta una),
    /// altrimenti il codice in maiuscolo. Un codice non valido dà null.
    /// </summary>
    public static string? DaSalvare(string? codice) =>
        Trova(codice) is { } icona && icona.Codice != Predefinita ? icona.Codice : null;

    private static IconaArea? Trova(string? codice) =>
        string.IsNullOrWhiteSpace(codice)
            ? null
            : Disponibili.FirstOrDefault(i => string.Equals(i.Codice, codice.Trim(), StringComparison.OrdinalIgnoreCase));
}
