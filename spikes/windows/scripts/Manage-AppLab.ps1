#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('Audit','Enforce','Restore','Status')][string]$Action)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Usar Windows PowerShell 5.1 como administrador.' }
Import-Module AppLocker -ErrorAction Stop
$root = Split-Path $PSScriptRoot -Parent
$local = Join-Path $root '.local'
$stateFile = Join-Path $local 'app-state.json'
$backupFile = Join-Path $local 'app-before.xml'
$filter = Join-Path $root 'WebFilter\bin\Release\net10.0\WebFilter.dll'
$passwordFile = Join-Path $local 'parent-password.json'

function Get-PolicyDigest([string]$text) {
    [xml]$xml = $text
    $sha = [Security.Cryptography.SHA256]::Create()
    try { [Convert]::ToBase64String($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($xml.OuterXml))) }
    finally { $sha.Dispose() }
}
function Read-LocalXml { Get-AppLockerPolicy -Local -Xml -ErrorAction Stop }
function Assert-Unchanged($state) {
    if ((Get-PolicyDigest (Read-LocalXml)) -ne $state.AppliedDigest) {
        throw 'La politica local cambio fuera del laboratorio. No se sobrescribira. Revisar manualmente el backup.'
    }
}
function Save-State($state) {
    $state | ConvertTo-Json | Set-Content -LiteralPath $stateFile -Encoding UTF8
}

if ($Action -eq 'Status') {
    if (Test-Path -LiteralPath $stateFile) {
        $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        [pscustomobject]@{ Mode = $state.Mode; Unchanged = ((Get-PolicyDigest (Read-LocalXml)) -eq $state.AppliedDigest); AppIdentity = (Get-Service AppIDSvc).Status } | Format-List
    } else { Write-Output 'Sin laboratorio AppLocker activo.' }
    return
}
if (-not (Test-Path -LiteralPath $filter) -or -not (Test-Path -LiteralPath $passwordFile)) {
    throw 'Compilar y configurar primero la contrasena del responsable segun README.'
}
& dotnet $filter --authorize $passwordFile
if ($LASTEXITCODE -ne 0) { throw 'No se autorizo el cambio.' }

if ($Action -eq 'Audit') {
    if (Test-Path -LiteralPath $stateFile) { throw 'Ya existe un experimento. Restaurar primero.' }
    $audit = Join-Path $local 'app-audit.xml'
    if (-not (Test-Path -LiteralPath $audit)) { throw 'Ejecutar Prepare-AppPolicy.ps1 primero.' }
    $before = Read-LocalXml
    [xml]$localPolicy = $before
    [xml]$effective = Get-AppLockerPolicy -Effective -Xml -ErrorAction Stop
    foreach ($p in @($localPolicy,$effective)) {
        if ($p.SelectNodes('/AppLockerPolicy/RuleCollection/*').Count -gt 0) { throw 'Hay reglas existentes. No se reemplazaran; usar una VM de laboratorio.' }
        foreach ($c in $p.SelectNodes('/AppLockerPolicy/RuleCollection')) {
            if ($c.EnforcementMode -and $c.EnforcementMode -ne 'NotConfigured') { throw 'Hay colecciones configuradas; no se modificaran.' }
        }
    }
    $before | Set-Content -LiteralPath $backupFile -Encoding UTF8
    # Conservar informacion ANTES de mutar para permitir recuperacion ante un fallo parcial.
    $state = [pscustomobject]@{ Mode='Preparing'; AppliedDigest=(Get-PolicyDigest $before); AuditStarted=(Get-Date).ToUniversalTime().ToString('o'); OriginalServiceStatus=(Get-Service AppIDSvc).Status.ToString() }
    Save-State $state
    try {
        Set-AppLockerPolicy -XmlPolicy $audit -ErrorAction Stop
        $state.AppliedDigest = Get-PolicyDigest (Read-LocalXml)
        $state.Mode = 'Audit'
        Save-State $state
        Start-Service AppIDSvc -ErrorAction Stop
    } catch {
        # El backup sigue disponible; nunca ocultar un fallo de restauracion.
        Set-AppLockerPolicy -XmlPolicy $backupFile -ErrorAction Stop
        Remove-Item -LiteralPath $stateFile
        throw
    }
    Write-Output 'Auditoria activa. Ejecutar SampleApp desde la sesion objetivo; todavia debe abrirse.'
    return
}

if (-not (Test-Path -LiteralPath $stateFile)) { throw 'No hay un experimento registrado.' }
$state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
Assert-Unchanged $state
if ($Action -eq 'Enforce') {
    if ($state.Mode -ne 'Audit') { throw 'Primero ejecutar Audit.' }
    $events = @(Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-AppLocker/EXE and DLL'; Id=8003; StartTime=([datetime]$state.AuditStarted).ToLocalTime()} -ErrorAction SilentlyContinue)
    $observed = @($events | Where-Object { $_.ToXml() -match 'fac3b090-609c-4ff6-922d-c67491174e5a' })
    if (-not $observed.Count) { throw 'Falta evento de auditoria 8003 de la regla de prueba. No activar bloqueo.' }
    $enforce = Join-Path $local 'app-enforce.xml'
    # No aceptar politicas regeneradas o editadas despues de la auditoria.
    [xml]$expected = Read-LocalXml
    foreach ($c in $expected.AppLockerPolicy.RuleCollection) { $c.EnforcementMode = 'Enabled' }
    if ((Get-PolicyDigest $expected.OuterXml) -ne (Get-PolicyDigest (Get-Content -LiteralPath $enforce -Raw))) {
        throw 'La politica de bloqueo no corresponde a la auditada.'
    }
    Set-AppLockerPolicy -XmlPolicy $enforce -ErrorAction Stop
    $state.AppliedDigest = Get-PolicyDigest (Read-LocalXml)
    $state.Mode = 'Enforced'
    Save-State $state
    Write-Output 'Modo hijo de laboratorio: bloqueo limitado al hash de SampleApp y al usuario seleccionado.'
    Write-Output 'Para desbloquear: ejecutar Restore e introducir la contrasena del responsable.'
} elseif ($Action -eq 'Restore') {
    Set-AppLockerPolicy -XmlPolicy $backupFile -ErrorAction Stop
    if ((Get-PolicyDigest (Read-LocalXml)) -ne (Get-PolicyDigest (Get-Content -LiteralPath $backupFile -Raw))) {
        throw 'La verificacion de restauracion no coincide; conservar backup y revisar.'
    }
    Remove-Item -LiteralPath $stateFile
    Write-Output 'Politica original restaurada. Verificar que SampleApp vuelve a abrirse.'
    Write-Output 'No se cambia el inicio del servicio AppIDSvc; puede permanecer iniciado hasta el reinicio.'
}
