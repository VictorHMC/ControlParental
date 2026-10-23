# Agente persistente de laboratorio Windows

Fecha: 2026-10-23. El proyecto `Agent` compila sin paquetes externos. Hay 32 pruebas del motor/IPC y 18 del filtro aprobadas. La instalacion como servicio, sus ACL efectivas y la navegacion real se verifican por separado: los tests del motor no certifican SCM, UAC, reinicio de Windows ni cobertura global.

## Que resuelve este incremento

El filtro deja de depender de la ventana del responsable. Un servicio se ejecuta como LocalService con SID exclusivo, conserva el modo hijo y verifica la contrasena en su propio proceso. El cliente puede cerrarse sin enviar una orden de desbloqueo.

El estado incluye huella PBKDF2, salt, SID de la sesion objetivo, modo activo/inactivo e intentos fallidos. Se guarda antes de confirmar un cambio. Tres contrasenas incorrectas bloquean nuevos intentos durante 30 segundos; la espera se conserva al recrear el motor o reiniciar el servicio. Una contrasena correcta no omite una espera todavia vigente.

El cambio de modo desconecta las conexiones actuales del proxy para que un tunel abierto antes de activar no conserve acceso al dominio restringido. El proxy sigue escuchando estando inactivo y permite dominios de la lista; sus limitaciones de protocolo de laboratorio (solo HTTPS CONNECT, sin IP directa ni destinos privados) siguen presentes.

**Alcance:** filtro HTTPS del navegador configurado con proxy. AppLocker sigue con su protocolo separado. No se ha implementado aun activacion conjunta de bloqueo de aplicaciones y web, ni categorias automaticas, ni filtro global WFP.

## Separacion de procesos y permisos

- Binarios y lista de dominios: `%ProgramFiles%\ControlParentalLabAgent`, escritura solo para administradores/SYSTEM; lectura y ejecucion para el servicio y el usuario objetivo.
- Credencial y estado: `%ProgramData%\ControlParentalLabAgent`, acceso de administradores/SYSTEM y del SID exclusivo del servicio. El usuario objetivo no obtiene acceso a la credencial por ser usuario de la interfaz.
- El instalador crea directorios nuevos con DACL privada de forma atomica; rechaza rutas/instalaciones previas. No sigue ni reemplaza directorios existentes.
- IPC local con ACL para el usuario objetivo, administradores y servicio. Las conexiones de red se deniegan. El usuario no recibe permiso de crear instancias del pipe.
- Antes de enviar una contrasena, el cliente comprueba el binario instalado en Program Files y que su PID coincide con el servicio registrado y activo en SCM. No basta con ejecutar otra copia del mismo binario.
- El protocolo solo admite estado, activacion, desactivacion y cambio de contrasena; no recibe rutas, programas ni comandos del sistema.
- Un error de estado no genera una credencial nueva ni un modo inactivo. El servicio informa fallo, conserva el estado y usa la recuperacion configurada en SCM.

Esto mejora la separacion para una sesion estandar. No impide que quien disponga de privilegios administrativos modifique servicios, binarios o politicas. El instalador y binarios de laboratorio aun no estan firmados para distribucion.

## Compilar y publicar localmente

Desde `spikes/windows`:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.local'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet build Agent/Agent.csproj -c Release --configfile NuGet.Config
dotnet build WebFilter/WebFilter.csproj -c Release --configfile NuGet.Config
dotnet build SampleApp/SampleApp.csproj -c Release --configfile NuGet.Config
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Prototype.ps1
dotnet publish Agent/Agent.csproj -c Release --no-restore --no-self-contained -o .local/publish-agent
```

Los tests usan directorios aleatorios dentro del resultado de compilacion, pipes de nombre aleatorio y sockets locales. No instalan el servicio ni escriben en Program Files/ProgramData. En un entorno de desarrollo aislado pueden requerir permiso para abrir sockets fuera de ese aislamiento; eso no equivale a UAC de administrador.

## Instalar en este equipo

La entrada mas sencilla es abrir `Instalar-ControlParental.cmd` desde el Explorador de archivos. Compila el agente, conserva el usuario de la sesion original, solicita UAC y abre la ventana privada de contrasena. Si termina correctamente, abre el control del modo hijo. Si se cancela UAC no vuelve a solicitarlo automaticamente. No se debe ejecutar como actualizador de una instalacion previa.

Las ventanas del instalador y control se lanzan en modo visible; puede verse una consola temporal detras del formulario. `Recover-Installer.ps1` permite recuperar exclusivamente un intento atascado en `AwaitingPassword`: exige administrador, verifica los PID y rutas de ambos scripts y rechaza actuar si existen archivos o servicio instalado.

Cerrar primero el filtro de consola anterior para liberar `127.0.0.1:18777`. Obtener el SID desde la sesion que usaran padre/hijo, antes de cambiar de cuenta para UAC:

```powershell
$targetSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
```

En Windows PowerShell 5.1 elevada, ejecutar el instalador con ese SID:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Install-Agent.ps1 -TargetSid $targetSid
```

