# 0009. Movimientos de caja y pagos inmutables

- Estado: aceptada (a implementar en `Payments` y `CashRegister`)
- Fecha: 2026-11-14

## Contexto

Caja y cobros tienen que ser auditables: si un monto se puede editar o borrar, no hay forma confiable de cerrar caja ni
de conciliar con Mercado Pago.

## Decisión

- Los registros de `payments` y de los movimientos de caja son **append-only**: nunca se modifican ni se borran.
- Un error se corrige con un **movimiento inverso** (`Money.Negate()`) que referencia al original
  (por ejemplo, `reverses_id`), con motivo, usuario y fecha.
- El estado (saldo de caja, pagado de un turno) se **calcula** a partir de los movimientos, o se materializa en tablas
  derivadas que se pueden reconstruir.
- La base lo refuerza: en esas tablas se revoca `UPDATE, DELETE` a `app_runtime`, y opcionalmente se agrega un trigger
  que lo impide.

## Consecuencias

- La auditoría es completa por diseño.
- Corregir requiere dos registros (el original y su anulación), y la UI tiene que mostrarlo así.
