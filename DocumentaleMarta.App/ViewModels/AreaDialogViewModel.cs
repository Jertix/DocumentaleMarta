using CommunityToolkit.Mvvm.ComponentModel;
using DocumentaleMarta.Core.Modelli;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>
/// I dati della finestra che permette di scegliere l'icona di un'area: o insieme al nome (per una nuova area)
/// o da sola (per cambiare l'icona di un'area che c'è già). Non fa nulla finché l'utente non conferma.
/// </summary>
public partial class AreaDialogViewModel : ObservableObject
{
    private readonly Func<string, string?>? _validatoreNome;

    /// <summary>
    /// Prepara la finestra. Con un <paramref name="validatoreNome"/> la finestra chiede anche il nome dell'area;
    /// senza, mostra solo la scelta dell'icona.
    /// </summary>
    /// <param name="titolo">Il titolo della finestra.</param>
    /// <param name="codiceIconaIniziale">L'icona già scelta (null per quella predefinita): parte selezionata.</param>
    /// <param name="validatoreNome">Restituisce il motivo per cui un nome non va bene, o null se va bene.</param>
    /// <param name="messaggioIcona">La frase scritta sopra la griglia delle icone.</param>
    public AreaDialogViewModel(
        string titolo, string? codiceIconaIniziale, Func<string, string?>? validatoreNome = null,
        string messaggioIcona = "Icona dell'area (si può cambiare in ogni momento):")
    {
        Titolo = titolo;
        MessaggioIcona = messaggioIcona;
        _validatoreNome = validatoreNome;
        _iconaSelezionata = IconeArea.Risolvi(codiceIconaIniziale);
    }

    /// <summary>Titolo della finestra.</summary>
    public string Titolo { get; }

    /// <summary>La frase scritta sopra la griglia delle icone.</summary>
    public string MessaggioIcona { get; }

    /// <summary>La finestra chiede anche il nome (nuova area) oppure no (solo cambio di icona).</summary>
    public bool ChiedeNome => _validatoreNome is not null;

    /// <summary>Le icone tra cui scegliere, nell'ordine in cui si mostrano.</summary>
    public IReadOnlyList<IconaArea> Icone => IconeArea.Disponibili;

    /// <summary>Il nome scritto dall'utente (solo se <see cref="ChiedeNome"/>).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ErroreNome), nameof(PuoConfermare), nameof(NomeConfermato))]
    private string _nome = "";

    /// <summary>L'icona selezionata nell'elenco.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DescrizioneIconaSelezionata))]
    private IconaArea _iconaSelezionata;

    /// <summary>Il nome dell'icona selezionata, scritto sotto la griglia.</summary>
    public string DescrizioneIconaSelezionata => IconaSelezionata.Descrizione;

    /// <summary>
    /// Perché il nome non va bene. Vuoto se va bene e finché l'utente non ha scritto nulla (un campo appena aperto non va
    /// rimproverato).
    /// </summary>
    public string ErroreNome => _validatoreNome is null || Nome.Length == 0 ? "" : _validatoreNome(Nome) ?? "";

    /// <summary>Il nome senza spazi ai lati.</summary>
    public string NomeConfermato => Nome.Trim();

    /// <summary>«OK» è attivo: il nome (se richiesto) è valido.</summary>
    public bool PuoConfermare => _validatoreNome is null || _validatoreNome(Nome) is null;

    /// <summary>
    /// Il codice da salvare per l'icona scelta: null per quella predefinita (come per le aree che non ne hanno mai scelta una).
    /// </summary>
    public string? CodiceIcona => IconeArea.DaSalvare(IconaSelezionata.Codice);
}
