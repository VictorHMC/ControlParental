# WINDOWS-01: primer laboratorio nativo

Estado: compilado; pruebas locales del filtro y evaluacion XML completadas. Bloqueo real de ejecucion, navegacion en navegador, restauracion de politicas y persistencia tras reinicio **pendientes**. No hay servicio instalado.

## Modalidad acordada

El hijo puede utilizar la sesion del padre. El futuro modo hijo se activa dentro de esa sesion y exige contrasena del responsable para desbloquear o cambiar reglas. Durante el modo hijo, las restricciones afectan tambien al padre que use esa misma cuenta.

La contrasena se verifica con PBKDF2-SHA256, 600000 iteraciones, salt aleatorio de 32 bytes y comparacion de tiempo constante; no se guarda en texto plano ni se pasa como argumento. El prototipo impone espera de 30 segundos cada tres fallos. Esta espera se reinicia al terminar el proceso: su persistencia corresponde al futuro servicio.

Este laboratorio usa un archivo privado dentro de `.local` y procesos de consola: **no protege contra eliminacion de credenciales, modificacion de archivos, terminacion de procesos ni cambios hechos por un administrador**. Cerrar o terminar el filtro lo detiene; no constituye el modo hijo definitivo. AppLocker, en cambio, conserva una politica aplicada aunque cierre su consola. No introducir una contrasena reutilizada en otros servicios.

En produccion, el servicio separado mantendra la proteccion, verificara la contrasena y protegera credenciales y reglas con permisos del sistema. El restablecimiento de contrasena exigira autenticar al responsable. Una sesion con privilegios administrativos sigue pudiendo alterar la instalacion; una contrasena de la app no elimina esa facultad de Windows.

## Herramientas disponibles

| Herramienta | Hallazgo en este equipo | Uso |
| --- | --- | --- |
| Windows | Windows 11 Pro, build 26200.9711, 25H2 | Equipo de prueba |
| .NET SDK | 10.0.401 | Compilar app inocua y filtro |
| Windows PowerShell | 5.1, modulo AppLocker 2.0 | Generar, evaluar y aplicar politicas |
| Application Identity | Instalado, detenido al inspeccionar, inicio manual | Necesario para ejecucion de AppLocker |
| Edge y Chrome | Ejecutables encontrados | Prueba manual de navegador pendiente |
| Flutter | No localizado en PATH | No necesario para esta prueba nativa |

No hace falta instalar paquetes NuGet, Flutter, Visual Studio ni un driver para estos experimentos. Los dos proyectos no tienen dependencias externas y NuGet.Config desactiva fuentes remotas. Para integrar Flutter Desktop se revisaran mas adelante sus herramientas de compilacion.

## Compilar y verificar sin modificar Windows

Abrir PowerShell en este directorio:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.local'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet build WebFilter/WebFilter.csproj -c Release --configfile NuGet.Config
dotnet build SampleApp/SampleApp.csproj -c Release --configfile NuGet.Config
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Test-Prototype.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Prepare-AppPolicy.ps1
```

`ExecutionPolicy Bypass` se limita al proceso de prueba; no cambia la politica del equipo. Revisar los scripts antes de ejecutarlos. `Prepare-AppPolicy` usa por defecto el usuario actual (sesion compartida); permite `-TargetUser` para otra cuenta. Genera XML en `.local` y evalua el hash de SampleApp; no aplica ninguna politica.

La prueba automatica del proxy usa sockets loopback y un destino local inyectado exclusivamente en el test. Demuestra filtrado CONNECT y transporte bidireccional; no demuestra acceso HTTPS a internet ni filtrado de todos los navegadores.

## Configurar la contrasena de laboratorio

En una consola interactiva, desde este directorio:

```powershell
dotnet WebFilter/bin/Release/net10.0/WebFilter.dll --setup-password .local/parent-password.json
```

Introduce una contrasena de al menos 12 caracteres y confirmala. Si existe una credencial, se exige la anterior para reemplazarla. No escribir la contrasena en esta conversacion ni guardarla en scripts. `.local` esta excluido de Git, incluyendo credenciales, SID, backups y perfiles del navegador.

## Prueba de dominios HTTPS

Terminal 1:

```powershell
dotnet WebFilter/bin/Release/net10.0/WebFilter.dll --rules blocked-domains.txt --password-file .local/parent-password.json
```

Terminal 2, abrir Edge con perfil de prueba separado (evita reutilizar un proceso con otra configuracion):

```powershell
$edge = 'C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe'
$profile = Join-Path (Get-Location) '.local\edge-lab'
Start-Process -FilePath $edge -ArgumentList @('--user-data-dir="' + $profile + '"','--proxy-server=http://127.0.0.1:18777','--disable-quic','--no-first-run','https://example.org') -WindowStyle Hidden
```

La ventana puede iniciarse oculta por el lanzador; seleccionarla desde la barra de tareas para la prueba. Visitar `https://example.org`: debe cargar. Visitar `https://example.com`: debe fallar por rechazo del tunel. Documentar ambas capturas sin datos personales. Abrir el mismo sitio desde un navegador sin proxy demuestra el limite de cobertura.

