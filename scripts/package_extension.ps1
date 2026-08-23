# Packages SuperScroll into an installable .pext.
#
# version.txt is the single source of truth: this script stamps extension.yaml and AssemblyInfo
# from it, so a release can never ship a manifest and a binary that disagree about the version.

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$version = (Get-Content (Join-Path $root 'version.txt') -Raw).Trim()
if (-not $version) { throw 'version.txt is empty' }

Write-Host "Packaging SuperScroll $version" -ForegroundColor Cyan

# --- stamp version into extension.yaml -------------------------------------------------
$manifestPath = Join-Path $root 'extension.yaml'
$manifest = Get-Content $manifestPath -Raw
$manifest = [regex]::Replace($manifest, '(?m)^Version:.*$', "Version: $version")
Set-Content $manifestPath $manifest -Encoding utf8 -NoNewline

# --- stamp version into AssemblyInfo ---------------------------------------------------
$asmPath = Join-Path $root 'src\AssemblyInfo.cs'
$asm = Get-Content $asmPath -Raw
$asm = [regex]::Replace($asm, 'AssemblyVersion\("[^"]*"\)', "AssemblyVersion(""$version.0"")")
$asm = [regex]::Replace($asm, 'AssemblyFileVersion\("[^"]*"\)', "AssemblyFileVersion(""$version.0"")")
Set-Content $asmPath $asm -Encoding utf8 -NoNewline

# --- build -----------------------------------------------------------------------------
Write-Host '  building...' -ForegroundColor Gray
& dotnet build (Join-Path $root 'src\SuperScroll.csproj') -c Release --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { throw "build failed with exit code $LASTEXITCODE" }

$buildDir = Join-Path $root 'src\bin\Release\net4.6.2'
$dll = Join-Path $buildDir 'SuperScroll.dll'
if (-not (Test-Path $dll)) { throw "build output missing: $dll" }

# --- stage ------------------------------------------------------------------------------
$staging = Join-Path $root 'package'
if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory $staging -Force | Out-Null

Copy-Item $dll $staging
Copy-Item $manifestPath $staging

$icon = Join-Path $root 'icon.png'
if (Test-Path $icon) { Copy-Item $icon $staging } else { Write-Host '  (no icon.png — Playnite will use a default)' -ForegroundColor DarkYellow }

# Newtonsoft ships with Playnite itself, so bundling it risks an assembly-version clash with the
# host. Only DLLs Playnite does NOT provide belong in the package.
foreach ($extra in @()) {
    $p = Join-Path $buildDir $extra
    if (Test-Path $p) { Copy-Item $p $staging }
}

# --- zip --------------------------------------------------------------------------------
$outDir = Join-Path $root 'pext'
New-Item -ItemType Directory $outDir -Force | Out-Null

$underscored = $version.Replace('.', '_')
$pext = Join-Path $outDir "SuperScroll-$underscored.pext"
if (Test-Path $pext) { Remove-Item $pext -Force }

# A .pext IS a zip, but Compress-Archive refuses any destination not named .zip — so write the zip
# and rename it.
$tempZip = Join-Path $outDir "SuperScroll-$underscored.zip"
if (Test-Path $tempZip) { Remove-Item $tempZip -Force }

# Antivirus race: a freshly written DLL can still be locked by a realtime scan when Compress-Archive
# opens it. Retry only on that — a genuine error must surface immediately rather than be retried
# three times and reported as a lock, which is exactly what the first version of this script did.
$attempt = 0
while ($true) {
    try {
        Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $tempZip -Force
        break
    } catch [System.IO.IOException] {
        $attempt++
        if ($attempt -ge 3) { throw }
        Write-Host "  archive locked, retrying ($attempt/3)..." -ForegroundColor DarkYellow
        Start-Sleep -Milliseconds 700
    }
}

Move-Item $tempZip $pext -Force

$size = [math]::Round((Get-Item $pext).Length / 1KB, 1)
$hash = (Get-FileHash $pext -Algorithm SHA256).Hash.ToLower()

Write-Host ''
Write-Host '  PACKAGE CREATED SUCCESSFULLY!' -ForegroundColor Green
Write-Host "  File:   $pext"
Write-Host "  Size:   $size KB"
Write-Host "  SHA256: $hash"
