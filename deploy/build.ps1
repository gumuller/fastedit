# FastEdit Build Script
# Builds self-contained packages for x64 and arm64, creates portable zips and NSIS installers
param(
    [string]$Version = "1.0.0",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent
$ProjectPath = Join-Path $RepoRoot "src\FastEdit\FastEdit.csproj"
$OutputDir = Join-Path $PSScriptRoot "output"
$Runtimes = @("win-x64", "win-arm64")

# Clean output
if (Test-Path $OutputDir) { Remove-Item $OutputDir -Recurse -Force }
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Write-Host "=== FastEdit Build v$Version ===" -ForegroundColor Cyan

foreach ($rid in $Runtimes) {
    $arch = $rid.Replace("win-", "")
    $publishDir = Join-Path $OutputDir "publish-$arch"
    $zipName = "FastEdit-$Version-$arch-portable.zip"
    $zipPath = Join-Path $OutputDir $zipName

    Write-Host ""
    Write-Host "--- Building $arch ---" -ForegroundColor Yellow

    # Publish self-contained, single-dir, trimmed
    dotnet publish $ProjectPath `
        -c Release `
        -r $rid `
        --self-contained true `
        -p:PublishSingleFile=false `
        -p:PublishTrimmed=false `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:Version=$Version `
        -p:FileVersion=$Version `
        -p:AssemblyVersion=$Version `
        -o $publishDir

    if ($LASTEXITCODE -ne 0) {
        Write-Host "ERROR: Build failed for $arch" -ForegroundColor Red
        exit 1
    }

    # Create portable zip
    Write-Host "Creating portable zip: $zipName" -ForegroundColor Green
    Compress-Archive -Path "$publishDir\*" -DestinationPath $zipPath -Force

    Write-Host "  -> $zipPath" -ForegroundColor Gray
}

# Build NSIS installers
if (-not $SkipInstaller) {
    $nsisExe = "C:\Program Files (x86)\NSIS\makensis.exe"
    if (-not (Test-Path $nsisExe)) {
        $nsisExe = "C:\Program Files\NSIS\makensis.exe"
    }

    if (Test-Path $nsisExe) {
        foreach ($rid in $Runtimes) {
            $arch = $rid.Replace("win-", "")
            $publishDir = Join-Path $OutputDir "publish-$arch"
            $nsiScript = Join-Path $PSScriptRoot "installer.nsi"

            Write-Host ""
            Write-Host "--- Creating $arch installer ---" -ForegroundColor Yellow

            & $nsisExe /DVERSION=$Version /DARCH=$arch /DPUBLISH_DIR=$publishDir /DOUTPUT_DIR=$OutputDir $nsiScript

            if ($LASTEXITCODE -ne 0) {
                Write-Host "WARNING: NSIS failed for $arch" -ForegroundColor Red
            }
        }
    }
    else {
        Write-Host ""
        Write-Host "NSIS not found. Skipping installer creation." -ForegroundColor Yellow
        Write-Host "Install NSIS from https://nsis.sourceforge.io/Download" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Cyan
Write-Host "Output: $OutputDir" -ForegroundColor Green
Get-ChildItem $OutputDir -File | ForEach-Object {
    $sizeMB = [math]::Round($_.Length / 1MB, 1)
    Write-Host "  $($_.Name) ($sizeMB MB)" -ForegroundColor Gray
}
