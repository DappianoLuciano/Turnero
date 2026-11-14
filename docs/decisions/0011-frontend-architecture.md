# 0011. Frontend: app única, PWA, zonas de rutas y cliente generado

- Estado: aceptada
- Fecha: 2026-11-14

## Contexto

Hay dos experiencias: el panel del negocio (uso diario, desde el celular del dueño o del recepcionista) y la web
pública de reservas. Somos dos devs y no queremos mantener dos apps.

## Decisión

- **Una sola app** Vite + React + TypeScript con dos zonas:
  - `/app/*`: panel, protegido por `RequireAuth`;
  - `/r/:slug/*`: reserva pública, sin login.
- **PWA** con `vite-plugin-pwa`: manifest generado desde `/product.json`, precache del app shell y nunca cache de `/api`.
- **Datos**: TanStack Query. **Rutas**: React Router (modo declarativo/data router, sin framework mode).
- **Cliente HTTP tipado**: `openapi-typescript` genera `schema.d.ts` desde `backend/openapi/openapi.json` (que la API
  genera en cada build) y `openapi-fetch` lo consume. El wrapper agrega el token y hace un refresh single-flight ante un 401.
- **UI**: Tailwind CSS 4 + shadcn/ui (componentes copiados en `src/shared/ui`). Mobile first.
- **Textos**: todos en `src/shared/i18n/es-AR.ts`. ESLint prohíbe texto literal en JSX.
- **Branding**: `/product.json` es la única fuente del nombre del producto (`[NOMBRE]` por ahora).

## Consecuencias

- Un cambio de contrato en la API rompe el typecheck del front. CI verifica que los tipos estén regenerados.
- Si la web pública necesita SEO fuerte, se puede separar o prerenderizar `/r/*` más adelante.
