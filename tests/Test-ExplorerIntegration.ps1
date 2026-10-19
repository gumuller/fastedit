param(
    [Parameter(Mandatory = $true)]
    [string]$InstallerPath
)

$ErrorActionPreference = "Stop"
if ($env:GITHUB_ACTIONS -ne "true" -or $env:RUNNER_ENVIRONMENT -ne "github-hosted") {
    throw "Installer integration checks require a fresh, isolated GitHub-hosted Windows VM."
}

$InstallerPath = (Resolve-Path -LiteralPath $InstallerPath).Path
$verb = "Software\Classes\*\shell\FastEdit"
$userKey = "Registry::HKEY_CURRENT_USER\$verb"
$machineKey = "Registry::HKEY_LOCAL_MACHINE\$verb"
if ((Test-Path -LiteralPath $userKey) -or (Test-Path -LiteralPath $machineKey)) {
    throw "Refusing to replace an existing FastEdit Explorer registration."
}

foreach ($ownership in @("installed", "disabled", "another-copy", "stale-marker")) {
    $installDirectory = Join-Path $env:RUNNER_TEMP "FastEdit shell smoke $([guid]::NewGuid().ToString('N'))"
    $installedExe = Join-Path $installDirectory "FastEdit.exe"
    $owner = if ($ownership -ne "another-copy") {
        $installedExe
    } else {
        Join-Path $env:RUNNER_TEMP "another portable copy\FastEdit.exe"
    }
    $commandTarget = if ($ownership -eq "installed") {
        $installedExe
    } elseif ($ownership -eq "disabled") {
        $null
    } else {
        Join-Path $env:RUNNER_TEMP "another portable copy\FastEdit.exe"
    }
    try {
        $setup = Start-Process -FilePath $InstallerPath -ArgumentList "/S /D=$installDirectory" -Wait -PassThru
        if ($setup.ExitCode -ne 0 -or !(Test-Path -LiteralPath $installedExe)) {
            throw "The test installation failed."
        }
        $command = [Microsoft.Win32.Registry]::GetValue(
            "HKEY_LOCAL_MACHINE\$verb\command", "", $null)
        if ($command -ne "`"$installedExe`" `"%1`"") {
            throw "The installer registered an incorrect Explorer command: $command"
        }

        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($verb)
        try {
            $key.SetValue("FastEditExecutable", $owner)
            if ($ownership -eq "disabled") {
                $key.SetValue("LegacyDisable", "")
            } else {
                $commandKey = $key.CreateSubKey("command")
                try {
                    $commandKey.SetValue("", "`"$commandTarget`" `"%1`"")
                } finally {
                    $commandKey.Dispose()
                }
            }
        } finally {
            $key.Dispose()
        }
        if ($ownership -eq "disabled" -and
            [Microsoft.Win32.Registry]::GetValue(
                "HKEY_CLASSES_ROOT\*\shell\FastEdit", "LegacyDisable", $null) -ne "") {
            throw "The per-user opt-out did not hide the installed Explorer entry."
        }

        $uninstaller = Join-Path $installDirectory "Uninstall.exe"
        $uninstall = Start-Process -FilePath $uninstaller `
            -ArgumentList "/S _?=$installDirectory" -Wait -PassThru
        if ($uninstall.ExitCode -ne 0) { throw "The test uninstallation failed." }
        if (Test-Path -LiteralPath $machineKey) {
            throw "The uninstaller left its machine-wide Explorer registration behind."
        }
        if ($ownership -in @("installed", "disabled")) {
            if (Test-Path -LiteralPath $userKey) {
                throw "The uninstaller left its own per-user Explorer registration behind."
            }
        } else {
            $remaining = [Microsoft.Win32.Registry]::GetValue(
                "HKEY_CURRENT_USER\$verb\command", "", $null)
            if ($remaining -ne "`"$commandTarget`" `"%1`"") {
                throw "The uninstaller changed another copy's per-user Explorer registration."
            }
        }
        Write-Host "Explorer registration and uninstall ownership passed: $ownership"
    } finally {
        if (Test-Path -LiteralPath $userKey) {
            $registeredOwner = [Microsoft.Win32.Registry]::GetValue(
                "HKEY_CURRENT_USER\$verb", "FastEditExecutable", $null)
            if ($registeredOwner -ne $owner) {
                throw "Refusing to clean a per-user entry not owned by this installer test."
            }
            [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($verb)
        }
        if (Test-Path -LiteralPath $installDirectory) {
            Remove-Item -LiteralPath $installDirectory -Recurse -Force
        }
    }
}
