using System.Text.Json;
using System.Text.Json.Serialization;

namespace DocumentaleMarta.Core.Impostazioni;

/// <summary>Con che colori si vede il programma.</summary>
public enum TemaApp
{
    /// <summary>Chiaro o scuro secondo le impostazioni di Windows (e cambia con loro).</summary>
    ComeWindows,

    Chiaro,

    Scuro
}

/// <summary>
/// Il colore principale del programma: quello dei pulsanti principali, delle spunte, della selezione e dei bordi attivi.
/// Il primo è quello predefinito (anche per i valori sconosciuti nel file delle impostazioni).
/// </summary>
public enum ColoreApp
{
    /// <summary>Un blu freddo, da metalli.</summary>
    Acciaio,

    /// <summary>Un arancio caldo, come il metallo in forgia.</summary>
    Fucina,

    Foresta,

    Prugna,

    Grafite,

    /// <summary>Il colore che l'utente ha scelto in Windows.</summary>
    ComeWindows
}

/// <summary>
/// Le scelte della sezione «Aspetto» delle impostazioni: tema chiaro o scuro, colore principale e se le finestre
/// hanno uno sfondo con una leggera tinta di quel colore.
/// </summary>
public record AspettoApp(TemaApp Tema, ColoreApp Colore = ColoreApp.Acciaio, bool SfondoColorato = false);

/// <summary>
/// Salva un'enumerazione come testo leggibile («Scuro») e, se nel file c'è un valore che non esiste più (o scritto male a mano),
/// usa quello predefinito invece di impedire l'avvio del programma: l'aspetto non vale un errore.
/// </summary>
public sealed class EnumTolleranteConverter<T> : JsonConverter<T> where T : struct, Enum
{
    /// <summary>
    /// Legge il valore dal file: accetta il nome scritto come testo (maiuscole non contano); se il valore non esiste o è
    /// strano usa quello predefinito.
    /// </summary>
    public override T Read(ref Utf8JsonReader reader, Type tipo, JsonSerializerOptions opzioni)
    {
        if (reader.TokenType == JsonTokenType.String
            && Enum.TryParse<T>(reader.GetString()?.Trim(), ignoreCase: true, out var valore)
            && Enum.IsDefined(valore))
            return valore;

        reader.Skip();
        return default;
    }

    /// <summary>Scrive il valore nel file come testo leggibile (per esempio «Scuro»).</summary>
    public override void Write(Utf8JsonWriter writer, T valore, JsonSerializerOptions opzioni) =>
        writer.WriteStringValue(valore.ToString());
}
