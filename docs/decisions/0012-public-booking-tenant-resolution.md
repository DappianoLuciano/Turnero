# 0012. Resolución del tenant en la web pública de reservas

- Estado: propuesta (preparada, sin implementar)
- Fecha: 2026-11-14

## Contexto

En la web pública no hay usuario autenticado, así que el tenant no puede salir de un claim. Tiene que salir de la URL:
`/r/{slug}` o, más adelante, `{slug}.dominio.com`.

## Decisión (propuesta)

- `businesses.slug` es único y válido como etiqueta DNS (`^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$`, máx. 63), así que sirve
  tanto para el path como para el subdominio.
- `ITenantResolver` abstrae la estrategia: `ClaimsTenantResolver` (activo, panel) y `SlugTenantResolver` (stub).
- Endpoints públicos en `/api/public/{slug}/…`, en un grupo propio que:
  1. resuelve `slug → business_id` con una política RLS acotada
     (`public_slug_lookup FOR SELECT USING (slug = current_setting('app.public_slug', true))`) o con una función
     `SECURITY DEFINER` que devuelva solo el id;
  2. setea el tenant y abre la transacción, igual que el grupo autenticado;
  3. expone **solo** datos públicos (servicios, profesionales, disponibilidad) mediante DTOs específicos.
- Hace falta rate limiting en los endpoints públicos.

## Consecuencias

- Al implementar `Booking` hay que elegir entre la política o la función `SECURITY DEFINER`, escribir los tests de
  aislamiento del grupo público y cerrar este ADR.
