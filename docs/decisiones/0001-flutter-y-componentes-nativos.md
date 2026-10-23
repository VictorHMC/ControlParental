# ADR 0001: Flutter y componentes nativos

Fecha: 2026-10-23. Estado: aceptada para dirección tecnológica; viabilidad de capacidades pendiente de fase 0.

## Contexto

Se requiere administrar una familia desde iPad, Android y Windows y aplicar restricciones en equipos de los hijos. Los permisos de control pertenecen al sistema operativo y varían según autorización, edición y modo de instalación.

## Decisión

Compartir interfaz y lógica de dominio con Flutter/Dart. Usar Swift y extensiones oficiales para iPadOS, Kotlin para Android y un servicio Windows con integración nativa cuando corresponda. Separar interfaz, contratos y ejecución local. Validar primero prototipos nativos mínimos y luego integrarlos mediante plugins propios.

## Alternativas

- Todo nativo: integración directa, pero duplica interfaz y reglas de producto.
- React Native: también requiere módulos nativos y validación adicional de escritorio; no ofrece ventaja suficiente para este alcance.
- Web: adecuada para administración, insuficiente para imponer restricciones del sistema.
- Router/DNS exclusivamente: útil como complemento, no controla ejecución ni garantiza cobertura fuera del hogar.

## Consecuencias

Se necesita experiencia nativa y un Mac para el desarrollo Apple. No se promete el mismo bloqueo en todas las plataformas. Evitar depender de un plugin comunitario crítico sin revisar mantenimiento, licencia y comportamiento. La selección de versiones, servidor, proveedor de categorías y mecanismos definitivos Windows/Android queda abierta hasta los resultados técnicos.
