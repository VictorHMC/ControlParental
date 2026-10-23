#Requires -Version 5.1
#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$name = 'ControlParentalLabAgent'
$exe = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) "$name\ControlParental.Agent.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw 'No se encontro el ejecutable instalado.' }
$service = Get-Service -Name $name -ErrorAction SilentlyContinue
if ($service -and $service.Status -eq 'Running') {
    & $exe deactivate
} else {
    # Recuperacion administrativa, con contrasena, cuando el servicio esta detenido.
    & $exe authorize-local
}
if ($LASTEXITCODE -ne 0) { throw 'Desinstalacion no autorizada.' }
if ($service) {
    Stop-Service -Name $name -ErrorAction Stop
    $sc = Join-Path ([Environment]::SystemDirectory) 'sc.exe'
    & $sc delete $name
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo eliminar el registro del servicio.' }
}
Write-Output 'Servicio retirado. Se conservan binarios y estado protegido para recuperacion; no se borran automaticamente.'
Write-Output 'Cerrar el navegador de laboratorio. No se han modificado DNS, proxy del sistema ni AppLocker.'
