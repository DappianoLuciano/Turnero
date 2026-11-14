# [NOMBRE]

SaaS de gestión para locales que trabajan con turnos (barberías, peluquerías, centros de uñas, estética):
panel del negocio y web pública de reservas, multi-negocio.

> Estado: **etapa 0 (bases)**. Hay estructura, multi-tenant con RLS, autenticación y documentación.
> Todavía no hay funcionalidad de negocio.

- Arquitectura: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- Decisiones (ADRs): [docs/decisions/](docs/decisions/)
- Guía para sesiones con Claude: [CLAUDE.md](CLAUDE.md)

## Requisitos

| Herramienta | Versión | Notas |
|-------------|---------|-------|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0.x | fijado en `backend/global.json` |
| [Node.js](https://nodejs.org) | 22 LTS | |
| [pnpm](https://pnpm.io) | 10.x | `corepack enable` o `npm i -g pnpm@10` |
| [Docker](https://www.docker.com/products/docker-desktop/) | Desktop o Engine | en Windows con WSL2 (`wsl --install`) |
| Git | | |

## Levantar todo desde cero

```bash
# 1. Clonar y configurar variables
git clone https://github.com/DappianoLuciano/Turnero.git
cd Turnero
cp .env.example .env          # y cambiá las contraseñas y Jwt__SigningKey

# 2. Base de datos (crea los roles app_migrator / app_runtime la primera vez)
docker compose up -d

# 3. Backend: herramientas, migraciones y datos de prueba
cd backend
dotnet tool restore
dotnet run --project src/Api -- seed      # aplica migraciones y crea negocio demo + dueño
dotnet run --project src/Api              # API en http://localhost:5080  (docs: /docs, health: /health)

# 4. Frontend (en otra terminal)
cd frontend
pnpm install
pnpm dev                                  # http://localhost:5173  → /app/login
```

Usuario de prueba: **owner@demo.local** / **Demo1234!**

> En `Development`, la API lee el `.env` de la raíz y aplica las migraciones al arrancar
> (`Database__ApplyMigrationsOnStartup=true`). Para aplicarlas a mano:
> `dotnet ef database update --project src/Api` (usa `ConnectionStrings__Migrations`, el rol `app_migrator`).

> Si cambiás las contraseñas del `.env` después de crear el volumen, recreá la base:
> `docker compose down -v && docker compose up -d`.

## Tests, lint y formato

```bash
# Backend
cd backend
dotnet test                                                   # todo, incluidos los de integración (requiere Docker)
dotnet test -- --filter-not-trait "Category=Integration"      # solo unitarios y de arquitectura
dotnet format --verify-no-changes

# Frontend
cd frontend
pnpm test
pnpm lint
pnpm typecheck
pnpm format:check
pnpm build
```

CI (GitHub Actions) corre todo esto en cada push y en cada PR: [.github/workflows/ci.yml](.github/workflows/ci.yml).

## Variables de entorno

Todas están documentadas en [.env.example](.env.example). No hay secretos en el repo: en producción se definen como
variables de entorno (`ConnectionStrings__App`, `ConnectionStrings__Migrations`, `Jwt__SigningKey`, …).

## Cambiar el nombre del producto

Editar [product.json](product.json): el nombre se toma de ahí en el título de la API, el manifest de la PWA y la UI.

## Notas para Windows

- Conviene clonar el repo **fuera de OneDrive** (por ejemplo, `C:\dev\turnero`): la sincronización de `node_modules`,
  `bin/` y `obj/` bloquea archivos y vuelve lento el build.
- Docker Desktop necesita WSL2: `wsl --install` como administrador y reiniciar.
