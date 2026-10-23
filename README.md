# ControlParental

Control parental para familias con interfaz Flutter y componentes nativos para iPadOS, Android y Windows.

## Estado

Arquitectura y plan inicial, 23 de octubre de 2026. No hay aplicación ejecutable ni bloqueos implementados. Todas las capacidades están pendientes de validación en dispositivos reales.

## Documentación

- [Arquitectura](docs/arquitectura.md): componentes, responsabilidades y contratos propuestos.
- [Plan por fases](docs/plan-fases.md): entregables, dependencias y criterios de salida.
- [Pruebas técnicas](docs/pruebas-tecnicas.md): protocolos por plataforma y matriz común.
- [Decisión de tecnología](docs/decisiones/0001-flutter-y-componentes-nativos.md).
- [Plantilla de resultados](spikes/resultados/PLANTILLA.md).

## Estructura prevista

```text
apps/                  Aplicaciones Flutter para padres e hijos
packages/              Reglas, contratos y adaptadores compartidos
services/              API y sincronización; tecnología por decidir
spikes/                Pruebas aisladas y evidencias anonimizadas
docs/                  Arquitectura, decisiones y plan
```

Los directorios de implementación contienen únicamente una descripción de su propósito. Se generarán los proyectos con las herramientas oficiales después de validar las capacidades críticas y fijar versiones.

## Primer objetivo

Ejecutar la fase 0: demostrar bloqueo real, persistencia y recuperación en cada plataforma. Empezar por el riesgo de distribución de Apple y distinguir Android convencional de Android administrado. La instalación en equipos de prueba será explícita y reversible.

No subir credenciales, certificados, perfiles de firma ni información de menores. Las pruebas usarán aplicaciones y dominios de prueba inocuos.
