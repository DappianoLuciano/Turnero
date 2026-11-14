# 0005. Usuarios globales y membresías por negocio

- Estado: aceptada
- Fecha: 2026-11-14

## Contexto

Hoy un usuario pertenece a un solo negocio, pero más adelante una persona (por ejemplo, un profesional que trabaja en
dos locales, o el dueño de varias sucursales) va a necesitar acceder a varios. Además, el login ocurre antes de saber
el negocio.

## Decisión

- `users` es una tabla **global** (`IGlobalEntity`, sin `business_id` ni RLS). Guarda solo identidad: email normalizado
  y único, hash de contraseña, nombre y estado. **No guarda datos de negocio.**
- `business_memberships (business_id, user_id, role)` es dato de tenant con RLS, y la combinación `(business_id, user_id)` es única.
- Roles por negocio: `Owner`, `Staff`, `Reception`. El rol vive en la membresía, no en el usuario.
- Política extra `member_self_read` (solo `SELECT`): con `app.user_id` seteado, un usuario puede leer **sus propias**
  membresías aunque todavía no haya un negocio elegido. Se usa en el login.
- El access token se emite para **una** membresía (`business_id` + `role`). En esta etapa se elige la más antigua; más
  adelante habrá un `POST /api/auth/switch-business`.
- Contraseñas con `PasswordHasher<T>` de ASP.NET Core Identity (PBKDF2, rehash automático), sin Identity completo.

## Consecuencias

- Pasar a multi-negocio no requiere cambiar el modelo, solo agregar la selección o el cambio de negocio.
- `users` es una excepción explícita a "toda tabla lleva `business_id`". Toda nueva tabla global necesita un ADR que la justifique.
