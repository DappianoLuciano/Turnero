# 0003. Multi-tenant con esquema compartido, filtro de EF y Row-Level Security

- Estado: aceptada
- Fecha: 2026-11-14

## Contexto

Cada negocio tiene que ver solo sus datos. Una fuga entre negocios es el peor incidente posible para el producto.
Las opciones eran base por tenant, esquema por tenant o esquema compartido.

## Decisión

- **Esquema compartido**: toda tabla de negocio lleva `business_id uuid not null`.
- **Capa 1 (EF Core)**: las entidades `ITenantEntity` tienen un filtro global `BusinessId == CurrentBusinessId`.
  `TenantGuardInterceptor` completa `business_id` en altas y rechaza altas, modificaciones o bajas de otro tenant.
- **Capa 2 (PostgreSQL)**: `ENABLE` + `FORCE ROW LEVEL SECURITY` y la política `tenant_isolation`:
  `USING / WITH CHECK (business_id = NULLIF(current_setting('app.business_id', true), '')::uuid)`.
  `NULLIF` hace que un setting vacío o reseteado no matchee nada.
- El tenant se pasa a Postgres con `set_config('app.business_id', …, true)`, **local a la transacción**, cada vez que
  EF abre una transacción (`TenantTransactionInterceptor`). Los endpoints de tenant corren dentro de una transacción
  por request (`TenantTransactionFilter`).
- `IgnoreQueryFilters()` solo se usa cuando RLS sigue protegiendo el caso, y queda comentado en el código (por ejemplo,
  las membresías en el login).
- **Toda tabla nueva de negocio debe tener `business_id`, la política RLS (`EnableTenantRls`) y un test de aislamiento.**

## Consecuencias

- Aunque un bug saltee el filtro de EF, RLS sigue aislando. Los tests lo prueban explícitamente.
- Como el setting es local a la transacción, nunca se filtra entre requests por el pool de conexiones.
- Fuera de una transacción no hay tenant, y RLS no devuelve filas (fail closed).
- `RlsCoverageTests` falla si una tabla con `business_id` no tiene RLS forzado y política, o si no figura en los tests de aislamiento.
- La app es la que decide qué tenant setear, así que RLS protege contra bugs de consulta, no contra un atacante con
  acceso al rol `app_runtime`. Por eso el tenant sale **solo** de claims firmados.
