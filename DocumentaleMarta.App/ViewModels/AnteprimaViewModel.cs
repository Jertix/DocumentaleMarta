using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.Core.Servizi;
using System.Windows.Media;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>Un documento di una griglia, abbastanza per mostrarne l'anteprima.</summary>
public interface IDocumentoAnteprima
{
    string NomeFile { get; }
    string PercorsoRelativo { get; }
}

/// <summary>
/// Il pannello di anteprima a destra: mostra la pagina del documento selezionato in una griglia (PDF e immagini),
/// con i pulsanti per sfogliare le pagine. Per gli altri formati spiega che si usa «Apri».
/// Con il doppio clic (o il pulsante) si apre la stessa pagina ingrandita, disegnata a una risoluzione più alta.
/// </summary>
/// <param name="larghezza">A che larghezza (in pixel) si disegnano le pagine: più alta per la finestra ingrandita.</param>
/// <param name="mostraIngrandita">Apre la finestra ingrandita con il modello che le si passa; senza, l'ingrandimento non è disponibile.</param>
public partial class AnteprimaViewModel(
    IGeneratoreAnteprima generatore, IArchivioFileService files,
    int larghezza = GeneratoreAnteprima.LarghezzaMassima, Action<AnteprimaViewModel>? mostraIngrandita = null) : ObservableObject
{
    public const string TestoNessunDocumento = "Seleziona un documento per vederne l'anteprima.";

    private IDocumentoAnteprima? _documento;
    private int _pagina;
    private CancellationTokenSource? _annullamento;
    private Task _lavoro = Task.CompletedTask;

    /// <summary>Attesa prima di disegnare, perché scorrendo l'elenco con le frecce non si disegni ogni riga.</summary>
    public TimeSpan Ritardo { get; set; } = TimeSpan.FromMilliseconds(120);

    /// <summary>Completa quando l'anteprima in lavorazione è pronta (serve ai test).</summary>
    public Task Completamento => _lavoro;

    /// <summary>Il pannello è aperto. Chiuso, non si disegna nulla (si risparmia lavoro).</summary>
    [ObservableProperty]
    private bool _visibile = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CaricamentoVisibile), nameof(HaMessaggio))]
    private StatoAnteprima _stato = StatoAnteprima.Vuota;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaImmagine), nameof(CaricamentoVisibile), nameof(HaPagine), nameof(HaNota))]
    [NotifyCanExecuteChangedFor(nameof(IngrandisciCommand))]
    private ImageSource? _immagine;

    /// <summary>Il nome del documento mostrato.</summary>
    [ObservableProperty]
    private string _titolo = "";

    /// <summary>Cosa dire quando non c'è un'immagine (nessuna selezione, formato senza anteprima, errore).</summary>
    [ObservableProperty]
    private string _messaggio = TestoNessunDocumento;

    /// <summary>Una precisazione sotto l'immagine (es. "Miniatura della prima pagina"); vuota se non serve.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaNota))]
    private string _nota = "";

    public bool HaNota => HaImmagine && Nota.Length > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaPagine), nameof(TestoPagina))]
    [NotifyCanExecuteChangedFor(nameof(PaginaPrecedenteCommand), nameof(PaginaSuccessivaCommand))]
    private int _numeroPagine = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TestoPagina))]
    [NotifyCanExecuteChangedFor(nameof(PaginaPrecedenteCommand), nameof(PaginaSuccessivaCommand))]
    private int _paginaCorrente = 1;

    /// <summary>C'è un'immagine da mostrare (resta visibile mentre si carica un'altra pagina dello stesso documento).</summary>
    public bool HaImmagine => Immagine is not null;

    /// <summary>"Caricamento…" si vede solo se non c'è ancora nulla da mostrare.</summary>
    public bool CaricamentoVisibile => Stato == StatoAnteprima.Caricamento && Immagine is null;

    public bool HaMessaggio => Stato is StatoAnteprima.Vuota or StatoAnteprima.NonDisponibile or StatoAnteprima.Errore;

    /// <summary>I pulsanti per sfogliare compaiono solo se ci sono più pagine.</summary>
    public bool HaPagine => HaImmagine && NumeroPagine > 1;

    public string TestoPagina => $"Pagina {PaginaCorrente} di {NumeroPagine}";

    /// <summary>Cambiando lo stato dell'anteprima i pulsanti per sfogliare le pagine possono comparire o sparire.</summary>
    partial void OnStatoChanged(StatoAnteprima value) => OnPropertyChanged(nameof(HaPagine));

    /// <summary>Mostra l'anteprima del documento (null = nessuno selezionato), a partire dalla pagina indicata (la prima è la 0).</summary>
    public void Mostra(IDocumentoAnteprima? documento, int pagina = 0)
    {
        _documento = documento;
        _pagina = Math.Max(0, pagina);
        Avvia();
    }

    /// <summary>Il documento mostrato si può ingrandire: c'è un'immagine e c'è qualcuno che sa aprire la finestra.</summary>
    private bool PuoIngrandire => HaImmagine && _documento is not null && mostraIngrandita is not null;

    /// <summary>
    /// Apre la pagina che si sta guardando in una finestra grande, con un nuovo modello che la disegna a una risoluzione più alta
    /// (senza attesa) e parte dalla stessa pagina. Il pannello resta com'è.
    /// </summary>
    [RelayCommand(CanExecute = nameof(PuoIngrandire))]
    private void Ingrandisci()
    {
        if (_documento is null || mostraIngrandita is null)
            return;

        var ingrandita = new AnteprimaViewModel(generatore, files, GeneratoreAnteprima.LarghezzaIngrandita) { Ritardo = TimeSpan.Zero };
        ingrandita.Mostra(_documento, _pagina);
        mostraIngrandita(ingrandita);
    }

    /// <summary>Nessun documento da mostrare (è cambiata la schermata a sinistra).</summary>
    public void Svuota() => Mostra(null);

    /// <summary>
    /// Aprendo il pannello dell'anteprima si prepara l'immagine del documento selezionato; chiudendolo si interrompe il
    /// lavoro in corso.
    /// </summary>
    partial void OnVisibileChanged(bool value)
    {
        if (value)
            Avvia();
        else
            Annulla();
    }

    private bool PuoTornareIndietro => PaginaCorrente > 1;
    private bool PuoAndareAvanti => PaginaCorrente < NumeroPagine;

    /// <summary>Passa alla pagina precedente del documento.</summary>
    [RelayCommand(CanExecute = nameof(PuoTornareIndietro))]
    private void PaginaPrecedente() => VaiAllaPagina(_pagina - 1);

    /// <summary>Passa alla pagina successiva del documento.</summary>
    [RelayCommand(CanExecute = nameof(PuoAndareAvanti))]
    private void PaginaSuccessiva() => VaiAllaPagina(_pagina + 1);

    /// <summary>Va alla pagina indicata (senza uscire dalle pagine che esistono) e ne prepara l'immagine.</summary>
    private void VaiAllaPagina(int pagina)
    {
        _pagina = Math.Clamp(pagina, 0, NumeroPagine - 1);
        Avvia();
    }

    /// <summary>Interrompe la preparazione dell'anteprima in corso, se c'è.</summary>
    private void Annulla()
    {
        _annullamento?.Cancel();
        _annullamento = null;
    }

    /// <summary>
    /// Fa partire la preparazione dell'immagine del documento corrente (interrompendo quella precedente); se non c'è un
    /// documento o il pannello è chiuso non fa nulla.
    /// </summary>
    private void Avvia()
    {
        Annulla();

        if (_documento is null)
        {
            Imposta(StatoAnteprima.Vuota, TestoNessunDocumento, immagine: null, titolo: "", pagine: 1);
            _lavoro = Task.CompletedTask;
            return;
        }
        if (!Visibile)
        {
            _lavoro = Task.CompletedTask;
            return;
        }

        var annullamento = new CancellationTokenSource();
        _annullamento = annullamento;
        _lavoro = GeneraAsync(_documento, _pagina, annullamento.Token);
    }

    /// <summary>
    /// Prepara l'immagine di una pagina: aspetta un attimo (per non lavorare a ogni clic), controlla che il file ci sia, la
    /// fa disegnare in background e la mostra, a meno che nel frattempo l'utente non abbia scelto altro.
    /// </summary>
    private async Task GeneraAsync(IDocumentoAnteprima documento, int pagina, CancellationToken annullamento)
    {
        try
        {
            // Cambiando pagina l'immagine resta com'è finché non arriva la nuova; cambiando documento si svuota.
            Stato = StatoAnteprima.Caricamento;
            Titolo = documento.NomeFile;
            if (pagina == 0)
                Immagine = null;

            if (Ritardo > TimeSpan.Zero)
                await Task.Delay(Ritardo, annullamento);

            if (!files.Esiste(documento.PercorsoRelativo))
            {
                Imposta(StatoAnteprima.Errore, "Il file non si trova più nell'archivio.", null, documento.NomeFile, 1);
                return;
            }

            var risultato = await Task.Run(
                () => generatore.GeneraAsync(files.PercorsoAssoluto(documento.PercorsoRelativo), pagina, annullamento, larghezza),
                annullamento);
            annullamento.ThrowIfCancellationRequested();

            Imposta(risultato.Stato, risultato.Messaggio, risultato.Immagine, documento.NomeFile, risultato.Pagine, risultato.Nota);
            PaginaCorrente = Math.Clamp(pagina, 0, Math.Max(0, risultato.Pagine - 1)) + 1;
        }
        catch (OperationCanceledException)
        {
            // Nel frattempo si è scelto un altro documento (o si è chiuso il pannello): vale quello più recente.
        }
        catch (Exception ex)
        {
            Imposta(StatoAnteprima.Errore, $"Non si riesce a mostrare l'anteprima: {ex.Message}", null, documento.NomeFile, 1);
        }
    }

    /// <summary>
    /// Aggiorna in blocco tutto ciò che il pannello mostra: immagine, nota, messaggio, titolo, pagine e stato.
    /// </summary>
    private void Imposta(StatoAnteprima stato, string messaggio, ImageSource? immagine, string titolo, int pagine, string nota = "")
    {
        Immagine = immagine;
        Nota = nota;
        Messaggio = messaggio;
        Titolo = titolo;
        NumeroPagine = Math.Max(1, pagine);
        PaginaCorrente = Math.Min(PaginaCorrente, NumeroPagine);
        if (stato != StatoAnteprima.Pronta)
            PaginaCorrente = 1;
        Stato = stato;
    }
}
