namespace DocumentaleMarta.Core.Modelli;

/// <summary>Un documento con i dati della cartella e dell'area a cui appartiene, per le griglie di radice e aree.</summary>
public record DocumentoElenco(
    int Id,
    string NomeFile,
    string Estensione,
    long Dimensione,
    DateTime DataCaricamento,
    string PercorsoRelativo,
    int CartellaId,
    string TitoloCartella,
    int AreaId,
    string NomeArea,
    DateOnly? ScadenzaCartella,
    bool CartellaCompletata);
