# WINDOWS-01: preparacion y primeras comprobaciones

Fecha: 2026-10-23. Estado: parcial. Prototipo: `spikes/windows`.

## Entorno observado

Windows 11 Pro 25H2, build 26200.9711. .NET SDK 10.0.401 y runtime 10.0.12. AppLocker 2.0 disponible en Windows PowerShell 5.1. Application Identity detenido, inicio manual. Edge y Chrome encontrados. Flutter no localizado en PATH.

La lectura de politicas locales AppLocker devolvio acceso denegado con el token disponible. La consulta de sistema y pruebas de sockets se repitieron fuera del sandbox de desarrollo; eso no concede elevacion UAC. No se ha aplicado una politica ni iniciado el servicio.

## Resultados

| Comprobacion | Resultado | Alcance |
| --- | --- | --- |
| Compilacion filtro y SampleApp | Correcta, cero errores y advertencias | .NET 10, sin dependencias externas |
| Baseline SampleApp | Ejecuta y sale con codigo 0 | Antes de restricciones |
| Contrasena y derivacion | Correcta aceptada, incorrecta rechazada, salt aleatorio, sin texto plano | Pruebas automatizadas; interfaz interactiva pendiente |
| Filtro HTTPS CONNECT | Dominio bloqueado devuelve 403 sin abrir destino | Socket local; lista de dominios de prueba |
| Tunel permitido | 200 y transporte bidireccional | Destino local inyectado solo en test |
| Normalizacion | Mayusculas, punto final, IDN y subdominios | Sin falsos positivos por sufijo parcial |
| Restricciones de protocolo | IP directa y puerto no permitido rechazados; HTTP plano 501 | Filtro de laboratorio |
| Evaluacion XML AppLocker | SampleApp Denied; PowerShell Allowed | Simulacion con Test-AppLockerPolicy; no ejecucion bloqueada |

18 comprobaciones automatizadas del filtro/autenticacion aprobadas. No son tres repeticiones del protocolo manual ni certifican proteccion global.

## Pendiente

- Establecer contrasena real de laboratorio en consola, sin compartirla en chat.
- Auditoria 8003, bloqueo real, eventos 8004 y restauracion autenticada en consola administradora.
- Probar Edge/Chrome, con y sin proxy, y documentar el alcance.
- Tres recorridos bloquear/verificar/desbloquear; apps permitidas; copia/renombrado y evasiones por DLL.
- Reinicio, cambio de red, falta de internet y recuperacion tras fallo.
- Servicio con credencial protegida, continuidad al cerrar interfaz y limites de intentos persistidos.

## Decision

Mantener bloqueo de ejecucion y web como **pendientes de validacion de plataforma**. Las pruebas de componentes funcionan; faltan los recorridos reales. No cambiar la matriz a aprobada. No hay cambios de configuracion del sistema que revertir.
