#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot 'Install-Agent.ps1'
$tokens = $null
$errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($installer,[ref]$tokens,[ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
$command = @($ast.FindAll({ param($node)
    $node -is [Management.Automation.Language.CommandAst] -and $node.GetCommandName() -eq 'Add-Type' -and $node.Extent.Text -like '*LabPrivateDirectory*'
}, $true))
if ($command.Count -ne 1) { throw 'Se esperaba una unica definicion nativa del instalador.' }
$literal = @($command[0].CommandElements | Where-Object { $_ -is [Management.Automation.Language.StringConstantExpressionAst] -and $_.Value -like '*public static class LabPrivateDirectory*' })
if ($literal.Count -ne 1) { throw 'No se encontro el literal de codigo esperado.' }
Add-Type -TypeDefinition $literal[0].Value
Write-Output 'Helper nativo del instalador compilado. No se invoco Create ni se modificaron directorios del sistema.'
$descriptor = New-Object Security.AccessControl.RawSecurityDescriptor('O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)')
if ($descriptor.DiscretionaryAcl.Count -ne 2) { throw 'DACL inicial inesperada.' }
Write-Output 'DACL inicial privada validada: solo SYSTEM y administradores.'
