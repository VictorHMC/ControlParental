#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
foreach ($script in (Get-ChildItem -LiteralPath $PSScriptRoot -Filter *.ps1)) {
    $tokens = $null
    $errors = $null
    [void][Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw ($errors | Out-String) }
}
Write-Output 'Sintaxis PowerShell valida.'
& dotnet (Join-Path $root 'WebFilter\bin\Release\net10.0\WebFilter.dll') --self-test
if ($LASTEXITCODE -ne 0) { throw 'Fallaron las pruebas del filtro.' }
& (Join-Path $root 'SampleApp\bin\Release\net10.0\ControlParental.SampleApp.exe')
if ($LASTEXITCODE -ne 0) { throw 'Fallo la linea base de SampleApp.' }
Write-Output 'Linea base aprobada. Este script no aplica politicas AppLocker.'
