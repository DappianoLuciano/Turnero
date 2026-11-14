# Arquitectura

Visión general del sistema. Las decisiones puntuales y sus motivos están en [decisions/](decisions/).

## Vista general

```
Navegador / PWA (React)                         API (ASP.NET Core 10)                 PostgreSQL 17
┌──────────────────────────┐  /api (same-origin) ┌───────────────────────────────┐    ┌──────────────────┐
│ /app/*   panel (login)   │ ──────────────────▶ │ Auth JWT → TenantResolution   │    │ RLS por          │
│ /r/:slug reserva pública │   Bearer + cookie   │ → Endpoint filters:           │──▶ │ business_id      │
└──────────────────────────┘   httpOnly (rt)     │   validación, transacción     │    │ (rol app_runtime)│
                                                 │ → Módulos (vertical slices)   │    └──────────────────┘
                                                 └───────────────────────────────┘
```

- **Una sola API**, organizada como **monolito modular** (ADR 0002).
- **Un solo frontend** con dos zonas de rutas: panel (`/app/*`) y reserva pública (`/r/:slug/*`) (ADR 0011).
- **Multi-tenant** con esquema compartido: cada fila de negocio lleva `business_id` (ADR 0003).

## Backend

```
backend/src/Api/
├── Program.cs              # entrada: carga .env (dev), arma la app, `-- seed`
├── Hosting/                # composition root: OpenAPI, health, grupos de rutas, seed
├── Modules/                # Businesses, Users, ... (ver Modules/README.md)
├── Shared/                 # building blocks, sin dependencias hacia módulos
│   ├── Auth/               # claims, roles, políticas, emisión de tokens
│   ├── Errors/             # AppException → Problem Details
│   ├── Hosting/            # .env, product.json, grupos de rutas
│   ├── Monetary/           # Money (numeric(12,2) + moneda)
│   ├── Persistence/        # AppDbContext, marcadores de entidad, helper RLS, migrador
│   ├── Tenancy/            # TenantContext, interceptores, filtro de transacción
│   ├── Time/               # BusinessTimeZone: única conversión UTC ↔ hora local
│   └── Validation/         # filtro de validación automática (FluentValidation)
└── Migrations/             # migraciones EF (corren con app_migrator)
```

### Ciclo de una request autenticada

1. `UseAuthentication` valida el JWT (HS256, 15 min).
2. `TenantResolutionMiddleware` copia los claims `business_id` y `sub` al `TenantContext` (scoped).
3. `UseAuthorization`: el grupo de tenant exige la política `member`.
4. `ValidationEndpointFilter` valida cualquier argumento que tenga un `IValidator<T>` registrado → 400 `ValidationProblemDetails`.
5. `TenantTransactionFilter` abre **una transacción por request**. Al abrirse, `TenantTransactionInterceptor`
   ejecuta `set_config('app.business_id', …, true)` y `set_config('app.user_id', …, true)`.
6. El handler consulta con EF: el **filtro global** agrega `WHERE business_id = @tenant` y **RLS** vuelve a filtrar en la base.
7. Commit si el resultado no es error; rollback en cualquier otro caso.

### Aislamiento multi-tenant: dos capas

| Capa | Qué hace | Dónde |
|------|----------|-------|
| EF Core | Filtro global por `BusinessId` en toda entidad `ITenantEntity` (y `Id` en `ITenantRoot`). `TenantGuardInterceptor` completa `business_id` en altas y rechaza escrituras sobre otro tenant | `AppDbContext`, `TenantGuardInterceptor` |
| PostgreSQL | `ENABLE` + `FORCE ROW LEVEL SECURITY` y política `tenant_isolation` (`USING` + `WITH CHECK`) contra `current_setting('app.business_id')` | migraciones (`EnableTenantRls`) |

La app se conecta como **`app_runtime`**: no es dueño de las tablas, no tiene `BYPASSRLS` y no puede crear objetos.
Las migraciones corren como **`app_migrator`** (dueño). Ver ADR 0004.

Si falta el tenant, o se consulta fuera de una transacción, **no se ve ninguna fila** (fail closed).

### Tablas actuales

| Tabla | Tipo | RLS |
|-------|------|-----|
| `businesses` | raíz del tenant | `id = app.business_id` |
| `business_memberships` | tenant | `tenant_isolation` + `member_self_read` (solo `SELECT` de las propias, para el login) |
| `users` | global (ADR 0005) | no |
| `refresh_tokens` | global (ADR 0006) | no |

### Autenticación

- `POST /api/auth/login`: devuelve el access token en el body y el refresh token en la cookie `rt`
  (`HttpOnly`, `Secure`, `SameSite=Strict`, `Path=/api/auth`).
- `POST /api/auth/refresh`: rota el refresh token. Si se reusa uno ya rotado, se revoca toda la familia.
- `POST /api/auth/logout`, `GET /api/me`, `GET /api/businesses/current`.

### Convenciones de datos

- Fechas: `DateTimeOffset` en UTC → `timestamptz`. La hora local solo existe en los bordes, vía `BusinessTimeZone` (ADR 0007).
- Dinero: `decimal` → `numeric(12,2)` + columna de moneda `char(3)`, default `ARS` (ADR 0008).
- IDs: UUID v7 (`Guid.CreateVersion7()`), generados en la app.
- Nombres: tablas en plural y `snake_case` (`EFCore.NamingConventions`).
- Movimientos de caja y pagos: **inmutables** (ADR 0009).

### Errores y API

- Todos los errores salen como Problem Details (RFC 9457), con `code` (estable, para el frontend) y `traceId`.
- OpenAPI en `/openapi/v1.json` y UI Scalar en `/docs` (solo en Development).
  El documento también se genera en cada build en `backend/openapi/openapi.json`.
- `GET /health` verifica la conexión a la base.

## Frontend

```
frontend/src/
├── app/                # router (zonas de rutas), query client, 404
├── features/
│   ├── auth/           # sesión en memoria, login, guard, restore por cookie
│   ├── panel/          # layout del panel, navegación, páginas placeholder
│   └── booking/        # web pública de reservas (placeholder)
├── shared/
│   ├── api/            # cliente openapi-fetch tipado + schema.d.ts generado
│   ├── i18n/           # textos es-AR (único lugar con texto de UI)
│   ├── config/         # product.json
│   ├── ui/             # componentes shadcn/ui
│   └── lib/
└── test/               # setup de Vitest + MSW
```

- **Cliente HTTP** (`shared/api/client.ts`): agrega el bearer token; ante un 401 hace **un** refresh compartido
  (single-flight) y reintenta una vez.
- **Tipos** generados desde OpenAPI: `pnpm gen:api`. En CI se verifica que estén al día.
- **PWA**: `vite-plugin-pwa` (Workbox). Precachea solo el app shell y nunca cachea `/api`.
- **Mobile first**: drawer en celular, sidebar fija desde `md`.

## Testing

| Qué | Cómo |
|-----|------|
| Aislamiento multi-tenant | `tests/Api.Tests/Tenancy`: EF y SQL directo contra Postgres real (Testcontainers), más meta-tests de cobertura de RLS |
| Auth / API | `WebApplicationFactory` + Postgres real |
| Arquitectura | NetArchTest: límites entre módulos; convenciones (validadores, sin `float`/`DateTime`) |
| Frontend | Vitest + Testing Library + MSW |
