# 0004. Roles de base de datos separados: migrator y runtime

- Estado: aceptada
- Fecha: 2026-11-14

## Contexto

En Postgres, el dueño de una tabla y los roles con `BYPASSRLS` o superusuario no quedan sujetos a RLS por defecto.
Si la app se conectara con alguno de esos roles, RLS no protegería nada.

## Decisión

- `app_migrator`: dueño de la base, del esquema `public` y de todas las tablas. Corre las migraciones de EF
  (`ConnectionStrings__Migrations`).
- `app_runtime`: es el usuario de la API (`ConnectionStrings__App`). Tiene `LOGIN`, `NOSUPERUSER`, `NOBYPASSRLS`, no es
  dueño de nada y no tiene `CREATE` en el esquema. Solo tiene `SELECT/INSERT/UPDATE/DELETE`, otorgado con
  `ALTER DEFAULT PRIVILEGES` en la migración inicial, así que las tablas nuevas le dan permisos solas.
- Los roles y la base los crea un superusuario una sola vez: `infra/postgres/init/01-roles.sh` en Docker, el fixture de
  Testcontainers en los tests y el aprovisionamiento de infraestructura en producción.
- Además se usa `FORCE ROW LEVEL SECURITY`, para que RLS aplique incluso al dueño.

## Consecuencias

- Los tests (`DatabaseBaselineTests`) verifican que `app_runtime` no puede saltear RLS, no es dueño de tablas y no puede crearlas.
- En tablas inmutables (caja, pagos) se le van a revocar `UPDATE/DELETE` a `app_runtime` (ver ADR 0009).
- Hacen falta dos connection strings por entorno.
