# Builds a release the same way on any PC or on GitHub:
# runs the tests, builds SwatchSheet.exe, zips it with the README, writes the zip's SHA-256
# fingerprint, and pulls this version's notes out of CHANGES.md.
#
#   powershell -File tools\make-release.ps1 -OutDir some\folder
#
# Leaves in OutDir: SwatchSheet-<ver>.zip, SwatchSheet-<ver>.zip.sha256, notes.md
param(
    [Parameter(Mandatory = $true)][string]$OutDir
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "VERSION should look like 1.2.3, but it says '$version'." }
Write-Host "=== Swatch Sheet $version ==="

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path
$app = Join-Path $OutDir "Swatch Sheet $version"
if (Test-Path $app) { Remove-Item $app -Recurse -Force }   # this folder only ever holds fresh build output

Write-Host 'Running tests...'
dotnet run -c Release --project tests/SwatchSheet.Tests
if ($LASTEXITCODE -ne 0) { throw 'Tests failed, so no release was made.' }

Write-Host 'Building SwatchSheet.exe...'
dotnet publish src/SwatchSheet/SwatchSheet.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none -o $app
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Copy-Item (Join-Path $root 'README.txt') (Join-Path $app 'README.txt')

$zipName = "SwatchSheet-$version.zip"
$zip = Join-Path $OutDir $zipName
Compress-Archive -Path (Join-Path $app 'SwatchSheet.exe'), (Join-Path $app 'README.txt') -DestinationPath $zip -Force
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash
[IO.File]::WriteAllText("$zip.sha256", "$hash  $zipName`n")
Write-Host "Made $zipName ($hash)"

# Release notes: the "## <version>" section of CHANGES.md, up to the next "## " heading.
$notes = New-Object System.Collections.Generic.List[string]
$inside = $false
$changes = Join-Path $root 'CHANGES.md'
if (Test-Path $changes) {
    foreach ($line in Get-Content $changes) {
        if ($line -match '^##\s+v?(\S+)') {
            if ($inside) { break }
            $inside = ($Matches[1] -eq $version)
            continue
        }
        if ($inside) { $notes.Add($line) }
    }
}
$text = ($notes -join "`n").Trim()
if ($text.Length -eq 0) { $text = "Swatch Sheet $version" }
$text += "`n`nDownload **$zipName**, unzip it, and double-click SwatchSheet.exe. If Windows shows a blue warning, click More info, then Run anyway."
[IO.File]::WriteAllText((Join-Path $OutDir 'notes.md'), $text + "`n")
Write-Host 'Release notes:'
Write-Host $text
