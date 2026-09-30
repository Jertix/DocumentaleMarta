using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

public class NomiFileSicuriTests
{
    [Theory]
    [InlineData("Fatture", "Fatture")]
    [InlineData("Fattura 12/2026", "Fattura 12_2026")]
    [InlineData("a<b>c:d\"e|f?g*h", "a_b_c_d_e_f_g_h")]
    [InlineData("  Agenzia Entrate  ", "Agenzia Entrate")]
    [InlineData("Nome finale.", "Nome finale")]
    [InlineData("Nome finale . . ", "Nome finale")]
    [InlineData("", "Senza nome")]
    [InlineData("   ", "Senza nome")]
    [InlineData("...", "Senza nome")]
    [InlineData("CON", "_CON")]
    [InlineData("com1", "_com1")]
    [InlineData("Società èàù", "Società èàù")]
    public void PulisciNomeCartella_Casi(string input, string atteso) =>
        Assert.Equal(atteso, NomiFileSicuri.PulisciNomeCartella(input));

    [Fact]
    public void PulisciNomeCartella_Null_DaNomePredefinito() =>
        Assert.Equal("Senza nome", NomiFileSicuri.PulisciNomeCartella(null));

    [Fact]
    public void PulisciNomeCartella_TroppoLungo_VieneTroncato()
    {
        var risultato = NomiFileSicuri.PulisciNomeCartella(new string('a', 200), lunghezzaMassima: 50);
        Assert.Equal(50, risultato.Length);
    }

    [Theory]
    [InlineData("fattura.pdf", "fattura.pdf")]
    [InlineData("fattura 1/2.pdf", "fattura 1_2.pdf")]
    [InlineData("relazione.tar.gz", "relazione.tar.gz")]
    [InlineData("CON.txt", "_CON.txt")]
    [InlineData("", "Senza nome")]
    [InlineData(".pdf", "Senza nome.pdf")]
    public void PulisciNomeFile_Casi(string input, string atteso) =>
        Assert.Equal(atteso, NomiFileSicuri.PulisciNomeFile(input));

    [Fact]
    public void PulisciNomeFile_NomeLungo_ConservaEstensione()
    {
        var risultato = NomiFileSicuri.PulisciNomeFile(new string('a', 300) + ".pdf", lunghezzaMassima: 60);
        Assert.EndsWith(".pdf", risultato);
        Assert.Equal(60, risultato.Length);
    }

    [Fact]
    public void RendiUnivoco_NomeLibero_RestaInvariato() =>
        Assert.Equal("a.pdf", NomiFileSicuri.RendiUnivoco("a.pdf", _ => false));

    [Fact]
    public void RendiUnivoco_NomeOccupato_AggiungeSuffissoPrimaDellEstensione()
    {
        var occupati = new HashSet<string> { "a.pdf", "a (1).pdf" };
        Assert.Equal("a (2).pdf", NomiFileSicuri.RendiUnivoco("a.pdf", occupati.Contains));
    }

    [Fact]
    public void RendiUnivoco_CartellaSenzaEstensione()
    {
        var occupati = new HashSet<string> { "Fatture" };
        Assert.Equal("Fatture (1)", NomiFileSicuri.RendiUnivoco("Fatture", occupati.Contains));
    }
}
