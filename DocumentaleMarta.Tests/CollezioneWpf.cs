namespace DocumentaleMarta.Tests;

/// <summary>
/// I test che costruiscono viste WPF o leggono le risorse incorporate girano uno dopo l'altro: il caricamento delle
/// risorse (<c>System.IO.Packaging</c>) non è pensato per più thread insieme, e l'ascolto degli errori di binding
/// è globale, quindi un test che li provoca apposta farebbe fallire gli altri. Nel programma c'è un solo thread
/// dell'interfaccia: il problema esiste solo nei test.
/// </summary>
[CollectionDefinition("WPF")]
public class CollezioneWpf;
