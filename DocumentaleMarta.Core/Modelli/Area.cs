namespace DocumentaleMarta.Core.Modelli;

/// <summary>Ramo principale dell'albero (Fatture, INPS...). Corrisponde a una sottocartella della radice.</summary>
public class Area
{
    public int Id { get; set; }
    public string Nome { get; set; } = "";
    public string PercorsoRelativo { get; set; } = "";
    public int Ordine { get; set; }

    public List<Cartella> Cartelle { get; set; } = [];
}
