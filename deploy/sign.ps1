# FastEdit Signing Script
# Self-signs binaries with a code-signing certificate stored in the CurrentUser personal store.
# Creates the certificate on first run (10-year validity). On subsequent runs, reuses it.
#
# Usage:
#   .\sign.ps1 -Files file1.exe,file2.exe
#   .\sign.ps1 -Files file1.exe -ExportCertTo .\FastEdit-PublicKey.cer
#
param(
    [Parameter(Mandatory=$true)]
    [string[]]$Files,

    [string]$CertSubject = "CN=FastEdit Self-Signed, O=FastEdit, C=US",

    [string]$FriendlyName = "FastEdit Code Signing",

    [string]$ExportCertTo,

    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

# ---- Locate signtool.exe (Windows SDK) ----
$signtool = Get-ChildItem "C:\Program Files (x86)\Windows Kits\10\bin" -Filter signtool.exe -Recurse -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1 -ExpandProperty FullName

if (-not $signtool) {
    Write-Host "ERROR: signtool.exe not found. Install the Windows 10/11 SDK." -ForegroundColor Red
    exit 1
}
Write-Host "Using signtool: $signtool" -ForegroundColor Gray

# ---- Find or create the code-signing certificate ----
$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $CertSubject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date) } |
    Sort-Object NotAfter -Descending |
    Select-Object -First 1

if (-not $cert) {
    Write-Host "Creating new self-signed code-signing certificate..." -ForegroundColor Yellow
    $cert = New-SelfSignedCertificate `
        -Type CodeSigningCert `
        -Subject $CertSubject `
        -FriendlyName $FriendlyName `
        -KeyAlgorithm RSA `
        -KeyLength 3072 `
        -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") `
        -CertStoreLocation Cert:\CurrentUser\My `
        -NotAfter (Get-Date).AddYears(10)

    Write-Host "Created certificate:" -ForegroundColor Green
    Write-Host "  Subject:    $($cert.Subject)" -ForegroundColor Gray
    Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray
    Write-Host "  Valid to:   $($cert.NotAfter)" -ForegroundColor Gray
}
else {
    Write-Host "Using existing certificate:" -ForegroundColor Green
    Write-Host "  Subject:    $($cert.Subject)" -ForegroundColor Gray
    Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor Gray
    Write-Host "  Valid to:   $($cert.NotAfter)" -ForegroundColor Gray
}

# ---- Export public cert if requested ----
if ($ExportCertTo) {
    $exportDir = Split-Path $ExportCertTo -Parent
    if ($exportDir -and -not (Test-Path $exportDir)) {
        New-Item -ItemType Directory -Path $exportDir -Force | Out-Null
    }
    Export-Certificate -Cert $cert -FilePath $ExportCertTo -Type CERT -Force | Out-Null
    Write-Host "Exported public certificate to: $ExportCertTo" -ForegroundColor Green
}

# ---- Sign files ----
$failed = 0
foreach ($file in $Files) {
    if (-not (Test-Path $file)) {
        Write-Host "  SKIP (not found): $file" -ForegroundColor Yellow
        continue
    }

    Write-Host ""
    Write-Host "Signing: $file" -ForegroundColor Cyan

    & $signtool sign `
        /sha1 $cert.Thumbprint `
        /fd SHA256 `
        /tr $TimestampUrl `
        /td SHA256 `
        /d "FastEdit" `
        /du "https://github.com/gumuller/fastedit" `
        $file

    if ($LASTEXITCODE -ne 0) {
        Write-Host "  FAILED" -ForegroundColor Red
        $failed++
    }
}

Write-Host ""
if ($failed -gt 0) {
    Write-Host "Signing completed with $failed failure(s)." -ForegroundColor Red
    exit 1
}
Write-Host "All files signed successfully." -ForegroundColor Green
