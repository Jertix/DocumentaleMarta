using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentaleMarta.App.Servizi;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>Un file scelto per essere allegato, non ancora copiato nell'archivio.</summary>
public class AllegatoInAttesa
{
    public AllegatoInAttesa(string percorso)
    {
        Percorso = percorso;
        NomeFile = Path.GetFileName(percorso);
        try { DimensioneTesto = FormatiTesto.Dimensione(new FileInfo(percorso).Length); }
        catch (IOException) { DimensioneTesto = ""; }
        catch (UnauthorizedAccessException) { DimensioneTesto = ""; }
    }

    public string Percorso { get; }
    public string NomeFile { get; }
    public string DimensioneTesto { get; }
}

/// <summary>Dati della finestra "Nuova cartella". Non si crea nulla su disco finché l'utente non conferma.</summary>
public partial class NuovaCartellaViewModel(IDialogService dialog, string nomeArea) : CartellaCampiViewModel
{
    private string _ultimoTitoloAutomatico = "";

    public string NomeArea { get; } = nomeArea;

    public ObservableCollection<AllegatoInAttesa> Allegati { get; } = [];

    [ObservableProperty]
    private string _errore = "";

    public IReadOnlyList<string> PercorsiFile => Allegati.Select(a => a.Percorso).ToList();

    [RelayCommand]
    private void Allega() => AggiungiAllegati(dialog.SelezionaFile("Allega documenti"));

    /// <summary>Aggiunge i file all'elenco da allegare (scelti con la finestra o trascinati da Esplora file).</summary>
    public void AggiungiAllegati(IEnumerable<string> percorsi)
    {
        var (file, cartelleEscluse) = Trascinamento.SoloFile(percorsi);
        if (cartelleEscluse > 0)
            dialog.MostraErrore(Trascinamento.MessaggioCartelleEscluse(cartelleEscluse));

        foreach (var percorso in file)
        {
            // Lo stesso file scelto due volte non va allegato due volte.
            if (!Allegati.Any(a => string.Equals(a.Percorso, percorso, StringComparison.OrdinalIgnoreCase)))
                Allegati.Add(new AllegatoInAttesa(percorso));
        }
        AggiornaTitoloAutomatico();
    }

    [RelayCommand]
    private void Rimuovi(AllegatoInAttesa? allegato)
    {
        if (allegato is null || !Allegati.Remove(allegato))
            return;
        AggiornaTitoloAutomatico();
    }

    /// <summary>
    /// Finché l'utente non ha scritto un titolo suo, il titolo segue i file allegati
    /// (un file: il suo nome; più file: "Primo (+N)"). Se l'utente lo scrive o lo modifica, non lo si tocca più.
    /// </summary>
    private void AggiornaTitoloAutomatico()
    {
        if (!string.IsNullOrWhiteSpace(Titolo) && Titolo != _ultimoTitoloAutomatico)
            return;

        _ultimoTitoloAutomatico = TitoloAutomatico.Genera(Allegati.Select(a => a.NomeFile).ToList());
        Titolo = _ultimoTitoloAutomatico;
    }

    /// <summary>Controlla i dati prima della creazione. Se non sono validi mette il motivo in <see cref="Errore"/>.</summary>
    public bool Convalida()
    {
        Errore = string.IsNullOrWhiteSpace(Titolo)
            ? "Scrivi un titolo oppure allega almeno un documento."
            : ValidazioneNomi.Errore(Titolo, ValidazioneNomi.LunghezzaMassimaTitolo) ?? "";
        return Errore.Length == 0;
    }
}
