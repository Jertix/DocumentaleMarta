using DocumentaleMarta.App.ViewModels;
using DocumentaleMarta.Core.Modelli;

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

    /// <summary>Domanda sì/no su una proposta (non su un'azione distruttiva): la risposta predefinita è Sì.</summary>
    bool Chiedi(string titolo, string messaggio);

    void MostraErrore(string messaggio);

    /// <summary>Un messaggio informativo con il solo pulsante OK (per esempio "Backup completato").</summary>
    void MostraMessaggio(string titolo, string messaggio);

    /// <summary>Scelta di una cartella. Restituisce null se l'utente annulla.</summary>
    string? SelezionaCartella(string titolo, string? percorsoIniziale);

    /// <summary>Scelta di uno o più file da allegare. Lista vuota se l'utente annulla.</summary>
    IReadOnlyList<string> SelezionaFile(string titolo);

    /// <summary>
    /// Dice all'utente che alcuni file da allegare sono già nell'archivio (e dove) e chiede cosa fare.
    /// Se i duplicati sono tutti i file scelti, "salta" non viene offerto.
    /// </summary>
    SceltaDuplicati ChiediDuplicati(IReadOnlyList<DuplicatoTrovato> duplicati, int totaleFile);

    /// <summary>Mostra la finestra di creazione di una cartella. True se l'utente conferma.</summary>
    bool MostraNuovaCartella(NuovaCartellaViewModel modello);

    /// <summary>Mostra la finestra delle impostazioni. True se l'utente conferma (e i valori sono validi).</summary>
    bool MostraImpostazioni(ImpostazioniViewModel modello);

    /// <summary>Mostra la finestra "Informazioni".</summary>
    void MostraInformazioni(InformazioniViewModel modello);
}
