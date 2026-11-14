# 0002. Monolito modular con vertical slices

- Estado: aceptada
- Fecha: 2026-11-14

## Contexto

El producto va a tener varios dominios (turnos, cobros, caja, reservas, clientes) y un equipo chico. Los microservicios
agregan costo operativo que hoy no se justifica, y un monolito sin límites termina acoplado.

## Decisión

- Un solo proyecto `Api` organizado en `Modules/<Nombre>/` (`Contracts`, `Domain`, `Persistence`, `Features`).
- Cada caso de uso es un **slice**: un archivo en `Features/` con endpoint, request, validator y handler.
- Un módulo usa a otro **solo a través de su namespace `Contracts`** (interfaces + DTOs). Nunca consulta las tablas ni
  usa las entidades de otro módulo.
- Lo transversal va en `Shared/` (tenancy, auth, errores, validación, tiempo, dinero). `Shared` no depende de ningún módulo.
- Hay **un solo `AppDbContext`, sin DbSets**: cada módulo mapea sus entidades con `IEntityTypeConfiguration` y accede
  con `Set<T>()`. Las FK entre módulos se crean en la migración con SQL, sin navegaciones de EF.
- Minimal APIs agrupadas por módulo (no controllers).

## Consecuencias

- Los tests de NetArchTest (`ModuleBoundariesTests`) fallan si un módulo referencia internals de otro o si `Shared`
  depende de un módulo.
- Un único DbContext simplifica las transacciones y las migraciones. A cambio, el límite entre tablas lo hacen cumplir
  los tests y no el compilador.
- Si un módulo crece mucho, se puede extraer, porque su superficie pública ya está definida en `Contracts`.
