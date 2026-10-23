#Requires -Version 5.1
#Requires -RunAsAdministrator
[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$TargetSid)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$statusFile = Join-Path $root '.local\install-status.json'
$serviceName = 'ControlParentalLabAgent'
$binaryRoot = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) $serviceName
$dataRoot = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) $serviceName
$published = Join-Path $root '.local\publish-agent'
$executable = Join-Path $binaryRoot 'ControlParental.Agent.exe'
$sc = Join-Path ([Environment]::SystemDirectory) 'sc.exe'
$createdService = $false

# Crear directorios privados atomicamente: no heredar permisos de ProgramData
# ni aceptar un directorio/junction creado por otro proceso entre comprobaciones.
Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
public static class LabPrivateDirectory {
    [StructLayout(LayoutKind.Sequential)] private struct SecurityAttributes {
        public int Length; public IntPtr Descriptor;
        [MarshalAs(UnmanagedType.Bool)] public bool Inherit;
    }
    [DllImport("advapi32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text, uint revision, out IntPtr descriptor, out uint size);
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    private static extern bool CreateDirectoryW(string path, ref SecurityAttributes attributes);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr value);
    public static void Create(string path) {
        IntPtr descriptor;
        uint size;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW("O:BAG:BAD:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)", 1, out descriptor, out size))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try {
            var attributes = new SecurityAttributes { Length=Marshal.SizeOf(typeof(SecurityAttributes)), Descriptor=descriptor, Inherit=false };
            if (!CreateDirectoryW(path, ref attributes)) throw new Win32Exception(Marshal.GetLastWin32Error());
        } finally { LocalFree(descriptor); }
    }
}
'@

