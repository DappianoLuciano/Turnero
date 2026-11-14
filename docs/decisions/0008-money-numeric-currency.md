# 0008. Dinero en numeric(12,2) con columna de moneda

- Estado: aceptada
- Fecha: 2026-11-14

## Contexto

`float` y `double` no representan exactamente los decimales, y en cobros o caja un centavo de diferencia es un bug.

## Decisión

- Montos: `decimal` en C# → `numeric(12,2)` en Postgres, por convención global en `AppDbContext.ConfigureConventions`.
- Siempre con moneda: value object `Money(Amount, Currency)` → `amount numeric(12,2)` + `currency char(3)` (ISO 4217).
  El default es `ARS`, y cada negocio tiene `currency`.
- `Money` rechaza más de 2 decimales y operaciones entre monedas distintas.
- No se usan `float`/`double` en el dominio (`ConventionsTests`).

## Consecuencias

- El máximo es 9.999.999.999,99 por monto. Si hiciera falta más, se cambia la precisión con un ADR nuevo.
- El redondeo es explícito: cada caso de uso que divida montos (por ejemplo, el porcentaje de una seña) define cómo redondea.
