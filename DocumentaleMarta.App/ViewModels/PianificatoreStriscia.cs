namespace DocumentaleMarta.App.ViewModels;

/// <summary>I personaggi che possono attraversare la striscia in fondo all'albero.</summary>
public enum PersonaggioStriscia
{
    /// <summary>Un cagnolino che corre.</summary>
    Cane,

    /// <summary>Un omino che passeggia.</summary>
    Omino,

    /// <summary>Un gattino che cammina.</summary>
    Gatto,

    /// <summary>Un uccellino che vola.</summary>
    Uccellino,

    /// <summary>Un operaio col casco e il martello.</summary>
    Operaio
}

/// <summary>Un passaggio da mostrare: chi attraversa la striscia e da quale lato parte.</summary>
public record PassaggioStriscia(PersonaggioStriscia Personaggio, bool DaDestra);

/// <summary>
/// Decide quando passa un personaggio nella striscia animata e quale: il primo poco dopo l'apertura (così lo si scopre),
/// poi ogni qualche minuto, con personaggio e direzione a caso e mai lo stesso personaggio due volte di fila.
/// Non sa nulla di WPF: si prova con un <see cref="Random"/> con il seme.
/// </summary>
public class PianificatoreStriscia(Random? random = null)
{
    /// <summary>Il primo passaggio arriva tra questi due tempi dall'apertura (o da quando si riaccende l'interruttore).</summary>
    public static readonly TimeSpan PrimaAttesaMinima = TimeSpan.FromSeconds(20), PrimaAttesaMassima = TimeSpan.FromSeconds(40);

    /// <summary>Tra un passaggio e il successivo passa un tempo a caso tra questi due.</summary>
    public static readonly TimeSpan AttesaMinima = TimeSpan.FromMinutes(3), AttesaMassima = TimeSpan.FromMinutes(6);

    /// <summary>Se al momento giusto la finestra non è attiva (o ridotta a icona) si riprova dopo questo tempo.</summary>
    public static readonly TimeSpan NuovoTentativo = TimeSpan.FromSeconds(30);

    private readonly Random _random = random ?? Random.Shared;
    private PersonaggioStriscia? _ultimo;

    /// <summary>Quanto aspettare prima del prossimo passaggio; più breve per il primo.</summary>
    public TimeSpan ProssimaAttesa(bool primaVolta) =>
        primaVolta ? Tra(PrimaAttesaMinima, PrimaAttesaMassima) : Tra(AttesaMinima, AttesaMassima);

    /// <summary>Sceglie chi passa e da che lato (un personaggio diverso dall'ultimo passato).</summary>
    public PassaggioStriscia ProssimoPassaggio()
    {
        var tutti = Enum.GetValues<PersonaggioStriscia>().Where(p => p != _ultimo).ToArray();
        var scelto = tutti[_random.Next(tutti.Length)];
        _ultimo = scelto;
        return new PassaggioStriscia(scelto, DaDestra: _random.Next(2) == 1);
    }

    /// <summary>La direzione di un passaggio chiesto a mano (doppio clic): a caso, e il personaggio resta quello chiesto.</summary>
    public PassaggioStriscia Passaggio(PersonaggioStriscia personaggio)
    {
        _ultimo = personaggio;
        return new PassaggioStriscia(personaggio, DaDestra: _random.Next(2) == 1);
    }

    /// <summary>Quanti pixel al secondo fa il personaggio (il cane corre, l'omino passeggia).</summary>
    public static double Velocita(PersonaggioStriscia personaggio) => personaggio switch
    {
        PersonaggioStriscia.Cane => 95,
        PersonaggioStriscia.Omino => 38,
        PersonaggioStriscia.Gatto => 32,
        PersonaggioStriscia.Uccellino => 80,
        PersonaggioStriscia.Operaio => 36,
        _ => 40
    };

    /// <summary>
    /// Quanto dura l'attraversamento: da fuori da un lato (il personaggio è tutto nascosto) a fuori dall'altro,
    /// alla velocità del personaggio.
    /// </summary>
    public static TimeSpan DurataAttraversamento(PersonaggioStriscia personaggio, double larghezzaStriscia, double larghezzaPersonaggio) =>
        TimeSpan.FromSeconds((Math.Max(0, larghezzaStriscia) + Math.Max(0, larghezzaPersonaggio)) / Velocita(personaggio));

    private TimeSpan Tra(TimeSpan minimo, TimeSpan massimo) =>
        minimo + TimeSpan.FromTicks((long)((massimo - minimo).Ticks * _random.NextDouble()));
}
