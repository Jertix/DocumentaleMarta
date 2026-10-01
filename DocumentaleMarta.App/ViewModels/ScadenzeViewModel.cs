using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using DocumentaleMarta.Core.Modelli;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>Una riga dell'elenco "Scadenze": una cartella non completata con la scadenza vicina o già passata.</summary>
public class ScadenzaViewModel(CartellaScadenza dati, StatoAvviso avviso, string quando, Action<int> vai)
{
    public int CartellaId { get; } = dati.CartellaId;
    public string Titolo { get; } = dati.Titolo;
    public string NomeArea { get; } = dati.NomeArea;
    public DateTime DataScadenza { get; } = dati.DataScadenza.ToDateTime(TimeOnly.MinValue);
    public int NumeroDocumenti { get; } = dati.NumeroDocumenti;
    public StatoAvviso Avviso { get; } = avviso;

    /// <summary>"Scaduta da 3 giorni", "Scade domani", "Scade tra 5 giorni".</summary>
    public string Quando { get; } = quando;

    /// <summary>Seleziona la cartella nell'albero (pulsante "Vai alla cartella" e doppio clic sulla riga).</summary>
    public IRelayCommand VaiAllaCartellaCommand { get; } = new RelayCommand(() => vai(dati.CartellaId));
}

/// <summary>Il contenuto del nodo speciale "Scadenze": tutte le cartelle che richiedono attenzione, dalla più urgente.</summary>
public class ScadenzeViewModel(IArchivioService archivio, AlertService avvisi)
{
    public ObservableCollection<ScadenzaViewModel> Righe { get; } = [];

    public string TestoVuoto => $"Nessuna scadenza nei prossimi {avvisi.SogliaArancioneGiorni} giorni.";

    /// <summary>L'utente vuole vedere una cartella: serve selezionarla nell'albero (id cartella).</summary>
    public event Action<int>? VaiAllaCartellaRichiesto;

    /// <summary>
    /// Riempie l'elenco con le cartelle che richiedono attenzione: il database dà tutte quelle con una scadenza, il
    /// servizio degli avvisi sceglie quali segnalare.
    /// </summary>
    public async Task CaricaAsync()
    {
        var scadenze = await archivio.CaricaScadenzeAsync();

        Righe.Clear();
        foreach (var scadenza in scadenze)
        {
            // Il database dà tutte le cartelle con una scadenza: quali vanno segnalate lo decide il servizio degli avvisi.
            var stato = avvisi.Valuta(scadenza.DataScadenza, completato: false);
            if (stato == StatoAvviso.Nessuno)
                continue;
            Righe.Add(new ScadenzaViewModel(scadenza, stato, avvisi.Descrivi(scadenza.DataScadenza), id => VaiAllaCartellaRichiesto?.Invoke(id)));
        }
    }
}
