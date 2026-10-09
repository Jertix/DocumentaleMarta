using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Tests;

/// <summary>Un orologio fermo a una data scelta dal test.</summary>
public class TempoFisso(DateTime adesso) : TimeProvider
{
    public DateTime Adesso { get; set; } = adesso;

    public override DateTimeOffset GetUtcNow() => new(Adesso, TimeSpan.Zero);
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}

public class AlertServiceTests
{
    private static readonly DateOnly Oggi = new(2026, 10, 1);

    private static AlertService Servizio(int arancione = 30, int rossa = 7, bool attivo = true) =>
        new(arancione, rossa, attivo, new TempoFisso(new DateTime(2026, 10, 1, 15, 30, 0)));

    private static DateOnly TraGiorni(int giorni) => Oggi.AddDays(giorni);

    // ---------- Soglie ----------

    [Theory]
    [InlineData(-400, StatoAvviso.Rosso)]   // scaduta da un anno
    [InlineData(-1, StatoAvviso.Rosso)]     // scaduta ieri
    [InlineData(0, StatoAvviso.Rosso)]      // scade oggi
    [InlineData(5, StatoAvviso.Rosso)]
    [InlineData(7, StatoAvviso.Rosso)]      // la soglia rossa è inclusa
    [InlineData(8, StatoAvviso.Arancione)]
    [InlineData(20, StatoAvviso.Arancione)]
    [InlineData(30, StatoAvviso.Arancione)] // la soglia arancione è inclusa
    [InlineData(31, StatoAvviso.Nessuno)]
    [InlineData(365, StatoAvviso.Nessuno)]
    public void Valuta_ConLeSoglieDiDefault(int giorni, StatoAvviso atteso) =>
        Assert.Equal(atteso, Servizio().Valuta(TraGiorni(giorni), completato: false));

    [Fact]
    public void Valuta_ConSoglieDiverse_LeRispetta()
    {
        var servizio = Servizio(arancione: 10, rossa: 2);

        Assert.Equal(StatoAvviso.Rosso, servizio.Valuta(TraGiorni(2), false));
        Assert.Equal(StatoAvviso.Arancione, servizio.Valuta(TraGiorni(3), false));
        Assert.Equal(StatoAvviso.Arancione, servizio.Valuta(TraGiorni(10), false));
        Assert.Equal(StatoAvviso.Nessuno, servizio.Valuta(TraGiorni(11), false));
    }

    [Fact]
    public void Valuta_SogliaRossaZero_SoloLeScadenzeDiOggiEPassate()
    {
        var servizio = Servizio(arancione: 5, rossa: 0);

        Assert.Equal(StatoAvviso.Rosso, servizio.Valuta(TraGiorni(0), false));
        Assert.Equal(StatoAvviso.Arancione, servizio.Valuta(TraGiorni(1), false));
    }

    [Fact]
    public void Valuta_CartellaCompletata_NonHaMaiAvvisi_ANcheSeScaduta()
    {
        Assert.Equal(StatoAvviso.Nessuno, Servizio().Valuta(TraGiorni(-30), completato: true));
        Assert.Equal(StatoAvviso.Nessuno, Servizio().Valuta(TraGiorni(3), completato: true));
    }

    [Fact]
    public void Valuta_SenzaScadenza_NonHaAvvisi() =>
        Assert.Equal(StatoAvviso.Nessuno, Servizio().Valuta(null, completato: false));

    [Fact]
    public void Valuta_AvvisiDisattivati_NonHaMaiAvvisi() =>
        Assert.Equal(StatoAvviso.Nessuno, Servizio(attivo: false).Valuta(TraGiorni(-5), completato: false));

    // ---------- Data di oggi ----------

    [Fact]
    public void Oggi_UsaLOrologio_EIgnoraLOra()
    {
        var tempo = new TempoFisso(new DateTime(2026, 10, 1, 23, 59, 59));
        var servizio = new AlertService(30, 7, true, tempo);

        Assert.Equal(Oggi, servizio.Oggi);

        tempo.Adesso = new DateTime(2026, 10, 2, 0, 0, 1);
        Assert.Equal(Oggi.AddDays(1), servizio.Oggi);
    }

    [Fact]
    public void LoStatoCambiaQuandoPassaIlTempo()
    {
        var tempo = new TempoFisso(new DateTime(2026, 10, 1));
        var servizio = new AlertService(30, 7, true, tempo);
        var scadenza = new DateOnly(2026, 10, 20);

        Assert.Equal(StatoAvviso.Arancione, servizio.Valuta(scadenza, false));

        tempo.Adesso = new DateTime(2026, 10, 14);
        Assert.Equal(StatoAvviso.Rosso, servizio.Valuta(scadenza, false));
    }

    // ---------- Descrizioni ----------

    [Theory]
    [InlineData(-10, "Scaduta da 10 giorni")]
    [InlineData(-2, "Scaduta da 2 giorni")]
    [InlineData(-1, "Scaduta ieri")]
    [InlineData(0, "Scade oggi")]
    [InlineData(1, "Scade domani")]
    [InlineData(2, "Scade tra 2 giorni")]
    [InlineData(30, "Scade tra 30 giorni")]    // fino a 30 giorni si dice in giorni
    public void Descrivi_InItaliano(int giorni, string atteso) =>
        Assert.Equal(atteso, Servizio().Descrivi(TraGiorni(giorni)));

