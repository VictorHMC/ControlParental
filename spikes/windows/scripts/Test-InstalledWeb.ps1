#Requires -Version 5.1
[CmdletBinding()]
param([Parameter(Mandatory=$true)][ValidateSet('Inactive','Active')][string]$ExpectedMode)
$ErrorActionPreference = 'Stop'
$agent = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'ControlParentalLabAgent\ControlParental.Agent.exe'
if (-not (Test-Path -LiteralPath $agent)) { throw 'El agente aun no esta instalado.' }
$status = & $agent status 2>&1
if ($LASTEXITCODE -ne 0) { throw ($status | Out-String) }
$status | Write-Output
$expectedText = if ($ExpectedMode -eq 'Active') { 'HTTPS: ACTIVO\.' } else { 'HTTPS: INACTIVO\.' }
if (($status | Out-String) -notmatch $expectedText) { throw 'El modo observado no coincide con la fase de prueba.' }

function Probe([string]$url) {
    $errorFile = Join-Path $env:TEMP ('controlparental-curl-' + [guid]::NewGuid().ToString('N') + '.txt')
    try {
        # Probar HTTPS a traves del mismo proxy que usara el navegador de laboratorio.
        $result = & curl.exe --silent --noproxy 'no-bypass.invalid' --proxy 'http://127.0.0.1:18777' --connect-timeout 10 --max-time 20 --output NUL --write-out 'connect=%{http_connect};http=%{http_code}' $url 2>$errorFile
        $exitCode = $LASTEXITCODE
        [pscustomobject]@{ Url=$url; CurlExit=$exitCode; Codes=($result -join ''); Error=((Get-Content -LiteralPath $errorFile -Raw -ErrorAction SilentlyContinue) -as [string]) }
    } finally { Remove-Item -LiteralPath $errorFile -ErrorAction SilentlyContinue }
}
$blocked = Probe 'https://example.com'
$allowed = Probe 'https://example.org'
foreach ($result in @($blocked,$allowed)) {
    Write-Output ($result.Url + ': ' + $result.Codes + ';curl=' + $result.CurlExit)
    if ($result.Error) { Write-Output $result.Error.Trim() }
}
if ($ExpectedMode -eq 'Active') {
    if ($blocked.Codes -notmatch 'connect=403;') { throw 'No se observo rechazo 403 del dominio restringido.' }
} elseif ($blocked.CurlExit -ne 0 -or $blocked.Codes -notmatch 'connect=200;http=2\d\d') {
    throw 'Fallo la linea base del dominio antes del bloqueo.'
}
if ($allowed.CurlExit -ne 0 -or $allowed.Codes -notmatch 'connect=200;http=2\d\d') { throw 'El dominio permitido no cargo correctamente.' }
Write-Output 'Prueba HTTPS del agente instalado aprobada para la fase indicada.'
