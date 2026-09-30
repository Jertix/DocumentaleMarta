using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

public class TitoloAutomaticoTests
{
    [Fact]
    public void NessunFile_TitoloVuoto() =>
        Assert.Equal("", TitoloAutomatico.Genera([]));

    [Fact]
    public void UnFile_NomeSenzaEstensione() =>
        Assert.Equal("Fattura 123", TitoloAutomatico.Genera(["Fattura 123.pdf"]));

    [Fact]
    public void PiuFile_PrimoPiuNumeroDeiRestanti() =>
        Assert.Equal("Fattura 123 (+2)", TitoloAutomatico.Genera(["Fattura 123.pdf", "Fattura 124.pdf", "scan.jpg"]));

    [Fact]
    public void UsaSoloIlNomeDelFile_NonIlPercorso() =>
        Assert.Equal("Fattura 123", TitoloAutomatico.Genera([@"C:\Scansioni\Fattura 123.pdf"]));
}
