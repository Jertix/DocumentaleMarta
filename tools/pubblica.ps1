# Prepara la versione da distribuire: lancia i test, pubblica il programma in un solo file .exe e lo comprime in uno ZIP.
#
#     powershell -NoProfile -File tools\pubblica.ps1                 (versione autonoma: funziona su qualsiasi Windows)
#     powershell -NoProfile -File tools\pubblica.ps1 -SenzaRuntime   (versione leggera: serve il .NET 10 Desktop Runtime sul PC)
#     powershell -NoProfile -File tools\pubblica.ps1 -SaltaTest      (senza i test, per rifare solo il pacchetto)
#
# La versione si legge da DocumentaleMarta.App\DocumentaleMarta.App.csproj (<Version>): va aggiornata prima di pubblicare.
# Il risultato sta in publish\ (cartella ignorata da git):
#     publish\Documentale-<versione>-win-x64\            il programma (DocumentaleMarta.App.exe)
#     publish\Documentale-<versione>-win-x64.zip         lo ZIP da distribuire
#     publish\Documentale-<versione>-win-x64.zip.sha256  l'impronta SHA-256 dello ZIP, per controllare che non si rovini
#     publish\Documentale-<versione>-win-x64-note.md     le note della versione, prese da CHANGELOG.md
#
# Prima di pubblicare serve la sezione "## <versione>" in CHANGELOG.md: senza, lo script si ferma.
#
# Il programma non si installa: si estrae lo ZIP in una cartella e si avvia DocumentaleMarta.App.exe. I dati (archivio,
# database, impostazioni) stanno fuori dalla cartella del programma, quindi aggiornare vuol dire sostituire l'.exe.
param(
    [switch]$SenzaRuntime,
    [switch]$SaltaTest
)

$ErrorActionPreference = "Stop"
$radice = Split-Path $PSScriptRoot -Parent
Set-Location $radice

$progetto = "DocumentaleMarta.App\DocumentaleMarta.App.csproj"

# 1. La versione, dal file del progetto
$versione = ([xml](Get-Content $progetto -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $versione) { throw "Nel file $progetto manca <Version>." }

$nome = "Documentale-$versione-win-x64" + $(if ($SenzaRuntime) { "-senza-runtime" } else { "" })
$cartella = Join-Path $radice "publish\$nome"
$zip = Join-Path $radice "publish\$nome.zip"
Write-Host "Versione $versione  ->  $nome" -ForegroundColor Cyan

# 2. Un avviso se il codice non e' tutto salvato in git: la versione pubblicata deve corrispondere a un commit
if (Get-Command git -ErrorAction SilentlyContinue) {
    if (git status --porcelain) {
        Write-Warning "Ci sono modifiche non salvate con git: questa versione non corrisponde a un commit."
    }
    Write-Host ("Commit: " + (git rev-parse --short HEAD))
}

# 3. Le note della versione: la sezione "## <versione>" di CHANGELOG.md (diventa il testo della release su GitHub)
$changelog = Get-Content (Join-Path $radice "CHANGELOG.md") -Raw -Encoding UTF8
$sezione = [regex]::Match($changelog, "(?ms)^## " + [regex]::Escape($versione) + "\b.*?(?=^## |\z)")
if (-not $sezione.Success) { throw "In CHANGELOG.md manca la sezione '## $versione': scrivi le novita' di questa versione." }
$note = Join-Path $radice "publish\$nome-note.md"
New-Item -ItemType Directory -Force (Split-Path $note) | Out-Null
[IO.File]::WriteAllText($note, $sezione.Value.Trim() + "`r`n", (New-Object Text.UTF8Encoding($false)))

# 4. I test (in Release, come il programma che si pubblica)
if (-not $SaltaTest) {
    Write-Host "Test..." -ForegroundColor Cyan
    dotnet test -c Release
    if ($LASTEXITCODE -ne 0) { throw "I test non passano: non si pubblica." }
}

# 5. La cartella di uscita si rifa' da zero (solo se sta dentro publish\, per non cancellare altro per sbaglio)
$cartellaPublish = [IO.Path]::GetFullPath((Join-Path $radice "publish"))
if ((Test-Path $cartella) -and ([IO.Path]::GetFullPath($cartella).StartsWith($cartellaPublish + [IO.Path]::DirectorySeparatorChar))) {
    Remove-Item -LiteralPath $cartella -Recurse -Force
}

# 6. La pubblicazione: un solo .exe (con il runtime di .NET dentro, se non si e' scelto -SenzaRuntime)
Write-Host "Pubblicazione..." -ForegroundColor Cyan
$autonoma = if ($SenzaRuntime) { "false" } else { "true" }
dotnet publish $progetto -c Release -r win-x64 --self-contained $autonoma `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -p:DebugSymbols=false -o $cartella
if ($LASTEXITCODE -ne 0) { throw "La pubblicazione e' fallita." }

$exe = Join-Path $cartella "DocumentaleMarta.App.exe"
if (-not (Test-Path $exe)) { throw "Manca $exe dopo la pubblicazione." }

# 7. Lo ZIP e la sua impronta
Compress-Archive -Path (Join-Path $cartella "*") -DestinationPath $zip -Force
$impronta = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$impronta  $nome.zip" -Encoding ascii

Write-Host ""
Write-Host "Fatto." -ForegroundColor Green
Write-Host ("  Programma: {0}  ({1:N1} MB)" -f $exe, ((Get-Item $exe).Length / 1MB))
Write-Host ("  ZIP:       {0}  ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
Write-Host "  SHA-256:   $impronta"
Write-Host ""
Write-Host "Per pubblicare la release su GitHub (dopo aver salvato e inviato i commit):"
Write-Host "  git tag v$versione"
Write-Host "  git push origin main v$versione"
Write-Host "  gh release create v$versione `"$zip`" `"$zip.sha256`" --title `"Documentale $versione`" --notes-file `"$note`""
