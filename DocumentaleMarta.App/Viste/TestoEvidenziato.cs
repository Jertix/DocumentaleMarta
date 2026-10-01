using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using DocumentaleMarta.App.ViewModels;

namespace DocumentaleMarta.App.Viste;

/// <summary>
/// Proprietà da collegare a un TextBlock: il testo dell'estratto di ricerca, con le parole trovate
/// in grassetto su sfondo giallo chiaro.
/// </summary>
public static class TestoEvidenziato
{
    private static readonly Brush SfondoEvidenza = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0x82));

    /// <summary>Congela il pennello dell'evidenziazione (non cambia mai, e così costa meno).</summary>
    static TestoEvidenziato() => SfondoEvidenza.Freeze();

    public static readonly DependencyProperty TestoProperty = DependencyProperty.RegisterAttached(
        "Testo", typeof(string), typeof(TestoEvidenziato), new PropertyMetadata(null, AlCambioDelTesto));

    /// <summary>Legge il testo da evidenziare collegato a un elemento.</summary>
    public static string? GetTesto(DependencyObject oggetto) => (string?)oggetto.GetValue(TestoProperty);

    /// <summary>Collega a un elemento il testo da evidenziare.</summary>
    public static void SetTesto(DependencyObject oggetto, string? valore) => oggetto.SetValue(TestoProperty, valore);

    /// <summary>
    /// Quando cambia il testo ricostruisce il contenuto del blocco: pezzi normali e pezzi evidenziati (grassetto su fondo
    /// giallo).
    /// </summary>
    private static void AlCambioDelTesto(DependencyObject oggetto, DependencyPropertyChangedEventArgs e)
    {
        if (oggetto is not TextBlock blocco)
            return;

        blocco.Inlines.Clear();
        foreach (var (testo, evidenziato) in SegmentiEvidenziati.Dividi(e.NewValue as string))
        {
            var pezzo = new Run(testo);
            if (evidenziato)
            {
                pezzo.FontWeight = FontWeights.SemiBold;
                pezzo.Background = SfondoEvidenza;
            }
            blocco.Inlines.Add(pezzo);
        }
    }
}
