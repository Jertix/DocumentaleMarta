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

/// <summary>Le scelte della sezione «Aspetto» delle impostazioni.</summary>
public record AspettoApp(TemaApp Tema);

/// <summary>
/// Salva un'enumerazione come testo leggibile («Scuro») e, se nel file c'è un valore che non esiste più (o scritto male a mano),
/// usa quello predefinito invece di impedire l'avvio del programma: l'aspetto non vale un errore.
/// </summary>
public sealed class EnumTolleranteConverter<T> : JsonConverter<T> where T : struct, Enum
{
    public override T Read(ref Utf8JsonReader reader, Type tipo, JsonSerializerOptions opzioni)
    {
        if (reader.TokenType == JsonTokenType.String
            && Enum.TryParse<T>(reader.GetString()?.Trim(), ignoreCase: true, out var valore)
            && Enum.IsDefined(valore))
            return valore;

        reader.Skip();
        return default;
    }

    public override void Write(Utf8JsonWriter writer, T valore, JsonSerializerOptions opzioni) =>
        writer.WriteStringValue(valore.ToString());
}
