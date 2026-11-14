# 0007. Fechas en UTC y zona horaria por negocio

- Estado: aceptada
- Fecha: 2026-11-14

## Contexto

Los turnos dependen de la hora local del negocio. Mezclar horas locales y UTC es una fuente clásica de errores,
sobre todo con cambios de horario de verano.

## Decisión

- Todo instante se guarda como `timestamptz` en UTC. En C# se usa `DateTimeOffset` con offset 0 (Npgsql lo exige).
- No se usa `DateTime` en las entidades (`ConventionsTests` lo verifica).
- Cada negocio tiene `time_zone` (IANA), con default `America/Argentina/Buenos_Aires`.
- **La conversión UTC ↔ hora local se hace solo en `Shared/Time/BusinessTimeZone`.** Las horas locales "de pared"
  (por ejemplo, "abre a las 9:00") se modelarán con `TimeOnly`/`DateOnly` en `Schedules`, y se convierten con ese helper.
- Se usa la BCL (`TimeZoneInfo` con IDs IANA, que funcionan en Windows y Linux con .NET 10) y `TimeProvider` para
  poder testear. NodaTime se reevalúa cuando empiece el módulo `Schedules`.

## Consecuencias

- Hay un solo lugar para auditar la lógica de zonas horarias.
- Las horas locales inexistentes, salteadas por un cambio de horario, se mueven hacia adelante, y las ambiguas
  (repetidas) toman la primera ocurrencia.
