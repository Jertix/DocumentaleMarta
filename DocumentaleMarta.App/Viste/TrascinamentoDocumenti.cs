using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

/// <summary>Cosa c'è dentro a un trascinamento: file da Esplora file oppure un documento dell'archivio.</summary>
public static class DatiTrascinati
{
    /// <summary>Vero se il trascinamento contiene file di Esplora file.</summary>
    public static bool HaFile(IDataObject dati) => dati.GetDataPresent(DataFormats.FileDrop);

    /// <summary>I percorsi dei file contenuti nel trascinamento (vuoto se non ce ne sono).</summary>
    public static IReadOnlyList<string> File(IDataObject dati) =>
        HaFile(dati) ? dati.GetData(DataFormats.FileDrop) as string[] ?? [] : [];

    /// <summary>Il documento dell'archivio contenuto nel trascinamento, se c'è.</summary>
    public static DocumentoTrascinato? Documento(IDataObject dati) =>
        dati.GetDataPresent(Trascinamento.FormatoDocumento) ? dati.GetData(Trascinamento.FormatoDocumento) as DocumentoTrascinato : null;
}

/// <summary>Dà al nodo dell'albero su cui si sta per rilasciare qualcosa uno sfondo che lo evidenzia.</summary>
public static class BersaglioTrascinamento
{
    public static readonly DependencyProperty AttivoProperty = DependencyProperty.RegisterAttached(
        "Attivo", typeof(bool), typeof(BersaglioTrascinamento), new PropertyMetadata(false));

    /// <summary>Legge se il nodo è il bersaglio evidenziato del trascinamento.</summary>
    public static bool GetAttivo(DependencyObject oggetto) => (bool)oggetto.GetValue(AttivoProperty);

    /// <summary>Evidenzia (o no) il nodo come bersaglio del trascinamento.</summary>
    public static void SetAttivo(DependencyObject oggetto, bool valore) => oggetto.SetValue(AttivoProperty, valore);
}

/// <summary>Fa partire un trascinamento quando si tiene premuto il mouse su una riga di documento e ci si sposta.</summary>
public static class AvvioTrascinamento
{
    /// <summary>
    /// Le righe della griglia che sono documenti (<see cref="IOrigineDocumento"/>) si possono trascinare su una cartella dell'albero.
    /// Non parte se si clicca su un pulsante della riga.
    /// </summary>
    public static void Collega(DataGrid griglia)
    {
        Point? partenza = null;
        IOrigineDocumento? origine = null;

        griglia.PreviewMouseLeftButtonDown += (_, e) =>
        {
            var sorgente = e.OriginalSource as DependencyObject;
            origine = AlberoVisuale.Antenato<ButtonBase>(sorgente) is null
                ? AlberoVisuale.Antenato<DataGridRow>(sorgente)?.DataContext as IOrigineDocumento
                : null;
            partenza = origine is null ? null : e.GetPosition(griglia);
        };

        griglia.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || partenza is not { } inizio || origine is null)
                return;

            var spostamento = e.GetPosition(griglia) - inizio;
            if (Math.Abs(spostamento.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(spostamento.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;

            var dati = new DataObject(
                Trascinamento.FormatoDocumento, new DocumentoTrascinato(origine.DocumentoId, origine.CartellaOrigineId));
            partenza = null;
            origine = null;
            DragDrop.DoDragDrop(griglia, dati, DragDropEffects.Move);
        };

        griglia.PreviewMouseLeftButtonUp += (_, _) =>
        {
            partenza = null;
            origine = null;
        };
    }
}
