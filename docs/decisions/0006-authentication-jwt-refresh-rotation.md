# 0006. Autenticación: JWT corto y refresh token rotativo en cookie httpOnly

- Estado: aceptada
- Fecha: 2026-11-14

## Contexto

El panel es una SPA/PWA que habla con la API. Hay que minimizar el impacto de un XSS y de un token robado, y que la
sesión dure días en el celular.

## Decisión

- **Access token**: JWT HS256 de 15 minutos con claims `sub`, `email`, `business_id` y `role`. El frontend lo guarda
  **solo en memoria** y lo manda como `Authorization: Bearer`.
- **Refresh token**: opaco, 256 bits aleatorios, válido 14 días. Viaja **solo** en la cookie `rt` (`HttpOnly`, `Secure`,
  `SameSite=Strict`, `Path=/api/auth`). En la base se guarda solo su SHA-256 (`refresh_tokens`, tabla global porque se
  busca antes de conocer el tenant). La columna del negocio se llama `issued_for_business_id` a propósito, para que no
  se confunda con una tabla de tenant.
- **Rotación**: cada `POST /api/auth/refresh` revoca el token usado y emite uno nuevo de la misma familia. Si se reusa
  un token ya rotado, se revoca **toda la familia** (detección de robo). El "claim" del token es atómico
  (`UPDATE … WHERE revoked_at IS NULL`), así que dos refresh concurrentes no generan dos sesiones.
- El refresh también verifica que la membresía siga existiendo.
- El frontend y la API se sirven **same-origin**: en dev, Vite proxea `/api`; en producción, la API va detrás de `/api`
  del mismo dominio o en un subdominio del mismo sitio.

## Consecuencias

- Un XSS no puede leer el refresh token, y un access token robado vale como máximo 15 minutos.
- La cookie `Secure` funciona en `http://localhost` con Chrome y Firefox. Safari puede requerir HTTPS local.
- La clave de firma (`Jwt__SigningKey`, ≥ 32 bytes) es un secreto de entorno. Si hiciera falta, se puede migrar a RS256.
