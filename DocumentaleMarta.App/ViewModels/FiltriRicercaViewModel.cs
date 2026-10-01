using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>Una voce del menu "Area": un'area oppure "Tutte le aree".</summary>
public record AreaOpzione(int? Id, string Nome)
{
    public static readonly AreaOpzione Tutte = new(null, "Tutte le aree");
}

public enum StatoRicerca
{
    Qualsiasi,
    NonCompletate,
    Completate,

    /// <summary>Non completate, con la scadenza entro la soglia arancione delle impostazioni.</summary>
    InScadenza,

    /// <summary>Non completate, con la scadenza già passata.</summary>
    Scadute
}

/// <summary>Una voce del menu "Stato".</summary>
public record StatoOpzione(StatoRicerca Valore, string Testo)
{
    public static readonly IReadOnlyList<StatoOpzione> Tutte =
    [
        new(StatoRicerca.Qualsiasi, "Qualsiasi"),
        new(StatoRicerca.NonCompletate, "Non completate"),
        new(StatoRicerca.Completate, "Completate"),
        new(StatoRicerca.InScadenza, "In scadenza"),
        new(StatoRicerca.Scadute, "Scadute")
    ];
}

/// <summary>I filtri del pannello "Ricerca avanzata".</summary>
public partial class FiltriRicercaViewModel : ObservableObject
{
    private bool _inAggiornamento;

    [ObservableProperty] private bool _pdf;
    [ObservableProperty] private bool _word;
    [ObservableProperty] private bool _excel;
    [ObservableProperty] private bool _immagini;
    [ObservableProperty] private bool _altro;
    private AreaOpzione _area = AreaOpzione.Tutte;
    private StatoOpzione _stato = StatoOpzione.Tutte[0];
    [ObservableProperty] private DateTime? _scadenzaDal;
    [ObservableProperty] private DateTime? _scadenzaAl;
    [ObservableProperty] private bool _cercaNelContenuto = true;

    // Un menu a tendina a cui si toglie la voce scelta scrive "nessuna scelta" nel filtro: qui diventa "Tutte" / "Qualsiasi".
    public AreaOpzione Area
    {
        get => _area;
        set => SetProperty(ref _area, value ?? AreaOpzione.Tutte);
    }

    public StatoOpzione Stato
    {
        get => _stato;
        set => SetProperty(ref _stato, value ?? StatoOpzione.Tutte[0]);
    }

    /// <summary>Le aree tra cui scegliere; la prima è sempre "Tutte le aree".</summary>
    public ObservableCollection<AreaOpzione> Aree { get; } = [AreaOpzione.Tutte];

    public IReadOnlyList<StatoOpzione> Stati => StatoOpzione.Tutte;

    /// <summary>Quanti gruppi di filtri sono in uso (tipo di file, area, stato, scadenza): serve al testo del pulsante.</summary>
    public int NumeroAttivi =>
        (Categorie != CategoriaFile.Nessuna ? 1 : 0)
        + (Area.Id is not null ? 1 : 0)
        + (Stato.Valore != StatoRicerca.Qualsiasi ? 1 : 0)
        + (ScadenzaDal is not null || ScadenzaAl is not null ? 1 : 0);

    public string TestoPulsante => NumeroAttivi > 0 ? $"Ricerca avanzata ({NumeroAttivi})" : "Ricerca avanzata";

    /// <summary>I tipi di file spuntati; nessuno = tutti.</summary>
    public CategoriaFile Categorie =>
        (Pdf ? CategoriaFile.Pdf : 0) | (Word ? CategoriaFile.Word : 0) | (Excel ? CategoriaFile.Excel : 0)
        | (Immagini ? CategoriaFile.Immagini : 0) | (Altro ? CategoriaFile.Altro : 0);

