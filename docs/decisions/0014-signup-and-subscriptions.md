# 0014. Alta de negocios y suscripción con prueba gratis

- Estado: aceptada
- Fecha: 2026-11-24

## Contexto

Cualquier persona tiene que poder crearse una cuenta y probar el producto sin hablar con nadie. Pasados 2 días sin
pagar, el negocio queda bloqueado hasta que pague, pero la puerta de alta sigue abierta para otros. Todavía no hay una
pasarela de cobro integrada (Mercado Pago figura como pendiente en [Payments](../../backend/src/Api/Modules/README.md)),
así que el pago se registra a mano por ahora.

## Decisión

- **Módulo `Subscriptions`** con la tabla `subscriptions` (una fila por negocio, `business_id` único, RLS):
  `trial_ends_at` y `paid_until` (nulo si nunca pagó). El estado **no se guarda**, se calcula con la hora actual:
  `Active` si `paid_until > ahora`, `Trial` si `trial_ends_at > ahora`, `Expired` en cualquier otro caso. Así no hay un
  proceso que "bloquee" a nadie ni estados que puedan quedar desfasados.
- **Todo negocio nace con prueba**: `BusinessProvisioning` llama a `ISubscriptionProvisioning.StartTrial` dentro de la
  misma transacción que crea el negocio. No existe un negocio sin suscripción (el alta, el seed y los tests pasan por
  ahí). Duración configurable con `Subscriptions:TrialDays` (por defecto 2).
- **Bloqueo total**. `SubscriptionGateFilter` (en `Shared/Tenancy`, detrás de la interfaz `ITenantAccessGate` que
  implementa el módulo, para que `Shared` no dependa de módulos) corre después del filtro de transacción en los grupos
  `Tenant` y `Public`. Si el negocio está `Expired`, o no tiene fila (falla cerrado), responde **402**
  `subscription_required`. Se exceptúan con `AllowWithoutSubscription` solo `GET /api/subscription` y `GET /api/me`, y
  el login sigue funcionando, para que el dueño llegue a la pantalla "activá tu suscripción". La web pública de
  reservas también se cierra (402). Los datos no se tocan.
- **Alta (`Signup`)**: `POST /api/signup` (anónimo, con rate limit por IP `RateLimiting:SignupsPerHour`, 10 por hora).
  Crea el negocio (el slug sale del nombre; si está tomado agrega un sufijo aleatorio) y su dueño, sin devolver sesión:
  el cliente inicia sesión con las credenciales recién creadas. El alta está siempre abierta, esté quien esté
  bloqueado.
- **El email ya registrado se rechaza (409 `email_taken`)**. `IUserProvisioning.AddMemberAsync` reutiliza al usuario
  existente sin verificar contraseña (sirve para invitaciones y seed); usarlo en un alta público permitiría colgarse de
  la cuenta de otra persona. Por eso el alta usa `RegisterOwnerAsync`, que nunca reutiliza usuarios. Un registro
  simultáneo con el mismo email lo resuelve el índice único de `users.email`.
- **Cobro manual**: `dotnet run --project src/Api -- activate <slug> [días=30]` suma días de período pago (desde ahora,
  o desde el fin del período vigente si todavía no venció). No hay endpoint HTTP de administración a propósito: una
  superficie menos que proteger. Funciona en cualquier entorno.
- **Negocios existentes**: la migración los crea con 10 años pagos para que el demo y los datos previos no queden
  bloqueados al día siguiente.

## Consecuencias

- Falta integrar el cobro real (Mercado Pago, suscripción recurrente): al llegar el pago bastará con llamar a
  `ISubscriptionAdmin.ActivateAsync`, el mismo punto que usa el comando. No se guarda historial de pagos de la
  suscripción (solo `paid_until`); si se necesita, será una tabla append-only (ADR 0009).
- El alta no verifica el email ni tiene captcha: lo único que frena abusos es el rate limit por IP. Un negocio de
  prueba abandonado queda en la base sin costo hasta que se defina una política de limpieza.
- El mensaje `email_taken` permite saber si un email está registrado (enumeración). Es el costo habitual de un alta con
  feedback inmediato; lo mitiga el rate limit.
- La pantalla de bloqueo muestra un email de contacto solo si `/product.json` define `supportEmail`.
