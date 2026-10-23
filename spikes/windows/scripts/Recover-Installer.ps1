#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][int]$InstallerPid,
    [Parameter(Mandatory=$true)][int]$LauncherPid,
    [Parameter(Mandatory=$true)][string]$TargetSid
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$state = Get-Content -LiteralPath (Join-Path $root '.local\install-status.json') -Raw | ConvertFrom-Json
if ($state.State -ne 'AwaitingPassword') { throw 'Solo se recupera el dialogo pendiente de contrasena.' }
$setup = Join-Path $PSScriptRoot 'Install-Agent.ps1'
$launcherPath = Join-Path $PSScriptRoot 'Launch-AgentSetup.ps1'
$installer = Get-CimInstance Win32_Process -Filter ("ProcessId = " + $InstallerPid)
$launcher = Get-CimInstance Win32_Process -Filter ("ProcessId = " + $LauncherPid)
if (-not $installer -or -not $launcher -or $installer.Name -ne 'powershell.exe' -or $launcher.Name -ne 'powershell.exe' -or $installer.ParentProcessId -ne $LauncherPid) {
    throw 'No coincide el par de procesos de la instalacion original.'
}
if (-not $installer.CommandLine -or $installer.CommandLine.IndexOf($setup,[StringComparison]::OrdinalIgnoreCase) -lt 0 -or -not $launcher.CommandLine -or $launcher.CommandLine.IndexOf($launcherPath,[StringComparison]::OrdinalIgnoreCase) -lt 0) {
    throw 'Los procesos no corresponden a los scripts de este repositorio.'
}
foreach ($path in @((Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'ControlParentalLabAgent'),(Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'ControlParentalLabAgent'))) {
    if (Test-Path -LiteralPath $path) { throw 'Hay archivos de instalacion; revisar manualmente antes de recuperar.' }
}
if (Get-Service -Name 'ControlParentalLabAgent' -ErrorAction SilentlyContinue) { throw 'El servicio ya existe; no se cerrara el instalador.' }
# Detener solo los dos procesos propios verificados, antes de que exista instalacion.
Stop-Process -Id $LauncherPid -Force -ErrorAction Stop
Stop-Process -Id $InstallerPid -Force -ErrorAction Stop
& $setup -TargetSid $TargetSid