    /// <summary>Scatta dopo ogni modifica dei filtri (non durante un azzeramento o un aggiornamento delle aree).</summary>
    public event Action? Cambiati;

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        // Le proprietà calcolate non sono modifiche dell'utente: niente giri a vuoto.
        if (e.PropertyName is nameof(NumeroAttivi) or nameof(TestoPulsante) or nameof(Categorie))
            return;

        OnPropertyChanged(nameof(Categorie));
        OnPropertyChanged(nameof(NumeroAttivi));
        OnPropertyChanged(nameof(TestoPulsante));
        if (!_inAggiornamento)
            Cambiati?.Invoke();
    }

    /// <summary>
    /// I filtri nel formato che capisce il servizio di ricerca. "In scadenza" e "Scadute" diventano intervalli di date
    /// calcolati sulla data di oggi e sulle soglie degli avvisi; se l'utente ha scelto anche delle date, valgono entrambe.
    /// </summary>
    public FiltriRicerca ToFiltri(AlertService avvisi)
    {
        var oggi = avvisi.Oggi;
        var dal = ScadenzaDal is { } d ? DateOnly.FromDateTime(d) : (DateOnly?)null;
        var al = ScadenzaAl is { } a ? DateOnly.FromDateTime(a) : (DateOnly?)null;
        var stato = StatoCartella.Qualsiasi;

        switch (Stato.Valore)
        {
            case StatoRicerca.NonCompletate:
                stato = StatoCartella.Aperta;
                break;
            case StatoRicerca.Completate:
                stato = StatoCartella.Completata;
                break;
            case StatoRicerca.InScadenza:
                stato = StatoCartella.Aperta;
                dal = Massimo(dal, oggi);
                al = Minimo(al, oggi.AddDays(avvisi.SogliaArancioneGiorni));
                break;
            case StatoRicerca.Scadute:
                stato = StatoCartella.Aperta;
                al = Minimo(al, oggi.AddDays(-1));
                break;
        }

        return new FiltriRicerca(Categorie, Area.Id, stato, dal, al, CercaNelContenuto);
    }

    [RelayCommand]
    private void Azzera() => Azzera(notifica: true);

    /// <summary>Rimette tutti i filtri a "nessuno". Con <paramref name="notifica"/> falso non avvisa nessuno (chi chiama sa già cosa fare).</summary>
    public void Azzera(bool notifica)
    {
        _inAggiornamento = true;
        try
        {
            Pdf = Word = Excel = Immagini = Altro = false;
            Area = AreaOpzione.Tutte;
            Stato = StatoOpzione.Tutte[0];
            ScadenzaDal = null;
            ScadenzaAl = null;
            CercaNelContenuto = true;
        }
        finally
        {
            _inAggiornamento = false;
        }

        if (notifica)
            Cambiati?.Invoke();
    }

    /// <summary>
    /// Rilegge l'elenco delle aree. L'area scelta resta scelta se esiste ancora (anche se è stata rinominata);
    /// altrimenti si torna a "Tutte le aree" e i filtri risultano cambiati.
    /// </summary>
    public void AggiornaAree(IEnumerable<(int Id, string Nome)> aree)
    {
        var scelta = Area.Id;
        var eraFiltrato = scelta is not null;

        _inAggiornamento = true;
        try
        {
            Aree.Clear();
            Aree.Add(AreaOpzione.Tutte);
            foreach (var (id, nome) in aree)
                Aree.Add(new AreaOpzione(id, nome));

            Area = Aree.FirstOrDefault(a => a.Id == scelta) ?? AreaOpzione.Tutte;
        }
        finally
        {
            _inAggiornamento = false;
        }

        if (eraFiltrato && Area.Id is null)
            Cambiati?.Invoke(); // l'area scelta non c'è più: senza quel filtro la ricerca cambia
    }

    private static DateOnly Massimo(DateOnly? a, DateOnly b) => a is { } x && x > b ? x : b;

    private static DateOnly Minimo(DateOnly? a, DateOnly b) => a is { } x && x < b ? x : b;
}
