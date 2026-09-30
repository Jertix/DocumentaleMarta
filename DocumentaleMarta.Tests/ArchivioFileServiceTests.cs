using System.Security.Cryptography;
using DocumentaleMarta.Data;

namespace DocumentaleMarta.Tests;

public class ArchivioFileServiceTests : IDisposable
{
    private readonly CartellaTemporanea _tmp = new();
    private readonly ArchivioFileService _servizio;

    public ArchivioFileServiceTests() =>
        _servizio = new ArchivioFileService(_tmp.Combina("Documentale"), usaCestino: false);

    public void Dispose() => _tmp.Dispose();

    [Fact]
    public void CreaCartella_CreaLaCartellaFisica_ERestituisceIlPercorsoRelativo()
    {
        var area = _servizio.CreaCartella("", "Fatture");
        var cartella = _servizio.CreaCartella(area, "Fattura 1/2026");

        Assert.Equal("Fatture", area);
        Assert.Equal(Path.Combine("Fatture", "Fattura 1_2026"), cartella);
        Assert.True(Directory.Exists(_servizio.PercorsoAssoluto(cartella)));
    }

    [Fact]
    public void CreaCartella_NomeGiaEsistente_AggiungeSuffisso()
    {
        var prima = _servizio.CreaCartella("", "INPS");
        var seconda = _servizio.CreaCartella("", "inps");

        Assert.Equal("INPS", prima);
        Assert.Equal("inps (1)", seconda); // Windows non distingue maiuscole/minuscole
    }

    [Fact]
    public void PercorsoAssoluto_RifiutaPercorsiCheEsconoDallaRadice()
    {
        Assert.Throws<ArgumentException>(() => _servizio.PercorsoAssoluto(@"..\Altro"));
        Assert.Throws<ArgumentException>(() => _servizio.PercorsoAssoluto(@"Fatture\..\..\Altro"));
        Assert.Throws<ArgumentException>(() => _servizio.PercorsoAssoluto(@"C:\Windows"));
    }

    [Fact]
    public void PercorsoAssoluto_NonConfondeUnaCartellaGemellaConLaRadice()
    {
        // "Documentale2" inizia con "Documentale" ma è fuori dalla radice.
        Assert.Throws<ArgumentException>(() => _servizio.PercorsoAssoluto(@"..\Documentale2\x"));
    }

    [Fact]
    public void CopiaFile_CopiaEntroLaCartella_LasciaLOriginale_ECalcolaHash()
    {
        var sorgente = _tmp.CreaFile(Path.Combine("esterno", "fattura.pdf"), "contenuto fattura");
        var cartella = _servizio.CreaCartella("", "Fatture");

        var risultato = _servizio.CopiaFile(sorgente, cartella);

        Assert.True(File.Exists(sorgente));
        Assert.Equal("fattura.pdf", risultato.NomeFile);
        Assert.Equal(Path.Combine("Fatture", "fattura.pdf"), risultato.PercorsoRelativo);
        Assert.Equal("contenuto fattura", File.ReadAllText(_servizio.PercorsoAssoluto(risultato.PercorsoRelativo)));
        Assert.Equal("contenuto fattura".Length, risultato.Dimensione);
        Assert.Equal(Convert.ToHexString(SHA256.HashData("contenuto fattura"u8.ToArray())), risultato.Hash);
    }

    [Fact]
    public void CopiaFile_StessoNomeDueVolte_NonSovrascrive()
    {
        var cartella = _servizio.CreaCartella("", "Fatture");
        var primo = _tmp.CreaFile(Path.Combine("a", "doc.txt"), "uno");
        var secondo = _tmp.CreaFile(Path.Combine("b", "doc.txt"), "due");

        var r1 = _servizio.CopiaFile(primo, cartella);
        var r2 = _servizio.CopiaFile(secondo, cartella);

        Assert.Equal("doc.txt", r1.NomeFile);
        Assert.Equal("doc (1).txt", r2.NomeFile);
        Assert.Equal("uno", File.ReadAllText(_servizio.PercorsoAssoluto(r1.PercorsoRelativo)));
        Assert.Equal("due", File.ReadAllText(_servizio.PercorsoAssoluto(r2.PercorsoRelativo)));
    }

    [Fact]
    public void CopiaFile_SorgenteMancante_Lancia() =>
        Assert.Throws<FileNotFoundException>(() => _servizio.CopiaFile(_tmp.Combina("non-esiste.pdf"), "Fatture"));