function Report([string]$state, [string]$message) {
    @{ State=$state; Message=$message; Date=(Get-Date).ToUniversalTime().ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $statusFile -Encoding UTF8
}
function Set-PrivateDirectory([string]$path, [string]$serviceSid, [bool]$isData) {
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true,$false)
    $acl.SetOwner((New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')))
    foreach ($sid in 'S-1-5-18','S-1-5-32-544') {
        $rule = New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($sid)), 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
        $acl.AddAccessRule($rule)
    }
    $rights = if ($isData) { 'Modify' } else { 'ReadAndExecute' }
    $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($serviceSid)), $rights, 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    if (-not $isData) {
        $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule((New-Object Security.Principal.SecurityIdentifier($TargetSid)), 'ReadAndExecute', 'ContainerInherit,ObjectInherit', 'None', 'Allow')))
    }
    Set-Acl -LiteralPath $path -AclObject $acl
}
try {
    Report 'Preparing' 'Comprobando requisitos sin activar restricciones.'
    $identitySid = New-Object Security.Principal.SecurityIdentifier($TargetSid)
    if (-not $identitySid.IsAccountSid()) { throw 'Se requiere SID de usuario.' }
    if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) { throw 'El servicio ya existe; no se sobrescribira.' }
    if ((Test-Path -LiteralPath $binaryRoot) -or (Test-Path -LiteralPath $dataRoot)) { throw 'Existen directorios de una instalacion anterior. Revisar antes de reinstalar.' }
    if (-not (Test-Path -LiteralPath (Join-Path $published 'ControlParental.Agent.exe'))) { throw 'Publicar Agent en .local\publish-agent antes de instalar.' }
    $probe = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback,18777)
    try { $probe.Start() } finally { $probe.Stop() }

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    $form = New-Object Windows.Forms.Form
    $form.Text = 'ControlParental - configurar contrasena local'
    $form.ClientSize = New-Object Drawing.Size(500,270)
    $form.StartPosition = 'CenterScreen'
    $form.FormBorderStyle = 'FixedDialog'
    $form.MaximizeBox = $false
    $form.TopMost = $true
    $label = New-Object Windows.Forms.Label
    $label.Text = "Crea la contrasena del responsable para este laboratorio.`nNo es tu contrasena de Windows. Minimo 12 caracteres.`nSe instalara un servicio; el modo hijo empezara DESACTIVADO."
    $label.SetBounds(20,15,460,65)
    $first = New-Object Windows.Forms.TextBox
    $first.UseSystemPasswordChar = $true
    $first.MaxLength = 1024
    $first.SetBounds(20,90,460,25)
    $second = New-Object Windows.Forms.TextBox
    $second.UseSystemPasswordChar = $true
    $second.MaxLength = 1024
    $second.SetBounds(20,130,460,25)
    $note = New-Object Windows.Forms.Label
    $note.Text = 'Repite la contrasena en el segundo campo.'
    $note.SetBounds(20,165,460,40)
    $accept = New-Object Windows.Forms.Button
    $accept.Text = 'Instalar'
    $accept.SetBounds(255,220,105,30)
    $accept.Add_Click({
        if ($first.Text.Length -lt 12 -or $first.Text -ne $second.Text) { $note.Text = 'Minimo 12 caracteres y ambos campos deben coincidir.'; return }
        $form.DialogResult = [Windows.Forms.DialogResult]::OK
        $form.Close()
    })
    $cancel = New-Object Windows.Forms.Button
    $cancel.Text = 'Cancelar'
    $cancel.DialogResult = [Windows.Forms.DialogResult]::Cancel
    $cancel.SetBounds(375,220,105,30)
    $form.Controls.AddRange(@($label,$first,$second,$note,$accept,$cancel))
    $form.AcceptButton = $accept
    $form.CancelButton = $cancel
    Report 'AwaitingPassword' 'Introduce una contrasena en la ventana local, no en el chat.'
    if ($form.ShowDialog() -ne [Windows.Forms.DialogResult]::OK) { Report 'Cancelled' 'Instalacion cancelada sin cambios del sistema.'; return }
    $secret = $first.Text
    $first.Clear()
    $second.Clear()
    $form.Dispose()

    # Las rutas fijas deben estar ausentes; no seguir junctions ni instalaciones previas.
    [LabPrivateDirectory]::Create($binaryRoot)
    [LabPrivateDirectory]::Create($dataRoot)
    # Registrar sin arrancar; obtener SID exclusivo para no compartir datos con otros LocalService.
    $empty = New-Object Security.SecureString
    $credential = New-Object Management.Automation.PSCredential('NT AUTHORITY\LocalService',$empty)
    New-Service -Name $serviceName -DisplayName 'ControlParental (laboratorio)' -BinaryPathName ('"{0}" --service' -f $executable) -StartupType Automatic -Credential $credential | Out-Null
    $createdService = $true
    & $sc sidtype $serviceName unrestricted | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo configurar el SID exclusivo del servicio.' }
    $serviceSid = (New-Object Security.Principal.NTAccount("NT SERVICE\$serviceName")).Translate([Security.Principal.SecurityIdentifier]).Value
    Set-PrivateDirectory $binaryRoot $serviceSid $false
    Set-PrivateDirectory $dataRoot $serviceSid $true
    Copy-Item -Path (Join-Path $published '*') -Destination $binaryRoot -Recurse -ErrorAction Stop
    Copy-Item -LiteralPath (Join-Path $root 'blocked-domains.txt') -Destination (Join-Path $binaryRoot 'blocked-domains.txt') -ErrorAction Stop
    $process = New-Object Diagnostics.Process
    $process.StartInfo.FileName = $executable
    $process.StartInfo.Arguments = 'initialize ' + $TargetSid
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.RedirectStandardInput = $true
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    [void]$process.Start()
    $process.StandardInput.WriteLine($secret)
    $process.StandardInput.WriteLine($secret)
    $process.StandardInput.Close()
    $secret = $null
    if (-not $process.WaitForExit(30000)) { $process.Kill(); throw 'Agotado tiempo para configurar credencial.' }
    if ($process.ExitCode -ne 0) { throw ('No se pudo inicializar: ' + $process.StandardError.ReadToEnd()) }
    $process.Dispose()
    & $sc failure $serviceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo configurar recuperacion.' }
    & $sc failureflag $serviceName 1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo configurar recuperacion de errores.' }
    Start-Service -Name $serviceName
    (Get-Service -Name $serviceName).WaitForStatus('Running',[TimeSpan]::FromSeconds(15))
    & $executable status | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'El servicio no responde por IPC.' }
    Report 'Installed' 'Servicio instalado e inactivo. La contrasena no se guardo en el repositorio.'
} catch {
    if ($createdService) {
        Stop-Service -Name $serviceName -ErrorAction SilentlyContinue
        & $sc delete $serviceName | Out-Null
    }
    Report 'Failed' $_.Exception.Message
    # Conservar directorios para diagnostico; nunca borrar credenciales automaticamente.
    throw
} finally { $secret = $null }
