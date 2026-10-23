# Builds the downloadable release: dist/Mandarine-v<Version>-win-x64.zip, a folder with Mandarine.exe,
# .NET included (nothing to install), the bundled tools and the licenses. Upload the zip to a GitHub release.
#
#   .\scripts\package.ps1 -Version 1.0.0
#   .\scripts\package.ps1 -Version 1.0.0 -SkipTests
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$name = "Mandarine-v$Version-win-x64"
$dist = Join-Path $repo 'dist'
$out = Join-Path $dist $name
$zip = "$out.zip"

function Invoke-Checked([string]$what, [scriptblock]$command) {
    Write-Host "==> $what"
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what failed (exit code $LASTEXITCODE)" }
}

# 1. Every bundled tool, at its pinned version and verified checksum.
Write-Host '==> Fetching tools'
& (Join-Path $PSScriptRoot 'fetch-tools.ps1')

# 2. Nothing broken gets packaged.
if (-not $SkipTests) {
    Invoke-Checked 'Running tests' { dotnet test (Join-Path $repo 'tests\Mandarine.Tests') -c Release }
}

# 3. Self-contained build: the .NET runtime travels with the app.
Remove-Item $out -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Invoke-Checked 'Publishing' {
    dotnet publish (Join-Path $repo 'src\Mandarine\Mandarine.csproj') -c Release -r win-x64 --self-contained true `
        -p:Version=$Version -p:DebugType=None -o $out
}

# The tools must have made it into the build; a release without them converts nothing.
$required = @('Mandarine.exe', 'tools\ffmpeg\ffmpeg.exe', 'tools\wordcraft\wordcraft-cli.exe', 'tools\gridcraft\gridcraft-cli.exe',
    'tools\deckcraft\deckcraft-cli.exe', 'tools\pdfcraft\pdfcraft-cli.exe', 'tools\models\birefnet-lite.onnx',
    'tools\tessdata\spa.traineddata', 'tools\tessdata\eng.traineddata')
$missing = $required | Where-Object { -not (Test-Path (Join-Path $out $_)) }
if ($missing) { throw "Missing from the build: $($missing -join ', ')" }

Copy-Item (Join-Path $repo 'README.md'), (Join-Path $repo 'README.es.md'), (Join-Path $repo 'LICENSE'), (Join-Path $repo 'THIRD-PARTY-NOTICES.md') $out

# 4. Zip with the folder as its root, so unzipping gives one tidy "Mandarine-v..." folder.
# Windows' bundled tar writes zips (-a picks the format from the extension) and, unlike Compress-Archive, has no 2 GB limit.
# Full path: a Git or MSYS tar earlier on PATH can't write zips or read C:\ paths.
Invoke-Checked 'Zipping' { & (Join-Path $env:SystemRoot 'System32\tar.exe') -a -c -f $zip -C $dist $name }

$size = '{0:N0} MB' -f ((Get-Item $zip).Length / 1MB)
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ''
Write-Host "Release ready: $zip"
Write-Host "Size:          $size"
Write-Host "SHA-256:       $hash"
