# Plan por fases

Estado inicial: todas las fases pendientes. Las estimaciones se fijarán después de la fase 0; no se comprometen fechas sin equipos, permisos y mediciones.

| Fase | Entregables | Criterio de salida |
| --- | --- | --- |
| 0. Viabilidad nativa | Tres prototipos aislados, informes reproducibles y matriz de capacidades | Bloqueo y recuperación demostrados por plataforma; limitaciones y distribución documentadas |
| 1. Contratos y base Flutter | Proyectos oficiales, versiones fijadas, contratos, reglas y decisiones de servidor/proveedor | Compilación por destino y pruebas de reglas, horarios e idempotencia |
| 2. Recorrido familiar mínimo | Vinculación, una política remota, persistencia local y confirmación | Padre configura, hijo aplica y panel confirma; aislamiento entre familias probado |
| 3. Funciones del MVP | Categorías, excepciones, horarios, bloqueo viable y solicitudes | Matriz funcional aprobada en dispositivos reales, incluso sin conexión |
| 4. Piloto y publicación | Instaladores firmados, recuperación, privacidad y documentación | Permisos de distribución, pruebas de actualización y recuperación, piloto aprobado |

## Fase 0: primero los riesgos

1. Inventariar equipos, versiones y ediciones. Disponer de iPad físico, Mac con Xcode y cuenta Apple apropiada; Android físico y un equipo reiniciable para pruebas administradas; Windows de prueba con cuentas de responsable e hijo estándar.
2. Abrir el trámite de Family Controls para distribución y registrar estado sin publicar identificadores privados. El éxito con firma de desarrollo no prueba viabilidad de publicación.
3. Ejecutar [los protocolos](pruebas-tecnicas.md) de iPad, Android y Windows. Se pueden trabajar de forma independiente cuando haya equipos.
4. Consolidar evidencias con la [plantilla](../spikes/resultados/PLANTILLA.md), separar observado de supuesto y registrar versión exacta de cada entorno.
5. Emitir una decisión por capacidad: aprobada, limitada, descartada o pendiente. Una capacidad pendiente no permite anunciarla como soportada.

### Puerta de decisión

Para aprobar una capacidad: tres ejecuciones consecutivas del recorrido bloquear/verificar/desbloquear, persistencia conforme al protocolo, recuperación comprobada y ninguna pérdida de acceso administrativo. Adjuntar evidencia anonimizada y pasos reproducibles.

Si Android convencional no permite bloqueo fiable de apps, elegir explícitamente entre ofrecer solo filtrado/horarios viables o exigir instalación administrada. No anunciar ambas como equivalentes. Si Apple no habilita distribución, mantener esa plataforma experimental. Si Windows no permite políticas por usuario con el alcance elegido, revisar edición soportada o tecnología antes del MVP.

No es necesario detener todo el proyecto por una capacidad descartada: reducir el alcance explícitamente y actualizar la matriz. Para pasar a fase 1 debe existir al menos un recorrido viable por cada plataforma incluida en el MVP, con el bloqueo de aplicaciones y web marcado por separado.

## Fase 1: diseño ejecutable

- Generar aplicaciones Flutter y plugins oficiales; fijar versiones según compatibilidad comprobada.
- Definir lista de plataformas/versiones/ediciones soportadas.
- Fijar semántica de reglas, zonas horarias, conflictos y recuperación.
- Elegir servidor, identidad y proveedor de categorías con una decisión registrada. Comparar cobertura, actualización, privacidad, licencia, disponibilidad y coste medido; no contratar por anticipado.
- Añadir integración continua: reglas y contratos en cada cambio; compilaciones Android/Windows y Apple en ejecutor macOS con firma protegida cuando corresponda.

## Fases 2 y 3: producto

Empezar por una familia y dos equipos de prueba, con autorización real en servidor. Verificar repetición de mensajes, políticas antiguas, equipo desconectado y permisos revocados. Una notificación enviada no cuenta como restricción aplicada.

Añadir categorías y excepciones una vez aprobado el recorrido completo. Verificar falsos positivos con dominios inocuos; permitir corrección por el responsable. Medir batería, memoria, latencia y recuperación de red frente a una línea base. Fijar presupuestos a partir de esas mediciones antes del piloto.

## Fase 4: preparación operativa

Probar actualización, migración de políticas, desinstalación autorizada y recuperación de cuenta. Revisar políticas vigentes de tiendas, requisitos de privacidad de menores y mercados de publicación. Documentar límites observados y soporte. El piloto usará consentimiento explícito y datos mínimos; no incorporar datos personales a evidencias públicas.

## Trabajo inmediato

- [ ] Asignar responsable y equipo a cada protocolo.
- [ ] Registrar acceso a Mac/Xcode, iPad y cuenta Apple.
- [ ] Registrar Android disponible y si puede restablecerse para administración.
- [ ] Registrar versión/edición Windows y mecanismo de recuperación.
- [ ] Crear prototipos nativos mínimos y ejecutar casos.
- [ ] Completar matriz y aprobar alcance del MVP.
