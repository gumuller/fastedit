# FastEdit Build Script
# Builds self-contained packages for x64 and arm64, creates portable zips and NSIS installers
param(
    [string]$Version,
    [switch]$IncrementBuildCounter,
    [switch]$PackageOnly,
    [switch]$SkipInstaller,
    [switch]$Sign,
    [string]$CertificateThumbprint
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path $PSScriptRoot -Parent
$ProjectPath = Join-Path $RepoRoot "src\FastEdit\FastEdit.csproj"
$OutputDir = Join-Path $PSScriptRoot "output"
$Runtimes = @("win-x64", "win-arm64")
$SignScript = Join-Path $PSScriptRoot "sign.ps1"
$PublicCertPath = Join-Path $OutputDir "FastEdit-SelfSigned.cer"

if ($IncrementBuildCounter -and -not [string]::IsNullOrWhiteSpace($Version)) {
    throw "Specify either -Version or -IncrementBuildCounter, not both."
}
if (-not $IncrementBuildCounter -and [string]::IsNullOrWhiteSpace($Version)) {
    throw "Specify -Version <version> or -IncrementBuildCounter."
}
if ($PackageOnly -and $IncrementBuildCounter) {
    throw "Package-only builds must use the version of the already tested binaries."
}
if ($CertificateThumbprint -and -not $Sign) {
    throw "Specify -Sign when selecting a signing certificate."
}

# Package-only builds preserve the published binaries downloaded from CI.
if (-not $PackageOnly -and (Test-Path $OutputDir)) {
    Remove-Item -LiteralPath $OutputDir -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

if ($IncrementBuildCounter) {
    $versionFile = Join-Path $OutputDir "release-version.txt"
    dotnet msbuild $ProjectPath `
        -t:IncrementReleaseVersion `
        "-p:BuildVersionOutputFile=$versionFile" `
        -nologo `
        -verbosity:minimal

    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $versionFile)) {
        throw "Failed to increment the release version."
    }

    $Version = (Get-Content $versionFile -Raw).Trim()
    Remove-Item $versionFile
}

Write-Host "=== FastEdit Build v$Version ===" -ForegroundColor Cyan

$certificateArgs = if ($CertificateThumbprint) {
    @("-CertificateThumbprint", $CertificateThumbprint)
} else {
    @()
}

if ($PackageOnly) {
    foreach ($rid in $Runtimes) {
        $arch = $rid.Replace("win-", "")
        $exe = Join-Path $OutputDir "publish-$arch\FastEdit.exe"
        if (-not (Test-Path -LiteralPath $exe)) {
            throw "Missing tested binary: $exe"
        }
        $binaryVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
        if (([version]$binaryVersion).ToString(3) -ne ([version]$Version).ToString(3)) {
            throw "The $arch binary version '$binaryVersion' does not match '$Version'."
        }
    }
}

foreach ($rid in $Runtimes) {
    $arch = $rid.Replace("win-", "")
    $publishDir = Join-Path $OutputDir "publish-$arch"
    $zipName = "FastEdit-$Version-$arch-portable.zip"
    $zipPath = Join-Path $OutputDir $zipName

    Write-Host ""
    Write-Host "--- Building $arch ---" -ForegroundColor Yellow

    if (-not $PackageOnly) {
        # Publish both architectures with the one version selected above.
        dotnet publish $ProjectPath `
            -c Release `
            -r $rid `
            --self-contained true `
            -p:PublishSingleFile=false `
            -p:PublishTrimmed=false `
            -p:IncludeNativeLibrariesForSelfExtract=true `
            -p:ReleaseVersion=$Version `
            -o $publishDir

        if ($LASTEXITCODE -ne 0) {
            throw "Build failed for $arch."
        }
    }

    # Sign the app binary before zipping so the portable zip contains a signed exe
    if ($Sign) {
        Write-Host "Signing $arch binaries..." -ForegroundColor Magenta
        $exeToSign = Join-Path $publishDir "FastEdit.exe"
        $exportArg = if ($rid -eq $Runtimes[0]) { @("-ExportCertTo", $PublicCertPath) } else { @() }
        & pwsh -NoProfile -File $SignScript -Files $exeToSign @exportArg @certificateArgs
        if ($LASTEXITCODE -ne 0) { Write-Host "ERROR: signing failed for $arch" -ForegroundColor Red; exit 1 }
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

            & $nsisExe /DVERSION=$Version /DARCH=$arch "/DPUBLISH_DIR=$publishDir" "/DOUTPUT_DIR=$OutputDir" $nsiScript

            if ($LASTEXITCODE -ne 0) {
                throw "NSIS failed for $arch."
            }
            elseif ($Sign) {
                $setupExe = Join-Path $OutputDir "FastEdit-$Version-$arch-setup.exe"
                Write-Host "Signing installer: $setupExe" -ForegroundColor Magenta
                & pwsh -NoProfile -File $SignScript -Files $setupExe @certificateArgs
                if ($LASTEXITCODE -ne 0) { throw "Installer signing failed for $arch." }
            }
        }
    }
    else {
        throw "NSIS not found. Install NSIS or explicitly use -SkipInstaller."
    }
}

Write-Host ""
Write-Host "=== Build Complete ===" -ForegroundColor Cyan
Write-Host "Output: $OutputDir" -ForegroundColor Green
Get-ChildItem $OutputDir -File | ForEach-Object {
    $sizeMB = [math]::Round($_.Length / 1MB, 1)
    Write-Host "  $($_.Name) ($sizeMB MB)" -ForegroundColor Gray
}
