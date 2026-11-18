# 0009. Movimientos de caja y pagos inmutables

- Estado: aceptada (implementada en `Payments` y `CashRegister`)
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

## Implementación en `payments`

- Un cobro tiene monto positivo; una anulación es otra fila con monto negativo y `reverses_id` apuntando al original
  (CHECK de signo y motivo obligatorio). Un cobro se anula una sola vez (índice único parcial sobre `reverses_id`) y una
  anulación no se anula.
- `app_runtime` tiene `UPDATE` y `DELETE` revocados sobre `payments` (la migración los quita tras el `GRANT` por defecto).
- Lo cobrado de un turno es la suma de sus movimientos; el saldo es `precio - cobrado`.
- La FK a `appointments` es `NO ACTION`: un turno con cobros no se puede borrar.

## Consecuencias

- La auditoría es completa por diseño.
- Corregir requiere dos registros (el original y su anulación), y la UI tiene que mostrarlo así.

## Implementación en `cash_movements` y `cash_closings`

- El efectivo esperado de un día **se calcula**, no se guarda dos veces: cobros en efectivo del libro de `payments`
  (por el contrato `IPaymentLedger`) más los movimientos manuales de `cash_movements` (ingresos y egresos con signo).
- Un movimiento manual se corrige con una anulación (fila de signo opuesto con `reverses_id` y motivo), una sola vez.
- `cash_closings` guarda, por negocio y día local (índice único), lo esperado, lo contado y la diferencia. Es inmutable:
  una vez cerrado el día de hoy no se aceptan más movimientos manuales ni anulaciones.
- Ambas tablas tienen `UPDATE` y `DELETE` revocados a `app_runtime`.
- Límite conocido: un cobro en efectivo registrado después del cierre del día no altera el cierre ya congelado.
