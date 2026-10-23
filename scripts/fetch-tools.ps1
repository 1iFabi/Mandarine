# Downloads the bundled converters into src/Mandarine/tools/<name>/ at pinned versions,
# verifying each download's SHA-256 before using it. Re-run after changing a pin.
$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\src\Mandarine\tools'

$tools = @(
    @{ Name = 'wordcraft'; Url = 'https://github.com/storytold/wordcraft/releases/download/v0.9.0/wordcraft-0.9.0-windows-x64-portable.zip';
       Sha256 = '8948d8a9e14cf234c864359010ff5fccb2871225805b8d377e39d35fe1d5d127'; Exe = 'wordcraft-cli.exe'; Drop = @('wordcraft.exe') }
    @{ Name = 'gridcraft'; Url = 'https://github.com/storytold/gridcraft/releases/download/v0.10.0/gridcraft-0.10.0-windows-x64-portable.zip';
       Sha256 = 'b3af8987e1bee1970de5e681a6a6505a1943084a4375df8189569fef0d800315'; Exe = 'gridcraft-cli.exe'; Drop = @('gridcraft.exe') }
    @{ Name = 'deckcraft'; Url = 'https://github.com/storytold/deckcraft/releases/download/v0.9.0/deckcraft-0.9.0-windows-x64-portable.zip';
       Sha256 = 'c52ecf6a20f0936d8584c3bbebad4a8cee9e9545545f268ddd7a559427f5fad6'; Exe = 'deckcraft-cli.exe'; Drop = @('deckcraft.exe') }
    @{ Name = 'pdfcraft'; Url = 'https://github.com/storytold/pdfcraft/releases/download/v0.11.0/pdfcraft-0.11.0-windows-x64-portable.zip';
       Sha256 = '1655b326dc3d34e7fe1a5c211d06a603b12661765291ea995da2b7d9bcf2ff3c'; Exe = 'pdfcraft-cli.exe'; Drop = @('pdfcraft.exe') }
    # gyan.dev's "essentials" build (GPLv3): every encoder the catalog uses at half the size of the full build.
    # Keep lists the only files taken from the archive (ffplay, ffprobe and the docs are left out).
    @{ Name = 'ffmpeg'; Url = 'https://github.com/GyanD/codexffmpeg/releases/download/9.0.1/ffmpeg-9.0.1-essentials_build.zip';
       Sha256 = 'fec81ae03971d9dd4be3ebe02e263bd2ec1d789483f931bdba5f5715e65da2e9'; Exe = 'ffmpeg.exe'; Keep = @('bin/ffmpeg.exe', 'LICENSE', 'README.txt') }
)

# Single files (not archives), saved as tools/<Dir>/<File>.
$files = @(
    # BiRefNet-lite, MIT (https://github.com/ZhengPeng7/BiRefNet), as exported to ONNX by rembg.
    @{ Dir = 'models'; File = 'birefnet-lite.onnx'; Url = 'https://github.com/danielgatis/rembg/releases/download/v0.0.0/BiRefNet-general-bb_swin_v1_tiny-epoch_232.onnx';
       Sha256 = '5600024376f572a557870a5eb0afb1e5961636bef4e1e22132025467d0f03333' }
    # Tesseract LSTM models (tessdata_best, Apache-2.0) for scanned PDFs.
    @{ Dir = 'tessdata'; File = 'spa.traineddata'; Url = 'https://github.com/tesseract-ocr/tessdata_best/raw/main/spa.traineddata';
       Sha256 = 'e2c1ffdad8b30f26c45d4017a9183d3a7f9aa69e59918be4f88b126fac99ab2c' }
    @{ Dir = 'tessdata'; File = 'eng.traineddata'; Url = 'https://github.com/tesseract-ocr/tessdata_best/raw/main/eng.traineddata';
       Sha256 = '8280aed0782fe27257a68ea10fe7ef324ca0f8d85bd2fd145d1c2b560bcb66ba' }
)

foreach ($f in $files) {
    $dest = Join-Path (Join-Path $root $f.Dir) $f.File
    if (Test-Path $dest) { Write-Host "$($f.File): already present"; continue }
    $part = "$dest.part"
    New-Item -ItemType Directory -Force (Split-Path $dest) | Out-Null
    try {
        Write-Host "$($f.File): downloading"
        Invoke-WebRequest -Uri $f.Url -OutFile $part -UseBasicParsing
        $actual = (Get-FileHash $part -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $f.Sha256) { throw "$($f.File): SHA-256 mismatch (expected $($f.Sha256), got $actual)" }
        Move-Item $part $dest
        Write-Host "$($f.File): ok"
    }
    finally { Remove-Item $part -ErrorAction SilentlyContinue }
}

foreach ($t in $tools) {
    $dest = Join-Path $root $t.Name
    if (Test-Path (Join-Path $dest $t.Exe)) { Write-Host "$($t.Name): already present"; continue }

    $zip = Join-Path ([IO.Path]::GetTempPath()) "$($t.Name)-$([Guid]::NewGuid()).zip"
    $tmp = Join-Path ([IO.Path]::GetTempPath()) "$($t.Name)-$([Guid]::NewGuid())"
    try {
        Write-Host "$($t.Name): downloading"
        Invoke-WebRequest -Uri $t.Url -OutFile $zip -UseBasicParsing
        $actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne $t.Sha256) { throw "$($t.Name): SHA-256 mismatch (expected $($t.Sha256), got $actual)" }

        Expand-Archive $zip -DestinationPath $tmp
        # Releases wrap everything in one versioned folder; flatten it so the path stays stable across versions.
        $inner = Get-ChildItem $tmp -Directory | Select-Object -First 1
        New-Item -ItemType Directory -Force $dest | Out-Null
        if ($t.Keep) { foreach ($k in $t.Keep) { Move-Item (Join-Path $inner.FullName $k) -Destination $dest } }
        else { Get-ChildItem $inner.FullName | Move-Item -Destination $dest }
        # Mandarine drives the CLI only; the desktop app is dead weight in the bundle.
        foreach ($f in $t.Drop) { Remove-Item (Join-Path $dest $f) -ErrorAction SilentlyContinue }
        Write-Host "$($t.Name): ok"
    }
    finally {
        Remove-Item $zip -ErrorAction SilentlyContinue
        Remove-Item $tmp -Recurse -ErrorAction SilentlyContinue
    }
}
