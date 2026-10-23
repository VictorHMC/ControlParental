#Requires -Version 5.1
[CmdletBinding()]
param([string]$TargetUser = [Security.Principal.WindowsIdentity]::GetCurrent().Name)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSEdition -ne 'Desktop') { throw 'Ejecutar con Windows PowerShell 5.1 (powershell.exe).' }
Import-Module AppLocker -ErrorAction Stop
$root = Split-Path $PSScriptRoot -Parent
$sample = Join-Path $root 'SampleApp\bin\Release\net10.0\ControlParental.SampleApp.exe'
if (-not (Test-Path -LiteralPath $sample)) { throw 'Compilar SampleApp en Release primero.' }
$sid = (New-Object Security.Principal.NTAccount($TargetUser)).Translate([Security.Principal.SecurityIdentifier]).Value
$output = Join-Path $root '.local'
New-Item -ItemType Directory -Path $output -Force | Out-Null

# Usar el hash Authenticode calculado por AppLocker, no Get-FileHash.
[xml]$policy = Get-AppLockerFileInformation -Path $sample | New-AppLockerPolicy -RuleType Hash -User $sid -Xml
$collection = $policy.SelectSingleNode('/AppLockerPolicy/RuleCollection[@Type="Exe"]')
if (-not $collection) { throw 'No se genero una coleccion EXE.' }
$hashRules = @($collection.SelectNodes('FileHashRule'))
if ($hashRules.Count -ne 1) { throw 'Se esperaba exactamente una regla de hash para la app de prueba.' }
$deny = $hashRules[0]
$deny.SetAttribute('Action', 'Deny')
$deny.SetAttribute('Name', 'ControlParental laboratorio: solo SampleApp')
$deny.SetAttribute('Description', 'Regla de laboratorio para la aplicacion inocua; no es una lista de produccion.')
$deny.SetAttribute('Id', 'fac3b090-609c-4ff6-922d-c67491174e5a')
$collection.SetAttribute('EnforcementMode', 'AuditOnly')

# Permitir lo demas explicitamente: evita convertir la coleccion en allowlist accidental.
$allow = $policy.CreateElement('FilePathRule')
foreach ($pair in @(@('Id','530be63d-3c32-4b70-98b7-7a039b8a4f53'),@('Name','Laboratorio: permitir otros EXE'),@('Description','Solo para experimento de denegacion puntual'),@('UserOrGroupSid','S-1-1-0'),@('Action','Allow'))) {
    $allow.SetAttribute($pair[0], $pair[1])
}
$conditions = $policy.CreateElement('Conditions')
$pathCondition = $policy.CreateElement('FilePathCondition')
$pathCondition.SetAttribute('Path','*')
[void]$conditions.AppendChild($pathCondition)
[void]$allow.AppendChild($conditions)
[void]$collection.AppendChild($allow)

# Evitar bloqueo implicito de aplicaciones empaquetadas al configurar EXE.
$appx = $policy.CreateElement('RuleCollection')
$appx.SetAttribute('Type','Appx')
$appx.SetAttribute('EnforcementMode','AuditOnly')
$publisher = $policy.CreateElement('FilePublisherRule')
foreach ($pair in @(@('Id','cd1a7980-d5b3-4316-9d5b-42e145155811'),@('Name','Laboratorio: permitir aplicaciones empaquetadas'),@('Description','Conservar acceso a paquetes firmados'),@('UserOrGroupSid','S-1-1-0'),@('Action','Allow'))) {
    $publisher.SetAttribute($pair[0], $pair[1])
}
$conditions = $policy.CreateElement('Conditions')
$publisherCondition = $policy.CreateElement('FilePublisherCondition')
foreach ($name in 'PublisherName','ProductName','BinaryName') { $publisherCondition.SetAttribute($name,'*') }
$version = $policy.CreateElement('BinaryVersionRange')
$version.SetAttribute('LowSection','0.0.0.0')
$version.SetAttribute('HighSection','*')
[void]$publisherCondition.AppendChild($version)
[void]$conditions.AppendChild($publisherCondition)
[void]$publisher.AppendChild($conditions)
[void]$appx.AppendChild($publisher)
[void]$policy.DocumentElement.AppendChild($appx)
$audit = Join-Path $output 'app-audit.xml'
$enforce = Join-Path $output 'app-enforce.xml'
$policy.Save($audit)
foreach ($c in $policy.AppLockerPolicy.RuleCollection) { $c.EnforcementMode = 'Enabled' }
$policy.Save($enforce)

$decisions = @(Test-AppLockerPolicy -XmlPolicy $enforce -Path $sample,"$env:WINDIR\System32\WindowsPowerShell\v1.0\powershell.exe" -User $sid)
if (@($decisions | Where-Object { $_.PolicyDecision -eq 'Denied' }).Count -ne 1) { throw 'La simulacion no devuelve exactamente una denegacion.' }
if (@($decisions | Where-Object { $_.PolicyDecision -eq 'DeniedByDefault' }).Count) { throw 'Denegacion implicita inesperada.' }
foreach ($decision in $decisions) { Write-Output ((Split-Path $decision.FilePath -Leaf) + ': ' + $decision.PolicyDecision) }
Write-Output 'Politicas generadas y evaluadas. NO se ha cambiado la politica del sistema.'
Write-Output 'La evaluacion XML no demuestra bloqueo real. No recompilar SampleApp entre auditoria y bloqueo.'
