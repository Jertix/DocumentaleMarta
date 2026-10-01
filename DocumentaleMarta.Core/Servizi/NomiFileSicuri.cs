using System.Text;

namespace DocumentaleMarta.Core.Servizi;

/// <summary>Rende utilizzabili come nomi di file o cartelle di Windows i testi scelti dall'utente.</summary>
public static class NomiFileSicuri
{
    public const string NomePredefinito = "Senza nome";

    private static readonly char[] CaratteriNonValidi = BuildCaratteriNonValidi();
    private static readonly HashSet<string> NomiRiservati = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>Pulisce il nome di una cartella: niente caratteri vietati, punti o spazi finali, nomi riservati.</summary>
    public static string PulisciNomeCartella(string? nome, int lunghezzaMassima = 80)
    {
        var pulito = Sostituisci(nome).Trim().TrimEnd('.', ' ');
        if (pulito.Length > lunghezzaMassima)
            pulito = pulito[..lunghezzaMassima].TrimEnd('.', ' ');
        if (pulito.Length == 0)
            return NomePredefinito;
        return NomiRiservati.Contains(pulito) ? "_" + pulito : pulito;
    }

    /// <summary>Come <see cref="PulisciNomeCartella"/> ma conserva l'estensione del file.</summary>
    public static string PulisciNomeFile(string? nomeFile, int lunghezzaMassima = 120)
    {
        var grezzo = Sostituisci(nomeFile).Trim();
        var estensione = Path.GetExtension(grezzo);
        var radice = Path.GetFileNameWithoutExtension(grezzo);

        // Un'estensione lunghissima non è un'estensione: la si tratta come parte del nome.
        if (estensione.Length > 16)
        {
            radice = grezzo;
            estensione = "";
        }

        var spazio = Math.Max(1, lunghezzaMassima - estensione.Length);
        if (radice.Length > spazio)
            radice = radice[..spazio];
        radice = radice.TrimEnd('.', ' ');

        if (radice.Length == 0)
            radice = NomePredefinito;
        else if (NomiRiservati.Contains(radice))
            radice = "_" + radice;

        return radice + estensione;
    }

    /// <summary>
    /// Se <paramref name="esiste"/> dice che il nome è già occupato, aggiunge un suffisso "(1)", "(2)"...
    /// prima dell'estensione fino a trovarne uno libero.
    /// </summary>
    public static string RendiUnivoco(string nome, Func<string, bool> esiste)
    {
        if (!esiste(nome))
            return nome;

        var estensione = Path.GetExtension(nome);
        var radice = Path.GetFileNameWithoutExtension(nome);
        if (estensione.Length > 16)
        {
            radice = nome;
            estensione = "";
        }

        for (var n = 1; ; n++)
        {
            var candidato = $"{radice} ({n}){estensione}";
            if (!esiste(candidato))
                return candidato;
        }
    }

    /// <summary>
    /// Sostituisce con un trattino basso i caratteri che Windows non ammette nei nomi di file e cartelle.
    /// </summary>
    private static string Sostituisci(string? testo)
    {
        if (string.IsNullOrEmpty(testo))
            return "";

        var sb = new StringBuilder(testo.Length);
        foreach (var c in testo)
            sb.Append(Array.IndexOf(CaratteriNonValidi, c) >= 0 ? '_' : c);
        return sb.ToString();
    }

    /// <summary>L'elenco dei caratteri vietati nei nomi (quelli di Windows più alcuni che creano confusione).</summary>
    private static char[] BuildCaratteriNonValidi()
    {
        var caratteri = new HashSet<char>(Path.GetInvalidFileNameChars());
        foreach (var c in "<>:\"/\\|?*")
            caratteri.Add(c);
        return [.. caratteri];
    }
}
