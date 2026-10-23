#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$exe = Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'ControlParentalLabAgent\ControlParental.Agent.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Instalar primero el agente.' }
$form = New-Object Windows.Forms.Form
$form.Text = 'ControlParental - modo hijo HTTPS de laboratorio'
$form.ClientSize = New-Object Drawing.Size(540,285)
$form.StartPosition = 'CenterScreen'
$form.FormBorderStyle = 'FixedDialog'
$form.MaximizeBox = $false
$label = New-Object Windows.Forms.Label
$label.Text = "Protege el navegador de prueba configurado con proxy.`nAppLocker se prueba por separado. Cerrar esta ventana NO desbloquea."
$label.SetBounds(20,15,500,50)
$password = New-Object Windows.Forms.TextBox
$password.UseSystemPasswordChar = $true
$password.MaxLength = 1024
$password.SetBounds(20,80,500,25)
$hint = New-Object Windows.Forms.Label
$hint.Text = 'Contrasena del responsable (requerida para activar o desbloquear).'
$hint.SetBounds(20,110,500,25)
$status = New-Object Windows.Forms.Label
$status.SetBounds(20,195,500,70)

function Invoke-Agent([string]$command) {
    try {
        $process = New-Object Diagnostics.Process
        $process.StartInfo.FileName = $exe
        $process.StartInfo.Arguments = $command + ' --stdin-password'
        $process.StartInfo.UseShellExecute = $false
        $process.StartInfo.CreateNoWindow = $true
        $process.StartInfo.RedirectStandardInput = $true
        $process.StartInfo.RedirectStandardOutput = $true
        $process.StartInfo.RedirectStandardError = $true
        [void]$process.Start()
        if ($command -ne 'status') { $process.StandardInput.WriteLine($password.Text) }
        $process.StandardInput.Close()
        $password.Clear()
        if (-not $process.WaitForExit(15000)) { $process.Kill(); throw 'El agente no responde; cerrar la UI no desactiva el servicio.' }
        $status.Text = ($process.StandardOutput.ReadToEnd() + $process.StandardError.ReadToEnd()).Trim()
        $process.Dispose()
    } catch { $status.Text = $_.Exception.Message }
}
$activate = New-Object Windows.Forms.Button
$activate.Text = 'Activar modo hijo'
$activate.SetBounds(20,150,155,32)
$activate.Add_Click({ Invoke-Agent 'activate' })
$deactivate = New-Object Windows.Forms.Button
$deactivate.Text = 'Desbloquear'
$deactivate.SetBounds(190,150,155,32)
$deactivate.Add_Click({ Invoke-Agent 'deactivate' })
$refresh = New-Object Windows.Forms.Button
$refresh.Text = 'Consultar estado'
$refresh.SetBounds(360,150,160,32)
$refresh.Add_Click({ Invoke-Agent 'status' })
$form.Controls.AddRange(@($label,$password,$hint,$status,$activate,$deactivate,$refresh))
$form.Add_Shown({ Invoke-Agent 'status' })
[void]$form.ShowDialog()
$password.Clear()
$form.Dispose()
