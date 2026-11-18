# 0013. Horarios de atención y disponibilidad

- Estado: aceptada
- Fecha: 2026-11-18

## Contexto

La agenda necesita saber cuándo atiende cada profesional, y la web pública de reservas (`/r/:slug`) va a necesitar
ofrecer solo horarios libres. Hay que decidir cómo se guardan los horarios, qué hacen con los turnos ya existentes y
dónde se calcula la disponibilidad.

## Decisión

- **Horario semanal por profesional** (`working_hours`): tramos con día de la semana y minutos desde la medianoche
  **local** del negocio (no instantes UTC). Un día puede tener varios tramos (corte de mediodía). La base impide tramos
  superpuestos del mismo profesional y día con una exclusion constraint (`btree_gist`, rangos semiabiertos).
- **Días cerrados** (`schedule_exceptions`): días completos, para todo el negocio (`professional_id` nulo, por ejemplo un
  feriado) o para un profesional. Una clave única con `COALESCE` evita cerrar dos veces el mismo día. No hay horarios
  parciales por excepción: por ahora se cierra o no se cierra el día.
- **Los horarios no se imponen a los turnos.** La agenda sombrea lo que queda fuera de horario y avisa al agendar, pero
  deja agendar: el negocio puede atender un cliente fuera de hora. El servidor no rechaza turnos por horario. Un profesional
  sin horario cargado se comporta como hasta ahora (sin sombreado ni avisos).
- **Disponibilidad calculada en el servidor** (`GET /api/availability`): tramos del día, menos días cerrados, menos turnos
  programados o completados (contrato `IAppointmentOccupancy` de `Appointments`), menos horas pasadas. Los inicios se
  ofrecen cada 15 minutos mientras el servicio entre en el tramo. Es la base de la reserva pública.
- Convertir minutos locales a UTC se hace solo con `BusinessTimeZone`, así el cambio de horario de verano no rompe nada.
- Cambiar el horario o cerrar un día **no toca los turnos ya agendados**: quedan y se ven en la agenda.

## Consecuencias

- Reservar por la web pública ya tiene de dónde sacar los horarios libres.
- Falta manejar excepciones parciales (por ejemplo, abrir solo medio día en un feriado) y avisar los turnos afectados al
  cerrar un día.
