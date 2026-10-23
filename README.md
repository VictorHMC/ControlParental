# ControlParental

Control parental para familias con interfaz Flutter y componentes nativos para iPadOS, Android y Windows.

## Estado

Arquitectura y primer laboratorio Windows, 23 de octubre de 2026. Hay un filtro HTTPS local y una app inocua compilables con .NET, scripts AppLocker y pruebas de componentes. No hay aplicación Flutter ni servicio instalado. Las capacidades de bloqueo del dispositivo siguen pendientes de validación real.

## Documentación

- [Arquitectura](docs/arquitectura.md): componentes, responsabilidades y contratos propuestos.
- [Plan por fases](docs/plan-fases.md): entregables, dependencias y criterios de salida.
- [Pruebas técnicas](docs/pruebas-tecnicas.md): protocolos por plataforma y matriz común.
- [Decisión de tecnología](docs/decisiones/0001-flutter-y-componentes-nativos.md).
- [Plantilla de resultados](spikes/resultados/PLANTILLA.md).
- [Laboratorio Windows](spikes/windows/README.md) e [informe inicial](spikes/resultados/WINDOWS-01-inicial.md).
- [Agente persistente Windows](spikes/windows/AGENT.md) e [informe de componentes](spikes/resultados/WINDOWS-02-agente.md).
- [Sesión compartida y contraseña de desbloqueo](docs/decisiones/0002-sesion-compartida-y-contrasena.md).

## Estructura prevista

```text
apps/                  Aplicaciones Flutter para padres e hijos
packages/              Reglas, contratos y adaptadores compartidos
services/              API y sincronización; tecnología por decidir
spikes/                Pruebas aisladas y evidencias anonimizadas
docs/                  Arquitectura, decisiones y plan
```

`apps`, `packages` y `services` contienen por ahora una descripción de su propósito. `spikes/windows` contiene los primeros proyectos nativos de laboratorio. Se generarán las aplicaciones Flutter después de validar las capacidades críticas y fijar versiones.

## Primer objetivo

Ejecutar la fase 0: demostrar bloqueo real, persistencia y recuperación en cada plataforma. Empezar por el riesgo de distribución de Apple y distinguir Android convencional de Android administrado. La instalación en equipos de prueba será explícita y reversible.

No subir credenciales, certificados, perfiles de firma ni información de menores. Las pruebas usarán aplicaciones y dominios de prueba inocuos.
