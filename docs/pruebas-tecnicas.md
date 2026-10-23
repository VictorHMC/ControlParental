# Protocolos de viabilidad

Todos los casos están **sin ejecutar**. Este documento define pruebas manuales; no certifica capacidades.

## Preparación común

Usar equipos de laboratorio o copias recuperables. Registrar hardware, sistema, herramientas, firma, permisos y modo de instalación. Preparar una aplicación inocua bloqueable, otra permitida y dominios inocuos bajo control del equipo; no navegar a contenido explícito para demostrar el filtro.

Registrar primero el funcionamiento sin restricciones. Probar bloqueo y desbloqueo con hora, resultado esperado/observado y evidencia. Repetir el recorrido tres veces. No aplicar políticas Windows a todo el equipo sin haber probado auditoría, excepciones administrativas y recuperación.

| Caso | Procedimiento | Resultado exigido o limitación que registrar |
| --- | --- | --- |
| C01 | Bloquear app de prueba y abrirla | Ejecución impedida o pantalla de restricción; cortar su red no cuenta |
| C02 | Abrir app permitida | Continúa funcionando |
| C03 | Bloquear dominio de prueba y abrirlo | Acceso impedido en el alcance declarado |
| C04 | Visitar dominio permitido | Sigue accesible |
| C05 | Desbloquear app y dominio | Acceso restaurado sin reinstalación |
| C06 | Cerrar interfaz y reiniciar equipo | Estado recuperado; documentar ventana sin protección |
| C07 | Desconectar internet y activar horario | Restricción local funciona según reglas descargadas |
| C08 | Cambiar Wi-Fi/red móvil donde exista | Protección se restablece; medir interrupción |
| C09 | Revocar permisos/detener componente | Estado degradado local; panel actualizado al comunicar |
| C10 | Cambiar hora y zona horaria | Comportamiento explicado y reproducible |
| C11 | Probar DNS cifrado, VPN alternativa e IP directa | Clasificar bloqueado, evasión o no aplicable; no asumir cobertura |
| C12 | Ejecutar recuperación autorizada | Responsable recupera acceso y elimina políticas de prueba |

En fase 0 se admite una consola local para el estado; la confirmación remota se prueba en fase 2. Cuando un caso no sea posible, marcarlo no aplicable con motivo, sin contarlo como aprobado.

## iPadOS: `IPAD-01`

**Pregunta:** ¿pueden las API oficiales restringir una app y navegación web con el modelo familiar previsto, sin depender de Flutter abierto?

**Requisitos:** Mac, Xcode compatible, iPad físico, familia/cuenta de prueba adecuada y capacidad Family Controls. Registrar por separado firma de desarrollo y distribución. No publicar cuentas, tokens ni perfiles.

**Prototipo:** app Swift mínima con autorización Family Controls, selección mediante selector del sistema, Managed Settings y extensión Device Activity. Guardar selección y horario en el contenedor permitido. Interfaz mínima: autorizar, seleccionar, bloquear, desbloquear y consultar estado.

**Ejecución:** aplicar C01–C12; probar horario con pantalla apagada y app cerrada, reinicio y revocación de autorización. Probar filtro web en Safari y al menos otro navegador disponible y una vista web de prueba; registrar cada resultado independientemente. No extrapolar soporte a todos los navegadores.

**Salida:** captura anonimizada de la restricción, horario ejecutado sin interfaz, recuperación, alcance web y estado de entitlement por target. Las referencias de apps pueden ser tokens opacos; validar qué puede seleccionarse y sincronizarse sin exponerlos.

**Decisión:** desarrollo y distribución tienen estados independientes. Un prototipo que funciona solo en desarrollo queda limitado hasta verificar la ruta de publicación.

## Android: `ANDROID-01`

**Pregunta:** ¿qué protección ofrece una instalación familiar convencional y qué exige administración del dispositivo?

**Requisitos:** dispositivo físico, versión documentada, herramientas Android y un dispositivo separable/restablecible si se prueba modo administrado. Explicar y respaldar antes de cualquier restablecimiento; no ejecutar ese paso en un teléfono personal por defecto.

**Prototipo A, convencional:** Kotlin con `VpnService`, consentimiento explícito, reglas de dominios de prueba y estado persistido. Documentar cómo se identifica el dominio y qué tráfico no cubre. No llamar bloqueo de apps al bloqueo de sus conexiones. Investigar por separado los mecanismos permitidos para restricción de ejecución; permisos de uso no conceden suspensión.

