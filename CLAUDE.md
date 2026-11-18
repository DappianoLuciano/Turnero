# CLAUDE.md

SaaS multi-negocio de gestión de turnos (barberías, peluquerías, uñas, estética; después tiendas y gastronomía):
panel del negocio (`/app`) + web pública de reservas (`/r/:slug`). El nombre del producto es `Turnero`
y vive **solo** en `/product.json`.

## Stack

- **Backend**: .NET 10 (LTS), ASP.NET Core Minimal APIs, EF Core 10 + Npgsql, FluentValidation, JWT.
- **DB**: PostgreSQL 17 con Row-Level Security. Migraciones EF.
- **Frontend**: Vite 8, React 19, TypeScript 5.9, React Router 8, TanStack Query 5, Tailwind 4, shadcn/ui, PWA.
- **Tests**: xUnit v3 (Microsoft Testing Platform) + Testcontainers (Postgres real) · Vitest + Testing Library + MSW.
- pnpm · Docker Compose · GitHub Actions.

## Estructura

```
backend/src/Api/Modules/<Módulo>/{Contracts,Domain,Persistence,Features}   # vertical slices
backend/src/Api/Shared/        # tenancy, auth, errores, validación, tiempo, dinero (no depende de módulos)
backend/src/Api/Hosting/       # composition root, seed
backend/tests/Api.Tests/       # Tenancy/, Architecture/, Modules/, Shared/
backend/openapi/openapi.json   # generado en cada build (commitear)
frontend/src/{app,features,shared}   # shared/api (cliente generado), shared/i18n (textos), shared/ui (shadcn)
docs/ARCHITECTURE.md · docs/decisions/ (ADRs)
```

## Comandos

```bash
docker compose up -d                                   # Postgres (requiere .env, copiar de .env.example)
cd backend && dotnet run --project src/Api             # API en :5080 (aplica migraciones en dev)
cd backend && dotnet run --project src/Api -- seed     # owner@demo.local / Demo1234!
cd backend && dotnet test                              # todo (necesita Docker)
cd backend && dotnet test -- --filter-not-trait "Category=Integration"   # sin Docker
cd backend && dotnet format                            # formatear
cd backend && dotnet ef migrations add <Nombre> --project src/Api --output-dir Migrations
cd frontend && pnpm dev | pnpm test | pnpm lint | pnpm typecheck | pnpm format | pnpm build
cd frontend && pnpm gen:api                            # regenerar tipos después de cambiar la API
```

## Reglas de arquitectura (obligatorias)

1. **Módulos**: un módulo usa a otro **solo** por su namespace `Contracts`. Nunca accede a tablas ni entidades ajenas.
   FK entre módulos: con SQL en la migración, sin navegaciones EF. `Shared` no referencia módulos.
2. **Multi-tenant**: **toda tabla nueva de negocio debe llevar `business_id`, política RLS y un test de aislamiento.**
   - Entidad: implementar `ITenantEntity` (o `IGlobalEntity` con ADR que lo justifique).
   - Migración: `migrationBuilder.EnableTenantRls("tabla")`.
   - Test: agregar la tabla a `EfTenantIsolationTests` / `SqlTenantIsolationTests` y a `CoveredTenantTables`
     (`RlsCoverageTests` falla si falta).
   - Endpoints de negocio: mapear en `groups.Tenant`, que ya trae transacción y tenant. No abrir transacciones a mano;
     para flujos sin request autenticada, usar `TenantScope`.
   - La app conecta como `app_runtime` (sin BYPASSRLS). Las migraciones, como `app_migrator`.
   - `IgnoreQueryFilters()` solo con RLS cubriendo el caso, y comentado.
3. **Fechas**: `DateTimeOffset` UTC → `timestamptz`. Nunca `DateTime` en entidades. Conversión a hora local
   **solo** con `Shared/Time/BusinessTimeZone` (zona por negocio, default `America/Argentina/Buenos_Aires`).
4. **Dinero**: `decimal`/`Money` → `numeric(12,2)` + moneda `char(3)` (default `ARS`). Nunca `float`/`double`.
5. **Inmutabilidad**: pagos y movimientos de caja son append-only. Se anulan con un movimiento inverso, nunca con
   UPDATE/DELETE (ADR 0009).
6. **API**: errores con Problem Details y `code` estable (`AppException`). Todo `*Request` tiene `AbstractValidator`
   en su slice. IDs con `Guid.CreateVersion7()`. Tablas en plural y `snake_case`.

## Idioma

- Código, clases, tablas, endpoints, commits (Conventional Commits): **inglés**.
- Textos de UI: **español rioplatense**, solo en `frontend/src/shared/i18n/es-AR.ts` (ESLint bloquea texto literal en JSX).
- Documentación del repo: **español**.

## Antes de terminar una tarea

`dotnet build` + `dotnet format --verify-no-changes` + `dotnet test` y `pnpm lint && pnpm typecheck && pnpm test && pnpm build`.
Si cambió la API: commitear `backend/openapi/openapi.json` y correr `pnpm gen:api`.
Si la decisión es estructural: escribir un ADR en `docs/decisions/`.