    // Oggi è il 1° ottobre 2026: i mesi sono mesi di calendario, non blocchi di 30 giorni.
    [Theory]
    [InlineData(31, "Scade tra 1 mese")]                     // 1° novembre
    [InlineData(32, "Scade tra 1 mese e 1 giorno")]
    [InlineData(45, "Scade tra 1 mese e 14 giorni")]
    [InlineData(61, "Scade tra 2 mesi")]                     // 1° dicembre
    [InlineData(92, "Scade tra 3 mesi")]                     // 1° gennaio 2027
    [InlineData(96, "Scade tra 3 mesi e 4 giorni")]          // 5 gennaio 2027
    [InlineData(300, "Scade tra 9 mesi e 27 giorni")]        // 28 luglio 2027
    [InlineData(365, "Scade tra 1 anno")]                    // 1° ottobre 2027
    [InlineData(366, "Scade tra 1 anno e 1 giorno")]
    [InlineData(400, "Scade tra 1 anno, 1 mese e 4 giorni")] // 5 novembre 2027
    public void Descrivi_OltreITrentaGiorni_DiceMesiEGiorni(int giorni, string atteso) =>
        Assert.Equal(atteso, Servizio().Descrivi(TraGiorni(giorni)));

    [Theory]
    [InlineData(-30, "Scaduta da 30 giorni")]
    [InlineData(-31, "Scaduta da 1 mese e 1 giorno")]        // 31 agosto
    [InlineData(-45, "Scaduta da 1 mese e 14 giorni")]       // 17 agosto
    [InlineData(-61, "Scaduta da 2 mesi")]                   // 1° agosto
    [InlineData(-400, "Scaduta da 1 anno, 1 mese e 4 giorni")] // 27 agosto 2025
    public void Descrivi_ScadutaDaPiuDiTrentaGiorni_DiceMesiEGiorni(int giorni, string atteso) =>
        Assert.Equal(atteso, Servizio().Descrivi(TraGiorni(giorni)));

    [Theory]
    [InlineData("2026-01-31", "2026-03-03", "Scade tra 1 mese e 3 giorni")]  // 31 gennaio + 1 mese = 28 febbraio
    [InlineData("2026-01-31", "2026-02-28", "Scade tra 28 giorni")]
    [InlineData("2026-01-30", "2026-03-02", "Scade tra 1 mese e 2 giorni")]
    [InlineData("2026-12-20", "2027-03-20", "Scade tra 3 mesi")]             // a cavallo dell'anno
    [InlineData("2028-02-29", "2029-02-28", "Scade tra 1 anno")]             // dal 29 febbraio bisestile
    [InlineData("2026-10-01", "2028-10-01", "Scade tra 2 anni")]
    public void Descrivi_FineMeseEAnni_ContaIMesiDiCalendario(string oggi, string scadenza, string atteso)
    {
        var servizio = new AlertService(30, 7, true, new TempoFisso(DateTime.Parse(oggi)));

        Assert.Equal(atteso, servizio.Descrivi(DateOnly.Parse(scadenza)));
    }

    [Fact]
    public void Giorni_NegativiSeScaduta() =>
        Assert.Equal(-3, Servizio().Giorni(TraGiorni(-3)));

    // ---------- Il più grave ----------

    [Theory]
    [InlineData(StatoAvviso.Nessuno, StatoAvviso.Nessuno, StatoAvviso.Nessuno)]
    [InlineData(StatoAvviso.Nessuno, StatoAvviso.Arancione, StatoAvviso.Arancione)]
    [InlineData(StatoAvviso.Arancione, StatoAvviso.Rosso, StatoAvviso.Rosso)]
    [InlineData(StatoAvviso.Rosso, StatoAvviso.Arancione, StatoAvviso.Rosso)]
    public void Peggiore_ERossoSopraArancione(StatoAvviso a, StatoAvviso b, StatoAvviso atteso) =>
        Assert.Equal(atteso, AlertService.Peggiore(a, b));

    // ---------- Costruzione ----------

    [Theory]
    [InlineData(7, 7)]   // arancione deve superare rosso
    [InlineData(5, 7)]
    [InlineData(30, -1)] // rossa negativa
    public void Costruttore_SoglieNonValide_Rifiutate(int arancione, int rossa) =>
        Assert.ThrowsAny<ArgumentOutOfRangeException>(() => new AlertService(arancione, rossa));

    [Fact]
    public void DaImpostazioni_LeggeSoglieEInterruttore()
    {
        var servizio = new AlertService(new ImpostazioniApp { SogliaArancioneGiorni = 45, SogliaRossaGiorni = 10, AvvisiAttivi = false });

        Assert.Equal(45, servizio.SogliaArancioneGiorni);
        Assert.Equal(10, servizio.SogliaRossaGiorni);
        Assert.False(servizio.Attivo);
    }

    [Fact]
    public void ConLeImpostazioniDiDefault_SonoTrentaESetteGiorni()
    {
        var servizio = new AlertService(new ImpostazioniApp());

        Assert.Equal(30, servizio.SogliaArancioneGiorni);
        Assert.Equal(7, servizio.SogliaRossaGiorni);
        Assert.True(servizio.Attivo);
    }
}
