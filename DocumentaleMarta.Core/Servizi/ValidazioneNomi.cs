namespace DocumentaleMarta.Core.Servizi;

/// <summary>Regole sui nomi digitati dall'utente per aree e cartelle.</summary>
public static class ValidazioneNomi
{
    public const int LunghezzaMassimaArea = 100;
    public const int LunghezzaMassimaTitolo = 200;

    /// <summary>Restituisce il messaggio d'errore, o null se il testo è valido.</summary>
    public static string? Errore(string? testo, int lunghezzaMassima)
    {
        var pulito = testo?.Trim() ?? "";
        if (pulito.Length == 0)
            return "Il nome non può essere vuoto.";
        if (pulito.Length > lunghezzaMassima)
            return $"Il nome è troppo lungo (massimo {lunghezzaMassima} caratteri).";
        return null;
    }
}
