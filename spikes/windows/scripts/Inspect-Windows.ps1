#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
$os = Get-CimInstance Win32_OperatingSystem
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object Security.Principal.WindowsPrincipal($identity)
$result = [ordered]@{
    Windows = $os.Caption
    Version = $os.Version
    Build = $os.BuildNumber
    Elevated = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    PowerShell = $PSVersionTable.PSVersion.ToString()
    AppLockerModule = [bool](Get-Module -ListAvailable AppLocker)
    Dotnet = [bool](Get-Command dotnet -ErrorAction SilentlyContinue)
    AppIdentity = (Get-Service AppIDSvc).Status.ToString()
}
[pscustomobject]$result | Format-List
# No incluir nombre de usuario ni SID en el informe publico.
