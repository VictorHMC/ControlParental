#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$private = Join-Path $root '.local'
New-Item -ItemType Directory -Path $private -Force | Out-Null
# Conservar el usuario de la sesion original aunque UAC utilice otra cuenta.
$targetSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$shell = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
$env:DOTNET_CLI_HOME = $private
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
try {
    Write-Host 'Preparando el servicio de laboratorio...'
    & dotnet publish (Join-Path $root 'Agent\Agent.csproj') -c Release --no-self-contained --configfile (Join-Path $root 'NuGet.Config') -o (Join-Path $private 'publish-agent') *> (Join-Path $private 'build-agent.log')
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar. Revisar .local\build-agent.log.' }
    $setup = Join-Path $PSScriptRoot 'Install-Agent.ps1'
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $setup + '" -TargetSid ' + $targetSid
    $statusFile = Join-Path $private 'install-status.json'
    if (Test-Path -LiteralPath $statusFile) { Remove-Item -LiteralPath $statusFile -ErrorAction Stop }
    $process = Start-Process -FilePath $shell -ArgumentList $arguments -Verb RunAs -WindowStyle Normal -PassThru -ErrorAction Stop
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw 'El instalador no termino correctamente. Revisar .local\install-status.json si existe.' }
    if (-not (Test-Path -LiteralPath $statusFile)) { throw 'La instalacion no ha comunicado un resultado. No se da por instalada.' }
    $status = Get-Content -LiteralPath $statusFile -Raw | ConvertFrom-Json
    Write-Host $status.Message
    if ($status.State -ne 'Installed') { exit 1 }
    $control = Join-Path $PSScriptRoot 'Show-AgentControl.ps1'
    Start-Process -FilePath $shell -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "' + $control + '"') -WindowStyle Normal
} catch {
    Write-Host ('No se completo la instalacion: ' + $_.Exception.Message)
    exit 1
}
