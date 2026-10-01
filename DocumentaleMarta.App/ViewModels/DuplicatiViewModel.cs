using DocumentaleMarta.Core.Modelli;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>Una riga della finestra dei duplicati: il file scelto e dove ne esiste già una copia.</summary>
public record RigaDuplicato(string NomeFile, string Dove);

/// <summary>Il testo della finestra "File già presenti nell'archivio".</summary>
public class DuplicatiViewModel
{
    /// <summary>Quante copie già archiviate si elencano per file; le altre si riassumono ("e altre 2").</summary>
    private const int CopieMostratePerFile = 3;

    public DuplicatiViewModel(IReadOnlyList<DuplicatoTrovato> duplicati, int totaleFile)
    {
        var tutti = duplicati.Count >= totaleFile;
        PuoSaltare = !tutti;

        Intestazione = (duplicati.Count, tutti) switch
        {
            (1, true) => $"Il file «{duplicati[0].NomeFile}» è già nell'archivio.",
            (_, true) => $"Questi {duplicati.Count} file sono già nell'archivio.",
            (1, false) => $"1 dei {totaleFile} file scelti è già nell'archivio.",
            _ => $"{duplicati.Count} dei {totaleFile} file scelti sono già nell'archivio."
        };
        Domanda = duplicati.Count == 1 && tutti ? "Vuoi allegarlo comunque?" : "Vuoi allegarli comunque?";

        Righe = duplicati.Select(d => new RigaDuplicato(d.NomeFile, Dove(d.GiaArchiviati))).ToList();
    }

    public string Intestazione { get; }
    public string Domanda { get; }
    public IReadOnlyList<RigaDuplicato> Righe { get; }

    /// <summary>Si può allegare solo ciò che è nuovo: ha senso solo se tra i file scelti ce n'è almeno uno nuovo.</summary>
    public bool PuoSaltare { get; }

    /// <summary>Se tutti i file sono duplicati, "salta" e "annulla" sono la stessa cosa: resta un solo pulsante.</summary>
    public string TestoRifiuto => PuoSaltare ? "Annulla" : "Non allegare";

    private static string Dove(IReadOnlyList<DocumentoGiaArchiviato> copie)
    {
        var righe = copie.Take(CopieMostratePerFile).Select(c => $"{c.NomeArea} › {c.TitoloCartella}").ToList();
        var altre = copie.Count - righe.Count;
        var elenco = string.Join("; ", righe);
        return altre > 0 ? $"Già in: {elenco}; e altre {altre}" : $"Già in: {elenco}";
    }
}
