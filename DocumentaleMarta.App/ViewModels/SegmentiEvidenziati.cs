using System.Text;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.App.ViewModels;

/// <summary>Divide un estratto di ricerca in pezzi normali e pezzi evidenziati (le parole trovate).</summary>
public static class SegmentiEvidenziati
{
    /// <summary>
    /// Divide l'estratto in pezzi di testo normale e pezzi evidenziati, usando i segnaposto che la ricerca mette attorno
    /// alle parole trovate.
    /// </summary>
    public static IReadOnlyList<(string Testo, bool Evidenziato)> Dividi(string? testo)
    {
        var pezzi = new List<(string, bool)>();
        var corrente = new StringBuilder();
        var dentro = false;

        // Chiude il pezzo di testo raccolto finora (se non è vuoto) e lo aggiunge all'elenco.
        void Chiudi()
        {
            if (corrente.Length > 0)
                pezzi.Add((corrente.ToString(), dentro));
            corrente.Clear();
        }

        foreach (var c in testo ?? "")
        {
            if (c == RisultatoRicerca.InizioEvidenza)
            {
                Chiudi();
                dentro = true;
            }
            else if (c == RisultatoRicerca.FineEvidenza)
            {
                Chiudi();
                dentro = false;
            }
            else
            {
                corrente.Append(c);
            }
        }
        Chiudi();

        return pezzi;
    }
}
