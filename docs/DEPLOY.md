# Deploy

Dos imágenes, construidas desde la raíz del repo:

| Imagen | Dockerfile | Qué hace |
|--------|------------|----------|
| API | `backend/Dockerfile` | ASP.NET Core en el puerto `8080`, usuario sin privilegios |
| Web | `frontend/Dockerfile` | Nginx con el build de Vite (PWA); reenvía `/api` y `/health` a la API (`API_UPSTREAM`) |

La web y la API comparten origen (la web hace de proxy), así que no hace falta CORS y la cookie de refresh funciona igual
que en desarrollo.

## Opción A: un VPS con Docker

```bash
cp .env.example .env.prod      # completar (ver variables abajo)
docker compose -f docker-compose.prod.yml --env-file .env.prod up -d --build
```

Delante, un proxy con HTTPS (Caddy es lo más simple) apuntando a `WEB_PORT`. Si ese proxy agrega `X-Forwarded-For`,
poné `ForwardedHeaders__ForwardLimit=2`.

## Opción B: plataforma administrada (Railway, Render…)

- Un Postgres administrado. Ejecutá **una vez** el contenido de `infra/postgres/init/01-roles.sh` (los `CREATE ROLE`,
  la base y los permisos) con el usuario administrador; sin los dos roles el RLS no protege nada.
- Servicio **api** con `backend/Dockerfile` y servicio **web** con `frontend/Dockerfile` (`API_UPSTREAM` = URL interna
  de la API). Solo la web necesita dominio público.
- `ForwardedHeaders__Enabled=true` y `ForwardedHeaders__ForwardLimit=2` (proxy de la plataforma + Nginx).

## Variables de la API

| Variable | Obligatoria | Notas |
|----------|-------------|-------|
| `ConnectionStrings__App` | sí | **siempre** con `app_runtime` |
| `ConnectionStrings__Migrations` | sí | con `app_migrator` |
| `Database__ApplyMigrationsOnStartup` | | `true` aplica migraciones al arrancar (una sola instancia de API) |
| `Jwt__SigningKey` | sí | `openssl rand -base64 48` |
| `AppointmentLinks__SigningKey` | sí en prod | otra clave distinta; firma los links de "gestionar mi turno" |
| `ForwardedHeaders__Enabled` | sí detrás de proxy | sin esto el rate limit ve a todos los clientes como una sola IP |
| `ForwardedHeaders__ForwardLimit` | | cuántos proxies hay delante (default 1) |
| `ForwardedHeaders__KnownProxies` / `__KnownNetworks` | | opcional: IPs/CIDRs del proxy, si la plataforma los publica |
| `RateLimiting__PublicPerMinute`, `__PublicBookingsPerHour`, `__SignupsPerHour` | | límites por IP |

## Operación

- Activar la suscripción de un negocio (sin pasarela todavía, ADR 0014):
  `docker compose -f docker-compose.prod.yml exec api dotnet Api.dll activate <slug> 30`
- Backups: `docker compose -f docker-compose.prod.yml exec postgres pg_dump -U postgres turnero > backup.sql`
