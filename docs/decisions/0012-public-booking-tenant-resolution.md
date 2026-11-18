# 0012. Resolución del tenant en la web pública de reservas

- Estado: aceptada (implementada con la política RLS acotada)
- Fecha: 2026-11-14 (implementada 2026-11-18)

## Contexto

En la web pública no hay usuario autenticado, así que el tenant no puede salir de un claim. Tiene que salir de la URL:
`/r/{slug}` o, más adelante, `{slug}.dominio.com`.

## Decisión

- `businesses.slug` es único y válido como etiqueta DNS (`^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$`, máx. 63), así que sirve
  tanto para el path como para el subdominio.
- Los endpoints públicos viven en `/api/public/{slug}/…`, en un grupo propio (`ApiRouteGroups.Public`) con
  `PublicTenantFilter`, que por request:
  1. descarta slugs con forma inválida (404 sin tocar la base);
  2. en una transacción corta fija `app.public_slug` y lee el `id` del negocio. Lo permite una **segunda política RLS
     permisiva y solo `SELECT`** sobre `businesses` (`public_slug_lookup`: `slug = current_setting('app.public_slug')`),
     que deja ver exactamente una fila y nunca escribir. Se eligió la política y no una función `SECURITY DEFINER`
     porque las tablas tienen `FORCE ROW LEVEL SECURITY`: una función del dueño tampoco vería nada;
  3. fija el tenant y abre la transacción de la request, igual que el grupo autenticado: todo lo que sigue corre con los
     filtros de EF y el RLS de ese único negocio.
- Un slug desconocido es un 404 `business_not_found`, igual que cualquier otro negocio inexistente.
- El módulo `Booking` no tiene tablas: compone los demás módulos por sus contratos (`IServiceCatalog`,
  `IProfessionalDirectory`, `IAvailabilityService`, `ICustomerRegistry`, `IAppointmentCreator`).
- **Solo datos públicos**: el catálogo trae servicios y profesionales activos que se pueden reservar; la disponibilidad,
  solo horarios libres (no motivos de cierre ni datos de otros clientes); la confirmación, solo lo del turno propio.
- **Reserva sin cuenta**: el cliente se identifica por teléfono. Si ya existe un cliente activo con ese teléfono se
  reutiliza (conserva el nombre que el negocio tiene), si no se crea. Solo se puede reservar un horario que la
  disponibilidad ofrezca en ese momento (hasta 90 días hacia adelante); si dos personas reservan a la vez, la exclusion
  constraint de `appointments` decide y el segundo recibe 409 `slot_unavailable`.
- **Rate limiting** por IP en todo el grupo público (300 por minuto) y más estricto en la reserva (60 por hora),
  configurable con `RateLimiting:PublicPerMinute` y `RateLimiting:PublicBookingsPerHour`.
- En el celular del cliente se guardan nombre y teléfono en `localStorage` (clave `turnero.booking.customer`) para
  rellenar la próxima reserva. No es una cuenta ni se envía nada hasta que reserva; se puede borrar desde la pantalla.

## Consecuencias

- Falta el resolver por subdominio (`{slug}.dominio.com`): el filtro ya lee el slug de la ruta, bastaría con otra fuente.
- No hay forma de que el cliente cancele o reprograme solo; sin seña ni recordatorios (ver ADR de pagos para la seña).
- Quien conoce un teléfono puede reservar a su nombre. Es el costo de no tener cuentas; el negocio ve y puede cancelar
  turnos desde la agenda.
