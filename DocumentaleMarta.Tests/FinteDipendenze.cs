using System.ComponentModel;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.Tests;

/// <summary>Risponde alle finestre di dialogo con risposte preparate dal test e ricorda cosa gli è stato mostrato.</summary>
public class FintoDialogService : IDialogService
{
    private readonly Queue<string?> _risposteTesto = new();
    private readonly Queue<string[]> _selezioniFile = new();
    private readonly Queue<Action<NuovaCartellaViewModel>?> _nuoveCartelle = new();

    public bool RispostaConferma { get; set; } = true;
    public List<string> Errori { get; } = [];
    public List<string> Conferme { get; } = [];
    public List<string> MessaggiChiesti { get; } = [];

    /// <summary>Errori di validazione che l'utente avrebbe visto digitando una risposta non valida.</summary>
    public List<string> ErroriValidazione { get; } = [];

    public string? ValoreInizialeUltimo { get; private set; }

    /// <summary>Quante volte è stata aperta la finestra "Nuova cartella".</summary>
    public int AperturaNuovaCartella { get; private set; }

    public NuovaCartellaViewModel? UltimaNuovaCartella { get; private set; }

    public void RispondiTesto(string? testo) => _risposteTesto.Enqueue(testo);

    /// <summary>Prepara i file che l'utente sceglierà nella prossima finestra di selezione (nessuno = annulla).</summary>
    public void RispondiFile(params string[] percorsi) => _selezioniFile.Enqueue(percorsi);

    /// <summary>Prepara cosa farà l'utente nella prossima finestra "Nuova cartella" (null = Annulla).</summary>
    public void RispondiNuovaCartella(Action<NuovaCartellaViewModel>? compila) => _nuoveCartelle.Enqueue(compila);

    public string? ChiediTesto(string titolo, string messaggio, string valoreIniziale, Func<string, string?> validatore)
    {
        MessaggiChiesti.Add(messaggio);
        ValoreInizialeUltimo = valoreIniziale;

        var risposta = _risposteTesto.Count > 0 ? _risposteTesto.Dequeue() : null;
        if (risposta is null)
            return null; // l'utente annulla

        // Se il testo non è valido il pulsante OK resta disattivato: per il chiamante è come se annullasse.
        if (validatore(risposta) is { } errore)
        {
            ErroriValidazione.Add(errore);
            return null;
        }
        return risposta.Trim();
    }

    public bool Conferma(string titolo, string messaggio)
    {
        Conferme.Add(messaggio);
        return RispostaConferma;
    }

    public void MostraErrore(string messaggio) => Errori.Add(messaggio);

    public IReadOnlyList<string> SelezionaFile(string titolo) =>
        _selezioniFile.Count > 0 ? _selezioniFile.Dequeue() : [];

    public bool MostraNuovaCartella(NuovaCartellaViewModel modello)
    {
        AperturaNuovaCartella++;
        UltimaNuovaCartella = modello;

        var compila = _nuoveCartelle.Count > 0 ? _nuoveCartelle.Dequeue() : null;
        if (compila is null)
            return false;

        compila(modello);
        // Come il pulsante "Crea": se i dati non sono validi la finestra resta aperta, per il test equivale ad annullare.
        return modello.Convalida();
    }
}

public class FintoShellService : IShellService
{
    public List<string> CartelleAperte { get; } = [];
    public List<string> FileAperti { get; } = [];
    public List<string> FileMostrati { get; } = [];

    /// <summary>Simula un tipo di file senza programma associato.</summary>
    public bool NessunProgrammaAssociato { get; set; }

    public void ApriCartella(string percorso) => CartelleAperte.Add(percorso);

    public void ApriFile(string percorso)
    {
        if (NessunProgrammaAssociato)
            throw new Win32Exception("Nessuna applicazione associata.");
        FileAperti.Add(percorso);
    }

    public void MostraFileInEsplora(string percorso) => FileMostrati.Add(percorso);
}
