using System.IO;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>Chi può essere trascinato da una griglia verso una cartella dell'albero: un documento e la cartella in cui si trova.</summary>
public interface IOrigineDocumento
{
    int DocumentoId { get; }
    int CartellaOrigineId { get; }
}

/// <summary>Il documento che sta viaggiando durante un trascinamento.</summary>
public record DocumentoTrascinato(int DocumentoId, int CartellaOrigineId);

public static class Trascinamento
{
    /// <summary>Il nome con cui i documenti trascinati si trovano nei dati del trascinamento.</summary>
    public const string FormatoDocumento = "DocumentaleMarta.Documento";

    /// <summary>
    /// Tra gli elementi trascinati da Esplora file tiene solo i file: le cartelle non si possono allegare.
    /// </summary>
    /// <returns>I file, e quante cartelle sono state escluse.</returns>
    public static (IReadOnlyList<string> File, int CartelleEscluse) SoloFile(IEnumerable<string> percorsi)
    {
        var file = new List<string>();
        var escluse = 0;
        foreach (var percorso in percorsi)
        {
            if (Directory.Exists(percorso))
                escluse++;
            else
                file.Add(percorso);
        }
        return (file, escluse);
    }

    public static string MessaggioCartelleEscluse(int cartelle) =>
        cartelle == 1
            ? "Una cartella non è stata allegata: si possono allegare solo file. Se vuoi i file che contiene, aprila e trascina quelli."
            : $"{cartelle} cartelle non sono state allegate: si possono allegare solo file. Se vuoi i file che contengono, aprile e trascina quelli.";
}
