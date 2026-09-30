namespace DocumentaleMarta.Core.Modelli;

/// <summary>Cartella creata dall'utente dentro un'area. Corrisponde a una sottocartella dell'area.</summary>
public class Cartella
{
    public int Id { get; set; }
    public int AreaId { get; set; }
    public Area Area { get; set; } = null!;

    public string Titolo { get; set; } = "";
    public string? Descrizione { get; set; }
    public DateOnly? DataScadenza { get; set; }
    public Ricorrenza Ricorrenza { get; set; }
    public bool Completato { get; set; }
    public DateOnly? DataCompletamento { get; set; }

    public string PercorsoRelativo { get; set; } = "";
    public DateTime DataCreazione { get; set; }

    public List<Documento> Documenti { get; set; } = [];
}
