import { product } from '@/shared/config/product';

/**
 * Todos los textos de la interfaz (español rioplatense).
 * Para traducir: crear otro archivo con la misma forma (`typeof esAR`) y elegirlo en ./index.ts.
 */
export const esAR = {
  app: {
    name: product.name,
    loading: 'Cargando…',
  },
  auth: {
    login: {
      title: 'Ingresá a tu cuenta',
      subtitle: 'Gestioná los turnos de tu negocio',
      email: 'Email',
      emailPlaceholder: 'vos@tunegocio.com',
      password: 'Contraseña',
      submit: 'Ingresar',
      submitting: 'Ingresando…',
    },
    logout: 'Cerrar sesión',
    validation: {
      emailRequired: 'Ingresá tu email.',
      emailInvalid: 'El email no es válido.',
      passwordRequired: 'Ingresá tu contraseña.',
    },
  },
  panel: {
    openMenu: 'Abrir menú',
    closeMenu: 'Cerrar menú',
    nav: {
      home: 'Inicio',
      agenda: 'Agenda',
      services: 'Servicios',
      staff: 'Profesionales',
      schedules: 'Horarios',
      customers: 'Clientes',
      payments: 'Cobros',
      cashRegister: 'Caja',
      reports: 'Reportes',
    },
    home: {
      greeting: (name: string) => `¡Hola, ${name}!`,
      description: 'Desde acá vas a poder gestionar tu negocio.',
    },
    placeholder: 'Esta sección todavía está en construcción.',
  },
  booking: {
    title: 'Reservá tu turno',
    placeholder: (slug: string) => `La reserva online para “${slug}” va a estar disponible muy pronto.`,
  },
  notFound: {
    title: 'Página no encontrada',
    back: 'Volver al inicio',
  },
  errors: {
    generic: 'Ocurrió un error inesperado. Probá de nuevo en un rato.',
    network: 'No pudimos conectarnos. Revisá tu conexión.',
    /** Mensajes por `code` de Problem Details devuelto por la API. */
    codes: {
      invalid_credentials: 'Email o contraseña incorrectos.',
      no_membership: 'Tu usuario no pertenece a ningún negocio.',
      invalid_refresh_token: 'Tu sesión expiró. Ingresá de nuevo.',
      session_invalid: 'Tu sesión ya no es válida. Ingresá de nuevo.',
      validation_failed: 'Revisá los datos ingresados.',
    } as Record<string, string>,
  },
} as const;

export type Messages = typeof esAR;
