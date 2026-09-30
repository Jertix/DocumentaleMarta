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

    static TestoEvidenziato() => SfondoEvidenza.Freeze();

    public static readonly DependencyProperty TestoProperty = DependencyProperty.RegisterAttached(
        "Testo", typeof(string), typeof(TestoEvidenziato), new PropertyMetadata(null, AlCambioDelTesto));

    public static string? GetTesto(DependencyObject oggetto) => (string?)oggetto.GetValue(TestoProperty);

    public static void SetTesto(DependencyObject oggetto, string? valore) => oggetto.SetValue(TestoProperty, valore);

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
