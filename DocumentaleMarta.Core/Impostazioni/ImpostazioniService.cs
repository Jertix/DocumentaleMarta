using System.Text.Encodings.Web;
using System.Text.Json;

namespace DocumentaleMarta.Core.Impostazioni;

/// <summary>Legge e scrive <c>impostazioni.json</c>. Se il file manca lo crea con i valori predefiniti.</summary>
public class ImpostazioniService(string? percorsoFile = null)
{
    private static readonly JsonSerializerOptions OpzioniJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping // lascia leggibili le lettere accentate
    };

    public string PercorsoFile { get; } = percorsoFile ?? PercorsoPredefinito();

    public const string VariabileAmbientePercorso = "DOCUMENTALE_MARTA_IMPOSTAZIONI";

    /// <summary>%AppData%\DocumentaleMarta\impostazioni.json, salvo che la variabile d'ambiente indichi un altro file (utile per le prove).</summary>
    public static string PercorsoPredefinito()
    {
        var personalizzato = Environment.GetEnvironmentVariable(VariabileAmbientePercorso);
        if (!string.IsNullOrWhiteSpace(personalizzato))
            return personalizzato;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DocumentaleMarta",
            "impostazioni.json");
    }

    /// <exception cref="InvalidDataException">Il file esiste ma non è un JSON valido.</exception>
    public ImpostazioniApp Carica()
    {
        if (!File.Exists(PercorsoFile))
        {
            var predefinite = new ImpostazioniApp();
            Salva(predefinite);
            return predefinite;
        }

        try
        {
            var json = File.ReadAllText(PercorsoFile);
            return JsonSerializer.Deserialize<ImpostazioniApp>(json, OpzioniJson)
                   ?? throw new InvalidDataException($"Il file delle impostazioni è vuoto: {PercorsoFile}");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"Il file delle impostazioni non è valido ({PercorsoFile}): {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Scrive le impostazioni nel file (creando la cartella se serve): prima in un file temporaneo e poi lo sostituisce,
    /// così un arresto a metà non lascia un file rovinato.
    /// </summary>
    public void Salva(ImpostazioniApp impostazioni)
    {
        var cartella = Path.GetDirectoryName(PercorsoFile);
        if (!string.IsNullOrEmpty(cartella))
            Directory.CreateDirectory(cartella);

        // Scrittura su file temporaneo e poi sostituzione, così un arresto a metà non lascia un file troncato.
        var temporaneo = PercorsoFile + ".tmp";
        File.WriteAllText(temporaneo, JsonSerializer.Serialize(impostazioni, OpzioniJson));
        File.Move(temporaneo, PercorsoFile, overwrite: true);
    }
}
