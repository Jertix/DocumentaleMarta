namespace DocumentaleMarta.App.Servizi;

/// <summary>Finestre di dialogo che i ViewModel possono chiedere senza conoscere WPF (così si testano).</summary>
public interface IDialogService
{
    /// <summary>
    /// Chiede un testo all'utente. Il pulsante OK resta disattivato finché <paramref name="validatore"/>
    /// restituisce un messaggio d'errore. Restituisce null se l'utente annulla.
    /// </summary>
    string? ChiediTesto(string titolo, string messaggio, string valoreIniziale, Func<string, string?> validatore);

    /// <summary>Domanda sì/no per azioni distruttive. La risposta predefinita è No.</summary>
    bool Conferma(string titolo, string messaggio);

    void MostraErrore(string messaggio);
}
