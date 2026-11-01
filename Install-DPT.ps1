param([switch]$Uninstall)

$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\DPT'
$startMenuDir = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\Display Preset Tool'
$appPath = Join-Path $installDir 'DPT.exe'
$installerPath = Join-Path $installDir 'Install-DPT.ps1'

function Set-Shortcut([string]$Path, [string]$Target, [string]$Arguments = '') {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($Path)
    $shortcut.TargetPath = $Target
    $shortcut.Arguments = $Arguments
    $shortcut.WorkingDirectory = $installDir
    if ($Target -eq $appPath) { $shortcut.IconLocation = "$appPath,0" }
    $shortcut.Save()
}

if ($Uninstall) {
    Remove-Item -LiteralPath $startMenuDir -Recurse -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $installDir) {
        $resolvedInstall = (Resolve-Path -LiteralPath $installDir).Path
        if ($resolvedInstall -eq $installDir) { Remove-Item -LiteralPath $resolvedInstall -Recurse -Force }
    }
    Write-Host 'Display Preset Tool was removed. Its recovery data was kept.'
    exit 0
}

$sourceExe = Join-Path $PSScriptRoot 'DPT.exe'
if (-not (Test-Path -LiteralPath $sourceExe)) {
    throw 'DPT.exe must be in the same folder as Install-DPT.ps1.'
}

New-Item -ItemType Directory -Path $installDir -Force | Out-Null
New-Item -ItemType Directory -Path $startMenuDir -Force | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination $appPath -Force
Copy-Item -LiteralPath $PSCommandPath -Destination $installerPath -Force
Set-Shortcut (Join-Path $startMenuDir 'Display Preset Tool (DPT).lnk') $appPath
Set-Shortcut (Join-Path $startMenuDir 'Uninstall Display Preset Tool.lnk') 'powershell.exe' "-NoProfile -ExecutionPolicy Bypass -File `"$installerPath`" -Uninstall"

Write-Host 'Installed. Search Windows for “Display Preset Tool” or “DPT”.'
Write-Host 'The app asks for administrator approval when opened because it changes monitor device states.'