Se abrira una ventana privada con dos campos para crear la contrasena del responsable. No pide ni utiliza la contrasena de Windows. La contrasena viaja al inicializador por stdin anonimo, nunca en argumentos ni archivos temporales. La configuracion empieza INACTIVA.

El instalador registra `ControlParentalLabAgent` con inicio automatico, cuenta LocalService, SID de servicio y recuperacion por reinicio. No cambia DNS, firewall, proxy del sistema ni AppLocker. Si falla, intenta retirar el registro del servicio y conserva archivos para diagnostico; comprobar el estado antes de reintentar. El progreso sin secretos se escribe en `.local/install-status.json`.

No ejecutar el instalador de nuevo para actualizar o recuperar credenciales: rechaza sobrescribir una instalacion existente. Un procedimiento de actualizacion versionado sigue pendiente.

## Usar el modo hijo

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Show-AgentControl.ps1
```

La ventana permite consultar estado, activar y desbloquear. Activar y desbloquear requieren la contrasena. Cerrar la ventana no cambia el modo.

Alternativa de consola:

```powershell
$agent = Join-Path $env:ProgramFiles 'ControlParentalLabAgent\ControlParental.Agent.exe'
& $agent status
& $agent activate
& $agent deactivate
& $agent change-password
```

La contrasena se solicita con entrada oculta. La opcion interna `--stdin-password` permite al cliente grafico usar un canal anonimo; no contiene una contrasena ni evita la verificacion del servicio.

Configurar solo el perfil de navegador de laboratorio descrito en [README](README.md), apuntando al proxy `127.0.0.1:18777`. Con modo inactivo, `https://example.com` debe cargar; con modo activo debe rechazarse, y `https://example.org` debe seguir cargando. Comprobar en dos navegadores por separado. No se instala un certificado ni se descifra TLS.

## Recorrido de validacion real

1. Confirmar servicio Running y estado INACTIVO por IPC desde la cuenta objetivo sin elevacion.
2. Abrir el navegador de prueba y comprobar linea base de ambos dominios.
3. Activar con contrasena y verificar dominio bloqueado/permitido.
4. Cerrar y reabrir la ventana: sigue ACTIVO. Una contrasena incorrecta no desbloquea.
5. Provocar tres fallos y cerrar/reabrir UI: la espera sigue vigente. Reiniciar el servicio desde administrador y comprobar la espera si aun no ha vencido.
6. Desbloquear con contrasena correcta una vez vencida la espera y verificar acceso restaurado.
7. Repetir tres veces. Solo despues, probar reinicio de Windows con recuperacion preparada y verificar modo persistido, inicio automatico y tiempo sin proteccion.
8. Registrar ACL efectivas, denegacion de acceso a estado desde usuario estandar y limites de una sesion administradora.

Estos pasos siguen pendientes hasta adjuntar evidencia real. Las pruebas automatizadas del motor recrean el controlador y reconectan clientes; no son un reinicio del equipo ni la ejecucion del servicio bajo SCM.

`scripts/Test-InstalledWeb.ps1 -ExpectedMode Inactive` comprueba linea base por HTTPS; despues de activar y cerrar la ventana, ejecutar con `-ExpectedMode Active`. La prueba consulta al agente y utiliza el mismo proxy del navegador, sin contrasenas en sus argumentos. Exige rechazo CONNECT 403 para `example.com` y acceso HTTPS 2xx para `example.org`. No sustituye la comprobacion visual en navegador.

## Retirar o recuperar

Cerrar el perfil de navegador de laboratorio antes de retirar el proxy. En consola administradora:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Uninstall-Agent.ps1
```

La herramienta exige contrasena, desactiva y retira el servicio. Si esta detenido, la recuperacion local exige administrador y contrasena. Conserva binarios y datos para diagnostico, sin borrar credenciales automaticamente. No modifica politicas AppLocker creadas en el otro experimento.

Recuperacion excepcional si se pierde la contrasena: un administrador puede detener el servicio y retirar su registro mediante las herramientas de Windows. Esto es una capacidad explicita del administrador; no hay contrasena maestra ni se anuncia resistencia frente a esos privilegios. Conservar el estado para diagnostico antes de cualquier restablecimiento. El flujo completo de recuperacion del responsable sigue pendiente del producto.

## Fuentes oficiales

- [Seguridad de named pipes](https://learn.microsoft.com/en-us/windows/win32/ipc/named-pipe-security-and-access-rights).
- [ACL de named pipes en .NET](https://learn.microsoft.com/en-us/dotnet/api/system.io.pipes.namedpipeserverstreamacl.create?view=net-10.0).
- [Creacion de servicios Windows](https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/sc-create).
- [Estado y PID del servicio](https://learn.microsoft.com/en-us/windows/win32/api/winsvc/nf-winsvc-queryservicestatusex).
