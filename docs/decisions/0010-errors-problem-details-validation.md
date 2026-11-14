# 0010. Errores con Problem Details y validación con FluentValidation

- Estado: aceptada
- Fecha: 2026-11-14

## Contexto

El frontend necesita errores con un formato único y códigos estables para mostrar mensajes en español. La validación
tiene que ser igual en todos los endpoints.

## Decisión

- Todos los errores salen como **Problem Details (RFC 9457)** (`application/problem+json`), con las extensiones
  `code` (estable, en `snake_case`) y `traceId`.
- Los errores esperados se lanzan como `AppException` (`NotFound`, `Conflict`, `Unauthorized`, `Forbidden`).
  `GlobalExceptionHandler` los traduce, y cualquier otra excepción es un 500 sin detalles internos.
- Los 401/403/404 del pipeline también salen como Problem Details (`UseStatusCodePages`).
- **Validación**: FluentValidation. El validator vive junto al slice. `ValidationEndpointFilter` se aplica a todo
  `/api` y valida cualquier argumento con `IValidator<T>` registrado → 400 con `code: validation_failed` y
  `errors: { campo: [códigos] }`. Los errores de validación son **códigos** (`required`, `invalid_email`, `too_long`),
  no textos: el frontend los traduce.
- `ConventionsTests` exige que todo tipo `*Request` tenga validator.

## Consecuencias

- Los mensajes para el usuario están solo en el frontend (`shared/i18n`).
- Se eligió FluentValidation en vez de la validación nativa de .NET 10 (DataAnnotations) porque escala mejor a reglas
  de negocio (reglas cruzadas, asincrónicas, por estado).
