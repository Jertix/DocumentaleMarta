namespace DocumentaleMarta.Core.Modelli;

/// <summary>File allegato a una cartella.</summary>
public class Documento
{
    public int Id { get; set; }
    public int CartellaId { get; set; }
    public Cartella Cartella { get; set; } = null!;

    public string NomeFile { get; set; } = "";
    public string PercorsoRelativo { get; set; } = "";
    public string Estensione { get; set; } = "";
    public long Dimensione { get; set; }
    public DateTime DataCaricamento { get; set; }

    /// <summary>SHA-256 del contenuto, in esadecimale. Serve a riconoscere i duplicati.</summary>
    public string Hash { get; set; } = "";

    public StatoIndicizzazione StatoIndicizzazione { get; set; }
}
