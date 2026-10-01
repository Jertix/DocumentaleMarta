using System.Windows;
using System.Windows.Controls;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

/// <summary>
/// La griglia dei documenti di tutto l'archivio, di un'area, dell'archivio completati o i risultati di una ricerca.
/// </summary>
public partial class ElencoView : UserControl
{
    /// <summary>
    /// Crea la griglia dei documenti: mostra o nasconde le colonne secondo il contenuto (area, «Trovato», date), rende
    /// trascinabili le righe e collega il doppio clic che porta alla cartella.
    /// </summary>
    public ElencoView()
    {
        InitializeComponent();

        DataContextChanged += (_, e) =>
        {
            var elenco = e.NewValue as ElencoDocumentiViewModel;

            // La colonna "Area" ha senso solo nell'elenco di tutto l'archivio: in quello di un'area sarebbe sempre uguale.
            ColonnaArea.Visibility = elenco is { MostraArea: false } ? Visibility.Collapsed : Visibility.Visible;

            // Nei risultati di una ricerca c'è la colonna "Trovato"; per fargli posto si tolgono le date, meno utili lì.
            var inRicerca = elenco is { MostraTrovato: true };
            ColonnaTrovato.Visibility = inRicerca ? Visibility.Visible : Visibility.Collapsed;
            ColonnaScadenza.Visibility = inRicerca ? Visibility.Collapsed : Visibility.Visible;
            ColonnaCaricato.Visibility = inRicerca ? Visibility.Collapsed : Visibility.Visible;
        };

        // Le righe si possono trascinare su una cartella dell'albero per spostare il documento.
        AvvioTrascinamento.Collega(Griglia);

        // Doppio clic su una riga: si va alla cartella del documento.
        DoppioClicGriglia.Collega<DocumentoElencoViewModel>(Griglia, riga => riga.VaiAllaCartellaCommand.Execute(null));
    }
}
