# 0001. Registrar las decisiones de arquitectura con ADRs

- Estado: aceptada
- Fecha: 2026-11-14

## Contexto

Somos dos desarrolladores y trabajamos en sesiones sucesivas, también con asistentes de IA. Hace falta que las decisiones
importantes y sus motivos queden escritos, cerca del código.

## Decisión

Cada decisión de arquitectura va en un archivo `docs/decisions/NNNN-titulo-en-kebab.md`, con este formato:
Estado, Fecha, Contexto, Decisión y Consecuencias. Una decisión por archivo. Si una decisión cambia, no se edita la
anterior: se escribe un ADR nuevo y el viejo pasa a estado "reemplazada por NNNN".

## Consecuencias

- La historia de por qué el sistema es como es queda versionada junto al código.
- Antes de cambiar algo estructural hay que leer el ADR correspondiente y, si corresponde, escribir uno nuevo.
