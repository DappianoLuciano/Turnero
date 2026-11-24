# 0015. Cambio y cancelación por el cliente, y recordatorios

- Estado: aceptada
- Fecha: 2026-11-24

## Contexto

El cliente reserva sin cuenta (ADR 0012), así que no tiene "su" lista de turnos. Hace falta que pueda cambiar o cancelar
el suyo, y que el negocio pueda recordárselo. No hay proveedor de mensajes (WhatsApp Business API, SMS, email) integrado,
y al reservar solo se piden nombre y teléfono.

## Decisión

- **El enlace es la credencial.** Cada turno tiene un token: `id` del turno (32 hex) + los primeros 16 bytes de
  `HMAC-SHA256(business_id:turno_id)` (32 hex), con una clave derivada de `Jwt:SigningKey` (separada por dominio). **No se
  guarda nada**: el servidor lo recalcula, así que el negocio puede armar el mismo enlace para el recordatorio. Como el
  `business_id` va dentro de lo firmado, un token de un negocio no abre nada en otro. Un token inválido es un 404
  `appointment_not_found`, igual que uno desconocido. Se comparan firmas en tiempo constante.
  Cambiar `Jwt:SigningKey` invalida todos los enlaces ya enviados.
- **Páginas y endpoints públicos** (`/r/{slug}/turno/{token}`; `/api/public/{slug}/appointments/{token}[/availability|/reschedule|/cancel]`):
  ver el turno, ver horarios libres, mover y cancelar. Mover conserva profesional, servicio, duración y precio. Solo se
  acepta un horario que la disponibilidad ofrece en ese momento, **ignorando al propio turno** (para poder correrlo
  15 minutos aunque se pise con su horario actual); si dos personas lo piden a la vez decide la exclusion constraint
  (`slot_unavailable`). Mover y cancelar tienen el rate limit de reservas.
- **Plazo configurable por negocio**: `businesses.customer_change_notice_hours` (por defecto 12, de 0 a 720; 0 = hasta que
  empieza). Dentro del plazo el cliente ve "ya no se puede cambiar por acá" y el teléfono del negocio
  (`businesses.phone`, opcional). El panel (dueño) lo edita en **Ajustes** (`PUT /api/businesses/current/settings`).
  El negocio, desde la agenda, puede cambiar un turno en cualquier momento, sin plazo.
- **Recordatorios por link de WhatsApp, a mano.** `GET /api/appointments/reminders?date=` lista los turnos programados
  de un día con teléfono y token; el frontend arma el `wa.me/<número>?text=<mensaje>` (mensaje con nombre, servicio,
  profesional, fecha y hora en la zona del negocio, y el enlace para cambiar o cancelar). Abrir WhatsApp marca el turno
  (`appointments.reminder_sent_at`, `POST .../reminder-sent`); mover el turno borra la marca, porque el horario nuevo
  necesita un recordatorio nuevo. El negocio solo aprieta "enviar" en WhatsApp.
- El **cliente** puede ser un teléfono sin código de país: los números sin `+` se asumen móviles argentinos (`549…`).

## Consecuencias

- No hay envío automático ni confirmación de entrega: depende de que el negocio recorra la lista (se ve cuántos faltan).
  El día que haya un proveedor, bastará un proceso que use la misma consulta y marque `reminder_sent_at`; el contrato
  `IAppointmentLinks` ya expone el token.
- Quien reciba el enlace puede cambiar o cancelar ese turno; si se reenvía, cualquiera que lo tenga puede hacerlo. Es el
  costo de no tener cuentas, y el plazo y la cancelación visibles para el negocio lo acotan.
- Los enlaces no vencen ni se pueden revocar por turno (un turno cancelado o pasado ya no admite cambios).
- La heurística de números locales puede fallar para teléfonos mal cargados; el panel muestra "Teléfono no válido".
- Pendiente: subdominio por negocio (`{slug}.dominio.com`); el enlace del recordatorio hoy usa el origen desde el que se
  abre el panel.