    [Fact]
    public void RinominaCartella_SpostaIlContenuto()
    {
        var area = _servizio.CreaCartella("", "Fatture");
        var cartella = _servizio.CreaCartella(area, "Vecchia");
        _servizio.CopiaFile(_tmp.CreaFile("x.txt"), cartella);

        var nuova = _servizio.RinominaCartella(cartella, "Nuova");

        Assert.Equal(Path.Combine("Fatture", "Nuova"), nuova);
        Assert.False(Directory.Exists(_servizio.PercorsoAssoluto(cartella)));
        Assert.True(File.Exists(_servizio.PercorsoAssoluto(Path.Combine(nuova, "x.txt"))));
    }

    [Fact]
    public void RinominaCartella_SoloMaiuscole_Funziona()
    {
        var area = _servizio.CreaCartella("", "inps");

        var nuova = _servizio.RinominaCartella(area, "INPS");

        Assert.Equal("INPS", nuova);
        Assert.Equal("INPS", Path.GetFileName(Directory.GetDirectories(_servizio.PercorsoRadice).Single()));
    }

    [Fact]
    public void RinominaCartella_ConNomeDiUnAltraCartella_AggiungeSuffisso()
    {
        _servizio.CreaCartella("", "A");
        var b = _servizio.CreaCartella("", "B");

        Assert.Equal("A (1)", _servizio.RinominaCartella(b, "A"));
    }

    [Fact]
    public void RinominaCartella_Inesistente_Lancia() =>
        Assert.Throws<DirectoryNotFoundException>(() => _servizio.RinominaCartella("Manca", "X"));

    [Fact]
    public void SpostaFile_VaNellaNuovaCartella_SenzaSovrascrivere()
    {
        var a = _servizio.CreaCartella("", "A");
        var b = _servizio.CreaCartella("", "B");
        var inA = _servizio.CopiaFile(_tmp.CreaFile(Path.Combine("x", "doc.txt"), "da A"), a);
        _servizio.CopiaFile(_tmp.CreaFile(Path.Combine("y", "doc.txt"), "gia in B"), b);

        var nuovo = _servizio.SpostaFile(inA.PercorsoRelativo, b);

        Assert.Equal(Path.Combine("B", "doc (1).txt"), nuovo);
        Assert.False(_servizio.Esiste(inA.PercorsoRelativo));
        Assert.Equal("da A", File.ReadAllText(_servizio.PercorsoAssoluto(nuovo)));
        Assert.Equal("gia in B", File.ReadAllText(_servizio.PercorsoAssoluto(Path.Combine("B", "doc.txt"))));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    public void RadiceDellArchivio_NonSiPuoRinominareEliminareORimuovere(string radice)
    {
        _servizio.CreaCartella("", "A");

        Assert.Throws<ArgumentException>(() => _servizio.EliminaCartella(radice));
        Assert.Throws<ArgumentException>(() => _servizio.RinominaCartella(radice, "X"));
        Assert.Throws<ArgumentException>(() => _servizio.RimuoviCartellaVuota(radice));
        Assert.True(Directory.Exists(_servizio.PercorsoAssoluto("A")));
    }

    [Fact]
    public void RimuoviCartellaVuota_RimuoveSoloSeVuota()
    {
        var vuota = _servizio.CreaCartella("", "Vuota");
        var piena = _servizio.CreaCartella("", "Piena");
        _servizio.CopiaFile(_tmp.CreaFile("x.txt"), piena);

        _servizio.RimuoviCartellaVuota(vuota);
        _servizio.RimuoviCartellaVuota(piena);
        _servizio.RimuoviCartellaVuota("NonEsiste");

        Assert.False(_servizio.Esiste(vuota));
        Assert.True(_servizio.Esiste(piena));
    }

    [Fact]
    public void EliminaFile_EEliminaCartella_RimuovonoDalDisco_EToleranoIlGiaEliminato()
    {
        var cartella = _servizio.CreaCartella("", "Da eliminare");
        var file = _servizio.CopiaFile(_tmp.CreaFile("x.txt"), cartella);

        _servizio.EliminaFile(file.PercorsoRelativo);
        Assert.False(_servizio.Esiste(file.PercorsoRelativo));

        _servizio.EliminaCartella(cartella);
        Assert.False(_servizio.Esiste(cartella));

        _servizio.EliminaFile(file.PercorsoRelativo);
        _servizio.EliminaCartella(cartella);
    }
}
