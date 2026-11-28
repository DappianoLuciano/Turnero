# 0016. Endurecimiento para producción

- Estado: aceptada
- Fecha: 2026-11-28

## Contexto

Revisión antes del primer piloto con locales reales. Aparecieron cinco problemas que no se ven en desarrollo.

## Decisión

1. **IP real detrás de proxies.** `ForwardedHeaders` (configurable, apagado por defecto) procesa `X-Forwarded-For` antes
   del rate limiting. Sin esto, en cualquier plataforma todos los clientes comparten la IP del proxy y los límites por IP
   pasan a ser un límite global para todos los negocios. Se confía en `ForwardLimit` saltos desde la derecha (lo que
   escribieron los proxies propios), nunca en lo que manda el cliente.
2. **La reserva pública no revela datos guardados.** Antes, reservar con un teléfono existente devolvía el nombre que
   el negocio tenía para ese teléfono: probando números se obtenían nombres de clientes. La respuesta ahora devuelve el
   nombre que se escribió; el negocio sigue viendo el suyo en el panel.
3. **Tope de turnos futuros por teléfono** (`BookingRules.MaxUpcomingPerCustomer` = 3) en la reserva pública, para que
   una sola persona no pueda llenar una agenda. El panel no tiene tope. La defensa de fondo será la seña con Mercado Pago.
4. **Cobros serializados por turno.** `IAppointmentLookup.LockAsync` toma `SELECT … FOR UPDATE` sobre el turno dentro de
   la transacción de la request antes de calcular el saldo, así dos cobros simultáneos no superan el precio. Se expone
   en el contrato de `Appointments` para no romper la regla de módulos.
5. **Clave propia para los links del cliente** (`AppointmentLinks:SigningKey`). Antes derivaba de la clave del JWT: rotar
   esa invalidaba todos los links ya enviados. Los links firmados con la clave vieja siguen validando mientras no se
   cambie la del JWT.

Además: `backend/Dockerfile`, `frontend/Dockerfile` (Nginx sirve la PWA y hace de proxy a la API en el mismo origen),
`docker-compose.prod.yml` y `docs/DEPLOY.md`.

## Consecuencias

- En producción hay que definir `ForwardedHeaders__Enabled`, `ForwardedHeaders__ForwardLimit` y
  `AppointmentLinks__SigningKey`.
- El tope por teléfono puede superarse por uno con dos reservas exactamente simultáneas del mismo teléfono; se acepta.
