using DocumentaleMarta.App.Servizi;

namespace DocumentaleMarta.Tests;

/// <summary>Risponde alle finestre di dialogo con risposte preparate dal test e ricorda cosa gli è stato mostrato.</summary>
public class FintoDialogService : IDialogService
{
    private readonly Queue<string?> _risposteTesto = new();

    public bool RispostaConferma { get; set; } = true;
    public List<string> Errori { get; } = [];
    public List<string> Conferme { get; } = [];
    public List<string> MessaggiChiesti { get; } = [];

    /// <summary>Errori di validazione che l'utente avrebbe visto digitando una risposta non valida.</summary>
    public List<string> ErroriValidazione { get; } = [];

    public void RispondiTesto(string? testo) => _risposteTesto.Enqueue(testo);

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

    public string? ValoreInizialeUltimo { get; private set; }

    public bool Conferma(string titolo, string messaggio)
    {
        Conferme.Add(messaggio);
        return RispostaConferma;
    }

    public void MostraErrore(string messaggio) => Errori.Add(messaggio);
}

public class FintoShellService : IShellService
{
    public List<string> CartelleAperte { get; } = [];

    public void ApriCartella(string percorso) => CartelleAperte.Add(percorso);
}
