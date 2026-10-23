# ADR 0002: modo hijo en sesion compartida y desbloqueo autenticado

Fecha: 2026-10-23. Estado: aceptada como requisito; implementacion de servicio pendiente.

## Contexto

Los hijos pueden utilizar la sesion de Windows del padre. El usuario requiere que desbloquear exija una contrasena definida por el padre o administrador. Una cuenta infantil separada sigue siendo opcional; no es requisito del recorrido inicial.

## Decision

- Activar un modo hijo en la misma sesion. Las restricciones afectan al usuario de Windows de esa sesion, incluido el padre mientras el modo este activo.
- Verificar contrasena del responsable para desactivar restricciones, modificar reglas o reemplazar la credencial. No reutilizar ni pedir la contrasena de inicio de Windows.
- Separar interfaz Flutter y servicio Windows. Cerrar la interfaz no sera una solicitud de desbloqueo. El servicio mantiene reglas activas y realiza la autorizacion.
- Guardar una derivacion criptografica con salt aleatorio y coste revisable; no la contrasena. Proteger archivos y canales mediante permisos del sistema y limitar intentos con estado persistente.
- Impedir que una nueva instalacion o reinicio de interfaz reemplace sin autorizacion la credencial existente. Recuperacion mediante procedimiento administrativo documentado; nunca una puerta oculta.
- No publicar credenciales, hashes de contrasena o evidencias privadas en GitHub.

## Limites

Una aplicacion no puede garantizar resistencia frente a alguien que usa una cuenta administradora y puede modificar sus servicios y politicas. La contrasena protege el flujo normal; la separacion de privilegios de Windows determina la resistencia a manipulacion. Se mostrara esta diferencia al responsable.

El primer laboratorio implementa verificacion de contrasena en consola y scripts de AppLocker, con estado privado ignorado por Git. No implementa aun el servicio ni almacenamiento resistente a manipulacion. Cerrar el filtro o borrar sus archivos evade su proteccion de laboratorio; esto debe resolverse antes de piloto.

## Criterios de aceptacion para el servicio

Contrasena incorrecta no cambia politica; contrasena correcta permite desbloquear; cerrar/reabrir UI y reiniciar no desbloquean; cambio de credencial requiere autorizacion anterior; intentos fallidos mantienen su limite al reiniciar UI; usuario sin privilegios no puede editar credencial o detener servicio; recuperacion administrativa probada. Para sesion administradora, registrar las evasiones disponibles y no aprobar resistencia que no exista.
