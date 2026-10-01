# Genera il file .ico dell'applicazione a partire dal disegno vettoriale dell'icona (DocumentaleMarta.App\Viste\Icone.xaml).
#
# Da rilanciare ogni volta che si cambia il disegno:
#     powershell -STA -NoProfile -File tools\genera-icona.ps1
#
# Il file risultante (DocumentaleMarta.App\Risorse\Documentale.ico) contiene l'icona a 7 dimensioni, da 16 a 256 pixel,
# ciascuna come immagine PNG dentro il contenitore .ico (formato supportato da Windows Vista in poi).
param(
    [string]$Xaml = (Join-Path $PSScriptRoot "..\DocumentaleMarta.App\Viste\Icone.xaml"),
    [string]$Uscita = (Join-Path $PSScriptRoot "..\DocumentaleMarta.App\Risorse\Documentale.ico")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) {
    throw "WPF richiede STA: esegui con  powershell -STA -NoProfile -File tools\genera-icona.ps1"
}

# 1. Il disegno
$flusso = [IO.File]::OpenRead((Resolve-Path $Xaml))
try { $risorse = [Windows.Markup.XamlReader]::Load($flusso) } finally { $flusso.Dispose() }
$disegno = $risorse["IconaAzienda"]
if ($null -eq $disegno) { throw "Nel file $Xaml manca la risorsa 'IconaAzienda'." }

# 2. Un'immagine PNG per ogni dimensione
$dimensioni = 16, 24, 32, 48, 64, 128, 256
$immagini = New-Object System.Collections.Generic.List[byte[]]
foreach ($lato in $dimensioni) {
    $visual = New-Object Windows.Media.DrawingVisual
    $contesto = $visual.RenderOpen()
    $contesto.DrawImage($disegno, (New-Object Windows.Rect 0, 0, $lato, $lato))
    $contesto.Close()

    $bitmap = New-Object Windows.Media.Imaging.RenderTargetBitmap $lato, $lato, 96, 96, ([Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)

    $codificatore = New-Object Windows.Media.Imaging.PngBitmapEncoder
    $codificatore.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $memoria = New-Object IO.MemoryStream
    $codificatore.Save($memoria)
    $immagini.Add($memoria.ToArray())
}

# 3. Il contenitore .ico: intestazione, una voce di directory per immagine, poi le immagini
$contenitore = New-Object IO.MemoryStream
$scrittore = New-Object IO.BinaryWriter $contenitore
$scrittore.Write([uint16]0)                       # riservato
$scrittore.Write([uint16]1)                       # tipo: icona
$scrittore.Write([uint16]$dimensioni.Count)

$posizione = 6 + 16 * $dimensioni.Count
for ($i = 0; $i -lt $dimensioni.Count; $i++) {
    $lato = $dimensioni[$i]
    $byteLato = if ($lato -ge 256) { 0 } else { $lato }   # nel formato .ico il 256 si scrive 0
    $scrittore.Write([byte]$byteLato)             # larghezza
    $scrittore.Write([byte]$byteLato)             # altezza
    $scrittore.Write([byte]0)                     # colori in tavolozza
    $scrittore.Write([byte]0)                     # riservato
    $scrittore.Write([uint16]1)                   # piani
    $scrittore.Write([uint16]32)                  # bit per pixel
    $scrittore.Write([uint32]$immagini[$i].Length)
    $scrittore.Write([uint32]$posizione)
    $posizione += $immagini[$i].Length
}
foreach ($immagine in $immagini) { $scrittore.Write($immagine) }
$scrittore.Flush()

$cartella = Split-Path -Parent ([IO.Path]::GetFullPath($Uscita))
[IO.Directory]::CreateDirectory($cartella) | Out-Null
[IO.File]::WriteAllBytes([IO.Path]::GetFullPath($Uscita), $contenitore.ToArray())
Write-Output ("Creato {0} ({1} byte, {2} dimensioni)" -f [IO.Path]::GetFullPath($Uscita), $contenitore.Length, $dimensioni.Count)
