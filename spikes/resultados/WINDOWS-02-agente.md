# WINDOWS-02: agente persistente y cliente local

Fecha: 2026-10-23. Estado: comprobaciones de componentes completadas; instalacion y pruebas bajo SCM pendientes de confirmar. Implementacion y recorrido: [guia del agente](../windows/AGENT.md).

## Cambios

Agente Windows ejecutable sin paquetes externos, adaptador SCM, canal local con ACL, estado persistido, limitacion de intentos y cliente grafico de laboratorio. El instalador prepara directorios privados, servicio LocalService con SID exclusivo y configuracion inicial inactiva. AppLocker sigue separado; no hay integracion automatica de bloqueo de aplicaciones.

## Validacion realizada

- Agent y WebFilter compilan sin errores ni advertencias; Agent publicado localmente.
- 32 comprobaciones del agente aprobadas: autorizar activacion/desactivacion, conservar modo al recrear motor y desconectar cliente, persistir espera de intentos fallidos, cambiar credencial con autorizacion, rechazar protocolo arbitrario, verificar ejecutable y registro SCM del servidor, limitar tamano de mensajes, no desbloquear si falla persistencia y cerrar tuneles previos al activar.
- 18 comprobaciones del filtro/autenticacion original siguen aprobadas.
- Sintaxis de todos los scripts PowerShell verificada.
- Helper nativo del instalador compilado en Windows PowerShell 5.1 sin invocar su creacion de directorios; descriptor de seguridad inicial privado validado.
- No hay contrasenas reales dentro de pruebas, argumentos o repositorio; solo credenciales ficticias generadas durante los tests.

## Evidencias y limites

Las pruebas del agente usan el mismo proceso como servidor de laboratorio y multiples conexiones cliente, con archivos temporales. Comprueban el motor y su protocolo, no un servicio instalado. Los tests de ACL comprueban la DACL del pipe; falta validar permisos efectivos del SID de servicio y archivos despues de instalar.

Pendientes: completar configuracion local de contrasena, comprobar SCM/cliente estandar, navegar con proxy, cerrar UI, reiniciar servicio/equipo, retirar servicio y repetir tres recorridos. No aprobar proteccion del dispositivo hasta completar estas pruebas. No se han instalado reglas AppLocker, filtros WFP ni cambios de proxy global como parte de los tests.

## Intento de instalacion

Se solicito lanzar el instalador mediante UAC. Windows devolvio `El usuario ha cancelado la operacion`; no se inicio el instalador ni se escribio `.local/install-status.json`. No se configuro una contrasena real ni se registro el servicio. No se repitio la solicitud automaticamente.
