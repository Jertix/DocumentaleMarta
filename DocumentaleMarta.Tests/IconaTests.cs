using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DocumentaleMarta.Tests;

public class IconaTests
{
    private static readonly Uri UriIcone = new("/DocumentaleMarta.App;component/Viste/Icone.xaml", UriKind.Relative);

    /// <summary>Carica il disegno come fanno le finestre dell'app per i loro file XAML (funziona anche senza un'applicazione avviata).</summary>
    private static DrawingImage CaricaIcona() =>
        (DrawingImage)((ResourceDictionary)Application.LoadComponent(UriIcone))["IconaAzienda"];

    /// <summary>Disegna l'icona vettoriale alla dimensione indicata e ne restituisce i pixel (Pbgra32).</summary>
    internal static (byte[] Pixel, RenderTargetBitmap Immagine) Disegna(int lato)
    {
        (byte[], RenderTargetBitmap)? risultato = null;
        VisteTests.InSta(() =>
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
                dc.DrawImage(CaricaIcona(), new Rect(0, 0, lato, lato));
            var bitmap = new RenderTargetBitmap(lato, lato, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            var pixel = new byte[lato * lato * 4];
            bitmap.CopyPixels(pixel, lato * 4, 0);
            risultato = (pixel, bitmap);
        });
        return risultato!.Value;
    }

    [Fact]
    public void LIconaVettoriale_SiCaricaESiDisegna_AOgniDimensione()
    {
        foreach (var lato in new[] { 16, 24, 32, 48, 64, 128, 256 })
        {
            var (pixel, _) = Disegna(lato);

            // Niente di vuoto: il quadrato arrotondato riempie il centro, gli angoli restano trasparenti.
            var centro = (lato / 2 * lato + lato / 2) * 4;
            Assert.True(pixel[centro + 3] == 255, $"Il centro dell'icona {lato}px deve essere opaco.");
            Assert.True(pixel[3] < 255, $"L'angolo dell'icona {lato}px deve essere arrotondato (trasparente).");
        }
    }

    [Fact]
    public void LIcona_HaPiuColori_NonEUnQuadratoUniforme()
    {
        var (pixel, _) = Disegna(128);

        var colori = new HashSet<int>();
        for (var i = 0; i < pixel.Length; i += 4)
            colori.Add(pixel[i] << 16 | pixel[i + 1] << 8 | pixel[i + 2]);

        Assert.True(colori.Count > 8, "Nell'icona devono esserci incudine, martello, scintille e sfondo.");
    }

    // ---------- Il file .ico dell'applicazione ----------

    /// <summary>Il file .ico com'è incorporato nell'applicazione compilata (lo stesso che usano l'eseguibile e le finestre).</summary>
    private static byte[] LeggiIco()
    {
        var risorsa = Application.GetResourceStream(new Uri("/DocumentaleMarta.App;component/Risorse/Documentale.ico", UriKind.Relative))
                      ?? throw new InvalidOperationException("L'icona non è incorporata nell'applicazione.");
        using var flusso = risorsa.Stream;
        using var memoria = new MemoryStream();
        flusso.CopyTo(memoria);
        return memoria.ToArray();
    }

    [Fact]
    public void IlFileIco_ContieneTutteLeDimensioni_ComeImmaginiPng()
    {
        var byte_ = LeggiIco();

        Assert.Equal(0, BitConverter.ToUInt16(byte_, 0));   // riservato
        Assert.Equal(1, BitConverter.ToUInt16(byte_, 2));   // tipo: icona
        var numero = BitConverter.ToUInt16(byte_, 4);
        Assert.Equal(7, numero);

        var dimensioni = new List<int>();
        for (var i = 0; i < numero; i++)
        {
            var voce = 6 + 16 * i;
            dimensioni.Add(byte_[voce] == 0 ? 256 : byte_[voce]);
            var lunghezza = (int)BitConverter.ToUInt32(byte_, voce + 8);
            var posizione = (int)BitConverter.ToUInt32(byte_, voce + 12);
            // Ogni immagine è un PNG vero (firma 0x89 'P' 'N' 'G') e sta tutta dentro il file.
            Assert.Equal([0x89, (byte)'P', (byte)'N', (byte)'G'], byte_[posizione..(posizione + 4)]);
            Assert.True(posizione + lunghezza <= byte_.Length);
        }
        Assert.Equal([16, 24, 32, 48, 64, 128, 256], dimensioni);
    }

    [Fact]
    public void IlFileIco_SiLeggeConIlDecodificatoreDiWindows_EHaLeDimensioniGiuste()
    {
        VisteTests.InSta(() =>
        {
            using var stream = new MemoryStream(LeggiIco());
            var decodificatore = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

            Assert.Equal([16, 24, 32, 48, 64, 128, 256], decodificatore.Frames.Select(f => f.PixelWidth).OrderBy(x => x));
            Assert.All(decodificatore.Frames, f => Assert.Equal(f.PixelWidth, f.PixelHeight));
        });
    }

    [Fact]
    public void IlFileIco_EAggiornato_ConIlDisegnoVettoriale()
    {
        // Se si cambia Icone.xaml senza rigenerare l'.ico, le due icone divergono: qui lo si scopre.
        (byte[] atteso, _) = Disegna(64);

        byte[]? dalFile = null;
        VisteTests.InSta(() =>
        {
            using var stream = new MemoryStream(LeggiIco());
            var fotogramma = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad)
                .Frames.Single(f => f.PixelWidth == 64);
            var convertito = new FormatConvertedBitmap(fotogramma, PixelFormats.Pbgra32, null, 0);
            dalFile = new byte[64 * 64 * 4];
            convertito.CopyPixels(dalFile, 64 * 4, 0);
        });

        // Piccole differenze di arrotondamento ammesse; un disegno diverso ne farebbe moltissime.
        var diversi = 0;
        for (var i = 0; i < atteso.Length; i++)
            if (Math.Abs(atteso[i] - dalFile![i]) > 8)
                diversi++;
        Assert.True(diversi < atteso.Length * 0.01, $"L'icona .ico non corrisponde più al disegno vettoriale ({diversi} valori diversi): rilancia tools\\genera-icona.ps1.");
    }

    [Fact]
    public void SalvaLImmagine_SeRichiesto()
    {
        if (Environment.GetEnvironmentVariable("DOCUMENTALE_TEST_IMMAGINI") is not { Length: > 0 } cartella)
            return;

        Directory.CreateDirectory(cartella);
        foreach (var lato in new[] { 16, 32, 64, 256 })
        {
            var (_, immagine) = Disegna(lato);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(immagine));
            using var file = File.Create(Path.Combine(cartella, $"icona-{lato}.png"));
            encoder.Save(file);
        }
    }
}
