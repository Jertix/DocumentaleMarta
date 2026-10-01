using System.ComponentModel;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Modelli;

namespace DocumentaleMarta.Tests;

/// <summary>Risponde alle finestre di dialogo con risposte preparate dal test e ricorda cosa gli è stato mostrato.</summary>
public class FintoDialogService : IDialogService
{
    private readonly Queue<string?> _risposteTesto = new();
    private readonly Queue<string[]> _selezioniFile = new();
    private readonly Queue<Action<NuovaCartellaViewModel>?> _nuoveCartelle = new();
    private readonly Queue<Action<ImpostazioniViewModel>?> _impostazioni = new();
    private readonly Queue<SceltaDuplicati> _sceltaDuplicati = new();

    public bool RispostaConferma { get; set; } = true;

    /// <summary>Risposta alle domande su una proposta (<see cref="Chiedi"/>); il messaggio finisce in <see cref="Domande"/>.</summary>
    public bool RispostaDomanda { get; set; } = true;
    public List<string> Domande { get; } = [];
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

    /// <summary>Prepara cosa farà l'utente nella prossima finestra "Impostazioni" (null = Annulla).</summary>
    public void RispondiImpostazioni(Action<ImpostazioniViewModel>? compila) => _impostazioni.Enqueue(compila);

    /// <summary>Prepara la scelta dell'utente alla prossima domanda sui duplicati (se non c'è: "allega comunque").</summary>
    public void RispondiDuplicati(SceltaDuplicati scelta) => _sceltaDuplicati.Enqueue(scelta);

    /// <summary>Le domande sui duplicati fatte all'utente: quanti file erano duplicati su quanti scelti, e quali.</summary>
    public List<(IReadOnlyList<DuplicatoTrovato> Duplicati, int TotaleFile)> DomandeDuplicati { get; } = [];

    /// <summary>Quante volte è stata aperta la finestra "Impostazioni".</summary>
    public int AperturaImpostazioni { get; private set; }

    public ImpostazioniViewModel? UltimeImpostazioni { get; private set; }

    /// <summary>L'ultima finestra "Informazioni" mostrata.</summary>
    public InformazioniViewModel? UltimeInformazioni { get; private set; }

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

    public bool Chiedi(string titolo, string messaggio)
    {
        Domande.Add(messaggio);
        return RispostaDomanda;
    }

    public void MostraErrore(string messaggio) => Errori.Add(messaggio);

    /// <summary>I messaggi informativi mostrati (titolo, testo).</summary>
    public List<(string Titolo, string Messaggio)> Messaggi { get; } = [];

    public void MostraMessaggio(string titolo, string messaggio) => Messaggi.Add((titolo, messaggio));

    private readonly Queue<string?> _cartelle = new();

    /// <summary>Prepara la cartella che l'utente sceglierà nella prossima finestra di scelta (null = annulla).</summary>
    public void RispondiCartella(string? percorso) => _cartelle.Enqueue(percorso);

    /// <summary>I titoli delle finestre di scelta cartella aperte.</summary>
    public List<string> SceltaCartellaChiesta { get; } = [];

    public string? SelezionaCartella(string titolo, string? percorsoIniziale)
    {
        SceltaCartellaChiesta.Add(titolo);
        return _cartelle.Count > 0 ? _cartelle.Dequeue() : null;
    }

    public IReadOnlyList<string> SelezionaFile(string titolo) =>
        _selezioniFile.Count > 0 ? _selezioniFile.Dequeue() : [];

    public SceltaDuplicati ChiediDuplicati(IReadOnlyList<DuplicatoTrovato> duplicati, int totaleFile)
    {
        DomandeDuplicati.Add((duplicati, totaleFile));
        return _sceltaDuplicati.Count > 0 ? _sceltaDuplicati.Dequeue() : SceltaDuplicati.AllegaComunque;
    }

    public bool MostraImpostazioni(ImpostazioniViewModel modello)
    {
        AperturaImpostazioni++;
        UltimeImpostazioni = modello;

        var compila = _impostazioni.Count > 0 ? _impostazioni.Dequeue() : null;
        if (compila is null)
            return false;

        compila(modello);
        // Come il pulsante "Salva": con dei valori non validi è disattivato, per il test equivale ad annullare.
        return modello.PuoSalvare;
    }

    public void MostraInformazioni(InformazioniViewModel modello) => UltimeInformazioni = modello;

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