Para detener desde la terminal del filtro, escribir `DESBLOQUEAR` e introducir la contrasena. Ctrl+C no desbloquea mediante la interfaz normal. No se instala un certificado ni se descifra TLS. Se bloquean dominio exacto y subdominios; no hay categorias automaticas. HTTP plano devuelve 501, solo se permite CONNECT a 443, se rechazan IP directas y destinos privados. Cada tunel dura como maximo cinco minutos. No es un proxy de produccion ni proteccion global.

Al terminar: cerrar el navegador de laboratorio y el filtro. El navegador habitual no se modifica. No hay cambios en DNS, hosts, firewall ni proxy del sistema.

## Prueba AppLocker: auditoria, bloqueo y recuperacion

Requiere una **consola Windows PowerShell 5.1 ejecutada como administrador**; la elevacion de herramientas de desarrollo no implica un token UAC de administrador. Ejecutar desde este directorio y con la misma cuenta usada al preparar los XML. Si UAC utiliza otra cuenta, volver a preparar con `-TargetUser` para la sesion que se desea restringir.

1. Compilar una vez SampleApp, preparar XML y configurar contrasena. Comprobar que la app abre antes de aplicar reglas.
2. Ejecutar auditoria:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Manage-AppLab.ps1 -Action Audit
.\SampleApp\bin\Release\net10.0\ControlParental.SampleApp.exe
```

3. Revisar el evento 8003 de la regla de laboratorio en `Microsoft-Windows-AppLocker/EXE and DLL`; la app aun debe ejecutarse. El script exige ese evento antes de activar bloqueo.
4. Aplicar y comprobar:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Manage-AppLab.ps1 -Action Enforce
.\SampleApp\bin\Release\net10.0\ControlParental.SampleApp.exe
```

5. La app debe ser rechazada. Comprobar que PowerShell y una app permitida siguen abriendo. Copiar/renombrar SampleApp debe conservar la denegacion por hash. No recompilarla entre estos pasos.
6. Restaurar, introduciendo la contrasena del responsable:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/Manage-AppLab.ps1 -Action Restore
.\SampleApp\bin\Release\net10.0\ControlParental.SampleApp.exe
```

7. Verificar acceso restaurado y repetir el recorrido tres veces antes de aprobar C01/C02/C05/C12. Un reinicio solo se prueba despues de confirmar recuperacion; el prototipo no cambia el inicio manual de AppIDSvc y no garantiza bloqueo al reiniciar.

El script rechaza equipos con reglas locales o efectivas existentes, guarda backup, comprueba cambios concurrentes y solo deniega el hash de nuestra app al SID elegido. Permite otros EXE y paquetes firmados. No aplica a scripts, DLL, MSI o todas las apps. Ejecutar `dotnet ControlParental.SampleApp.dll` es una evasion conocida del experimento: no se anuncia resistencia ante ella. No se utiliza terminacion de procesos como sustituto de bloqueo.

Recuperacion excepcional: un administrador puede revisar y restaurar `.local/app-before.xml` con `Set-AppLockerPolicy -XmlPolicy`, si la herramienta normal falla. Antes debe comprobar que no hubo cambios de politicas de terceros. Esta via administrativa es parte de la recuperacion; no demuestra seguridad frente a un hijo que dispone de privilegios de administrador. No borrar `.local` mientras haya politica activa.

## Siguiente incremento

Servicio Windows separado de la UI; autenticacion y limitacion de intentos persistidas en ubicacion protegida; permisos IPC; configuracion de recuperacion; pruebas reales de bloqueo/desbloqueo y navegacion; despues, filtrado global mediante componentes del sistema/WFP y persistencia tras reinicio. Integrar Flutter cuando se apruebe el recorrido nativo.

Fuentes: [Application Identity](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/applocker/configure-the-application-identity-service), [evaluar politicas](https://learn.microsoft.com/en-us/powershell/module/applocker/test-applockerpolicy), [comportamiento de reglas](https://learn.microsoft.com/en-us/windows/security/application-security/application-control/app-control-for-business/applocker/understanding-applocker-rule-behavior).
