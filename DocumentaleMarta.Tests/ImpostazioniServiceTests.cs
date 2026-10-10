using DocumentaleMarta.Core.Impostazioni;

namespace DocumentaleMarta.Tests;

public class ImpostazioniServiceTests
{
    [Fact]
    public void FileMancante_VieneCreatoConIValoriPredefiniti()
    {
        using var tmp = new CartellaTemporanea();
        var servizio = new ImpostazioniService(tmp.Combina("sotto", "impostazioni.json"));

        var impostazioni = servizio.Carica();

        Assert.True(File.Exists(servizio.PercorsoFile));
        Assert.Equal("METAL PROJET DI TEDDE PAOLO E SORRENTINO LUCA SNC", impostazioni.Azienda.RagioneSociale);
        Assert.Equal("01790610990", impostazioni.Azienda.CodiceFiscale);
        Assert.Equal(@"C:\Documentale", impostazioni.PercorsoRadice);
        Assert.Equal("Tutti i documenti", impostazioni.NomeRadice);
        Assert.Equal(8, impostazioni.SogliaArancioneGiorni);
        Assert.Equal(4, impostazioni.SogliaRossaGiorni);
        Assert.Empty(impostazioni.Valida());
    }

    [Fact]
    public void SalvaECarica_ConservaIValori_EIlFileRestaLeggibile()
    {
        using var tmp = new CartellaTemporanea();
        var servizio = new ImpostazioniService(tmp.Combina("impostazioni.json"));

        var impostazioni = new ImpostazioniApp { SogliaArancioneGiorni = 45, SogliaRossaGiorni = 10, NomeRadice = "Archivio è più" };
        servizio.Salva(impostazioni);
        var riletto = servizio.Carica();

        Assert.Equal(45, riletto.SogliaArancioneGiorni);
        Assert.Equal(10, riletto.SogliaRossaGiorni);
        Assert.Equal("Archivio è più", riletto.NomeRadice);
        Assert.Contains("Archivio è più", File.ReadAllText(servizio.PercorsoFile)); // accenti non trasformati in \u00E8
        Assert.False(File.Exists(servizio.PercorsoFile + ".tmp"));
    }

    [Fact]
    public void FileNonValido_LanciaEccezioneChiara()
    {
        using var tmp = new CartellaTemporanea();
        var percorso = tmp.CreaFile("impostazioni.json", "{ questo non è json");
        var servizio = new ImpostazioniService(percorso);

        var ex = Assert.Throws<InvalidDataException>(() => servizio.Carica());
        Assert.Contains(percorso, ex.Message);
        Assert.Equal("{ questo non è json", File.ReadAllText(percorso)); // il file dell'utente non viene toccato
    }

    [Theory]
    [InlineData(30, 7, true)]
    [InlineData(8, 7, true)]
    [InlineData(7, 7, false)]   // arancione deve essere maggiore di rosso
    [InlineData(5, 7, false)]
    [InlineData(30, -1, false)]
    [InlineData(30, 0, true)]
    public void Valida_Soglie(int arancione, int rossa, bool valide)
    {
        var impostazioni = new ImpostazioniApp { SogliaArancioneGiorni = arancione, SogliaRossaGiorni = rossa };
        Assert.Equal(valide, impostazioni.Valida().Count == 0);
    }
}