**Prototipo B, administrado:** aprovisionamiento de laboratorio con rol adecuado y suspensión de paquete mediante DevicePolicyManager, cuando el rol lo permita. Registrar alcance: dispositivo o perfil. Comparar con A sin mezclar resultados.

**Ejecución:** C01–C12 en cada modo; incluir optimización de batería, pantalla apagada, reinicio, detención forzada, revocación VPN, desinstalación y conflicto con otra VPN. Registrar si el usuario puede desactivar la protección. Revisar elegibilidad y declaraciones Google Play antes de escoger cualquier dependencia de accesibilidad o VPN para producción.

**Salida:** dos columnas de capacidades A/B, consumo respecto a línea base, persistencia y pasos de instalación reales. Si A no puede bloquear ejecución con fiabilidad, marcar esa capacidad limitada o descartada y proponer el alcance revisado.

## Windows: `WINDOWS-01`

**Pregunta:** ¿podemos restringir ejecución y navegación del hijo manteniendo acceso de recuperación del responsable?

**Requisitos:** equipo o VM recuperable, edición/build registradas y recuperacion administrativa. Se admite sesion compartida del padre con modo hijo y desbloqueo por contrasena segun [ADR 0002](decisiones/0002-sesion-compartida-y-contrasena.md); comparar tambien con cuenta infantil estandar para medir resistencia a manipulacion. Probar integración final también en equipo físico. Conservar mecanismo de recuperación fuera de la política.

Primer [laboratorio Windows](../spikes/windows/README.md) e [informe parcial](../spikes/resultados/WINDOWS-01-inicial.md) disponibles. Siguen pendientes las pruebas reales C01-C12. Agregar pruebas de contrasena incorrecta/correcta, cierre de interfaz y reinicio: el objetivo final es que solo la autorizacion del responsable desbloquee mediante la app, incluso tras cerrar su interfaz.

**Prototipo:** servicio mínimo con registro de estado y aplicación de reglas de red mediante API del sistema; investigar WFP. Evaluar AppLocker/App Control por edición, alcance por usuario y actualización. C#/.NET es candidato para el servicio; la integración WFP puede requerir interoperabilidad nativa. La selección final queda pendiente de evidencia.

**Ejecución:** comenzar en modo auditoría para control de aplicaciones; revisar alcance y excepciones antes de activar una regla sobre la app inocua. Ejecutar C01–C12 con dos navegadores, cambio de usuario, app portable/renombrada, apagado del servicio y reinicio. Verificar que no se bloqueen herramientas de recuperación, sesión administrativa ni componentes esenciales. Registrar si una política afecta a todo el equipo.

**Salida:** demostración separada de ejecución y red, edición soportada, necesidad de elevación, comportamiento por usuario y reversión comprobada. No aprobar un diseño basado únicamente en cerrar procesos después de que arranquen.

## Matriz de decisión inicial

| Capacidad | iPadOS | Android convencional | Android administrado | Windows |
| --- | --- | --- | --- | --- |
| Bloqueo de ejecución | Pendiente | Pendiente | Pendiente | Pendiente |
| Filtrado de dominios | Pendiente | Pendiente | Pendiente | Pendiente |
| Horarios sin interfaz | Pendiente | Pendiente | Pendiente | Pendiente |
| Persistencia tras reinicio | Pendiente | Pendiente | Pendiente | Pendiente |
| Recuperación autorizada | Pendiente | Pendiente | Pendiente | Pendiente |
| Ruta de distribución | Pendiente | Pendiente | Pendiente | Pendiente |

Actualizar cada celda con estado y enlace al informe. Estados: aprobada, limitada, descartada, pendiente. El informe debe describir las limitaciones y la versión probada.

## Fuentes oficiales de referencia

Verificar requisitos vigentes al implementar cada prototipo:

- [Flutter: plataformas e integración nativa](https://docs.flutter.dev/resources/faq).
- [Apple: Screen Time](https://developer.apple.com/documentation/screentimeapidocumentation/).
- [Apple: permiso Family Controls](https://developer.apple.com/documentation/familycontrols/requesting-the-family-controls-entitlement).
- [Android: VPN](https://developer.android.com/develop/connectivity/vpn).
- [Android: DevicePolicyManager](https://developer.android.com/reference/android/app/admin/DevicePolicyManager).
- [Google Play: VpnService](https://support.google.com/googleplay/android-developer/answer/12564964).
- [Microsoft: Windows Filtering Platform](https://learn.microsoft.com/en-us/windows/win32/fwp/windows-filtering-platform-start-page).
- [Microsoft: Application Control](https://learn.microsoft.com/windows/security/threat-protection/windows-defender-application-control/).
