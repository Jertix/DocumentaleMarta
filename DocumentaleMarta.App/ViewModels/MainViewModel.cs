using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.Core.Impostazioni;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.App.ViewModels;

public partial class MainViewModel(
    IArchivioService archivio,
    IArchivioFileService files,
    IDialogService dialog,
    IShellService shell,
    ImpostazioniApp impostazioni) : ObservableObject
{
    private bool _caricamentoInCorso;

    /// <summary>Contiene sempre e solo la radice "Tutti i documenti" (il TreeView vuole una lista).</summary>
    public ObservableCollection<NodoAlberoViewModel> Radici { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HaSelezione), nameof(RadiceSelezionata), nameof(AreaSelezionata),
        nameof(PuoCreareCartella), nameof(PuoModificare),
        nameof(TipoDettaglio), nameof(TitoloDettaglio), nameof(RiepilogoDettaglio), nameof(PercorsoDettaglio))]
    [NotifyCanExecuteChangedFor(nameof(NuovaCartellaCommand), nameof(RinominaCommand),
        nameof(EliminaCommand), nameof(ApriInEsploraCommand))]
    private NodoAlberoViewModel? _nodoSelezionato;

    // ---------- Intestazione e piè di pagina ----------

    public string TestoAzienda
    {
        get
        {
            var a = impostazioni.Azienda;
            var parti = new[]
            {
                a.RagioneSociale,
                Se(a.CodiceFiscale, "C.F. "),
                Se(a.PartitaIva, "P.IVA "),
                a.Indirizzo
            };
            return string.Join("  -  ", parti.Where(p => !string.IsNullOrWhiteSpace(p)));

            static string Se(string valore, string prefisso) =>
                string.IsNullOrWhiteSpace(valore) ? "" : prefisso + valore;
        }
    }

    public string DescrizioneAzienda => impostazioni.Azienda.Descrizione;

    // ---------- Cosa si può fare con la selezione (governa menu e pulsanti) ----------

    public bool HaSelezione => NodoSelezionato is not null;
    public bool RadiceSelezionata => NodoSelezionato?.Tipo == TipoNodo.Radice;
    public bool AreaSelezionata => NodoSelezionato?.Tipo == TipoNodo.Area;

    /// <summary>Da un'area o da una sua cartella si può aggiungere una cartella (nella stessa area).</summary>
    public bool PuoCreareCartella => NodoSelezionato?.Tipo is TipoNodo.Area or TipoNodo.Cartella;

    public bool PuoModificare => NodoSelezionato?.Tipo is TipoNodo.Area or TipoNodo.Cartella;

    // ---------- Pannello di destra ----------

    public string TipoDettaglio => NodoSelezionato?.Tipo switch
    {
        TipoNodo.Radice => "Archivio",
        TipoNodo.Area => "Area",
        TipoNodo.Cartella => "Cartella",
        _ => ""
    };

    public string TitoloDettaglio => NodoSelezionato?.Nome ?? "";

    public string RiepilogoDettaglio => NodoSelezionato switch
    {
        { Tipo: TipoNodo.Radice } n =>
            $"{Conta(n.Figli.Count, "area", "aree")}  ·  {Conta(n.NumeroCartelle, "cartella", "cartelle")}  ·  {Conta(n.NumeroDocumenti, "documento", "documenti")}",
        { Tipo: TipoNodo.Area } n =>
            $"{Conta(n.NumeroCartelle, "cartella", "cartelle")}  ·  {Conta(n.NumeroDocumenti, "documento", "documenti")}",
        { Tipo: TipoNodo.Cartella } n => Conta(n.NumeroDocumenti, "documento", "documenti"),
        _ => ""
    };

    public string PercorsoDettaglio =>
        NodoSelezionato is { } n ? files.PercorsoAssoluto(n.PercorsoRelativo) : "";

    // ---------- Caricamento ----------

    public Task InizializzaAsync() => EseguiAsync(() => RicaricaAsync());

    /// <summary>
    /// Rilegge l'albero dal database conservando i rami aperti. Seleziona <paramref name="chiaveDaSelezionare"/>
    /// (o, se manca, la selezione corrente; se non esiste più, la radice).
    /// </summary>
    private async Task RicaricaAsync(string? chiaveDaSelezionare = null)
    {
        var aree = await archivio.CaricaAlberoAsync();

        var espansi = Radici.SelectMany(r => r.ConDiscendenti()).Where(n => n.IsExpanded).Select(n => n.Chiave).ToHashSet();
        var primoCaricamento = Radici.Count == 0;
        var chiave = chiaveDaSelezionare ?? NodoSelezionato?.Chiave;

        _caricamentoInCorso = true;
        NodoAlberoViewModel radice;
        try
        {
            radice = new NodoAlberoViewModel(TipoNodo.Radice, 0, impostazioni.NomeRadice, "", null, Seleziona)
            {
                NumeroCartelle = aree.Sum(a => a.Cartelle.Count),
                NumeroDocumenti = aree.Sum(a => a.Cartelle.Sum(c => c.NumeroDocumenti))
            };
            foreach (var area in aree)
            {
                var nodoArea = new NodoAlberoViewModel(TipoNodo.Area, area.Id, area.Nome, area.PercorsoRelativo, radice, Seleziona)
                {
                    NumeroCartelle = area.Cartelle.Count,
                    NumeroDocumenti = area.Cartelle.Sum(c => c.NumeroDocumenti)
                };
                foreach (var cartella in area.Cartelle)
                    nodoArea.Figli.Add(new NodoAlberoViewModel(
                        TipoNodo.Cartella, cartella.Id, cartella.Titolo, cartella.PercorsoRelativo, nodoArea, Seleziona)
                    { NumeroDocumenti = cartella.NumeroDocumenti });
                radice.Figli.Add(nodoArea);
            }

            foreach (var nodo in radice.ConDiscendenti())
                nodo.IsExpanded = espansi.Contains(nodo.Chiave);
            if (primoCaricamento)
                radice.IsExpanded = true;

            Radici.Clear();
            Radici.Add(radice);
        }
        finally
        {
            _caricamentoInCorso = false;
        }

        var daSelezionare = radice.ConDiscendenti().FirstOrDefault(n => n.Chiave == chiave) ?? radice;
        for (var p = daSelezionare.Padre; p is not null; p = p.Padre)
            p.IsExpanded = true;

        // I nodi sono nuovi, quindi IsSelected passa sempre da false a true e scatta Seleziona.
        NodoSelezionato = null;
        daSelezionare.IsSelected = true;
    }

    private void Seleziona(NodoAlberoViewModel nodo)
    {
        if (_caricamentoInCorso)
            return;

        var precedente = NodoSelezionato;
        NodoSelezionato = nodo;
        if (precedente is not null && !ReferenceEquals(precedente, nodo))
            precedente.IsSelected = false;
    }

    // ---------- Comandi ----------

    [RelayCommand]
    private async Task NuovaAreaAsync()
    {
        var nome = dialog.ChiediTesto(
            "Nuova area", "Nome della nuova area (ad esempio Fatture o INPS):", "",
            testo => ErroreNomeArea(testo, escludi: null));
        if (nome is null)
            return;

        await EseguiAsync(async () =>
        {
            var id = await archivio.CreaAreaAsync(nome);
            await RicaricaAsync(NodoAlberoViewModel.CreaChiave(TipoNodo.Area, id));
        });
    }

    [RelayCommand(CanExecute = nameof(PuoCreareCartella))]
    private async Task NuovaCartellaAsync()
    {
        var area = NodoSelezionato?.Tipo == TipoNodo.Area ? NodoSelezionato : NodoSelezionato?.Padre;
        if (area is null)
            return;

        var titolo = dialog.ChiediTesto(
            "Nuova cartella", $"Titolo della nuova cartella in «{area.Nome}»:", "",
            testo => ValidazioneNomi.Errore(testo, ValidazioneNomi.LunghezzaMassimaTitolo));
        if (titolo is null)
            return;

        await EseguiAsync(async () =>
        {
            var id = await archivio.CreaCartellaAsync(area.Id, titolo);
            await RicaricaAsync(NodoAlberoViewModel.CreaChiave(TipoNodo.Cartella, id));
        });
    }

    [RelayCommand(CanExecute = nameof(PuoModificare))]
    private async Task RinominaAsync()
    {
        if (NodoSelezionato is not { Tipo: not TipoNodo.Radice } nodo)
            return;

        var eArea = nodo.Tipo == TipoNodo.Area;
        var nuovo = dialog.ChiediTesto(
            eArea ? "Rinomina area" : "Rinomina cartella",
            eArea ? "Nuovo nome dell'area:" : "Nuovo titolo della cartella:",
            nodo.Nome,
            testo => eArea
                ? ErroreNomeArea(testo, escludi: nodo)
                : ValidazioneNomi.Errore(testo, ValidazioneNomi.LunghezzaMassimaTitolo));
        if (nuovo is null)
            return;

        await EseguiAsync(async () =>
        {
            if (eArea)
                await archivio.RinominaAreaAsync(nodo.Id, nuovo);
            else
                await archivio.RinominaCartellaAsync(nodo.Id, nuovo);
            await RicaricaAsync(nodo.Chiave);
        });
    }

    [RelayCommand(CanExecute = nameof(PuoModificare))]
    private async Task EliminaAsync()
    {
        if (NodoSelezionato is not { Tipo: not TipoNodo.Radice } nodo)
            return;

        var eArea = nodo.Tipo == TipoNodo.Area;
        if (!dialog.Conferma(eArea ? "Elimina area" : "Elimina cartella", MessaggioEliminazione(nodo)))
            return;

        await EseguiAsync(async () =>
        {
            if (eArea)
                await archivio.EliminaAreaAsync(nodo.Id);
            else
                await archivio.EliminaCartellaAsync(nodo.Id);

            var successivo = nodo.Padre ?? Radici.FirstOrDefault();
            await RicaricaAsync(successivo?.Chiave);
        });
    }

    [RelayCommand(CanExecute = nameof(HaSelezione))]
    private void ApriInEsplora()
    {
        if (NodoSelezionato is not { } nodo)
            return;

        var percorso = files.PercorsoAssoluto(nodo.PercorsoRelativo);
        if (!Directory.Exists(percorso))
        {
            dialog.MostraErrore($"La cartella non esiste sul disco:\n{percorso}");
            return;
        }

        try
        {
            shell.ApriCartella(percorso);
        }
        catch (Win32Exception ex)
        {
            dialog.MostraErrore($"Impossibile aprire Esplora file: {ex.Message}");
        }
    }

    // ---------- Supporto ----------

    private NodoAlberoViewModel? Radice => Radici.FirstOrDefault();

    private string? ErroreNomeArea(string testo, NodoAlberoViewModel? escludi)
    {
        if (ValidazioneNomi.Errore(testo, ValidazioneNomi.LunghezzaMassimaArea) is { } errore)
            return errore;

        var nome = testo.Trim();
        var giaUsato = Radice?.Figli.Any(a =>
            !ReferenceEquals(a, escludi) && string.Equals(a.Nome, nome, StringComparison.OrdinalIgnoreCase)) == true;
        return giaUsato ? $"Esiste già un'area chiamata «{nome}»." : null;
    }

    private static string MessaggioEliminazione(NodoAlberoViewModel nodo)
    {
        var contenuto = new List<string>();
        if (nodo.Tipo == TipoNodo.Area && nodo.NumeroCartelle > 0)
            contenuto.Add(Conta(nodo.NumeroCartelle, "cartella", "cartelle"));
        if (nodo.NumeroDocumenti > 0)
            contenuto.Add(Conta(nodo.NumeroDocumenti, "documento", "documenti"));

        var tipo = nodo.Tipo == TipoNodo.Area ? "l'area" : "la cartella";
        var testo = $"Eliminare {tipo} «{nodo.Nome}»?";
        if (contenuto.Count > 0)
            testo += $"\n\nVerranno eliminati anche: {string.Join(" e ", contenuto)}.";
        return testo + "\n\nI file vengono spostati nel Cestino di Windows.";
    }

    private static string Conta(int numero, string singolare, string plurale) =>
        $"{numero} {(numero == 1 ? singolare : plurale)}";

    /// <summary>Esegue un'operazione trasformando gli errori prevedibili in messaggi per l'utente.</summary>
    private async Task EseguiAsync(Func<Task> operazione)
    {
        try
        {
            await operazione();
        }
        catch (ArchivioException ex)
        {
            dialog.MostraErrore(ex.Message);
            await RicaricaAsync(); // l'albero mostrato potrebbe essere vecchio (es. elemento già eliminato)
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            dialog.MostraErrore(
                $"Operazione non riuscita: {ex.Message}\n\nControlla che i file e le cartelle non siano aperti in un altro programma.");
            await RicaricaAsync();
        }
        catch (OperationCanceledException)
        {
            // L'utente ha annullato una finestra di Windows (es. il Cestino): nessun messaggio.
        }
    }
}
