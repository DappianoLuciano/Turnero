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
  services: {
    title: 'Servicios',
    new: 'Nuevo servicio',
    empty: 'Todavía no hay servicios cargados.',
    emptyOwner: 'Todavía no cargaste servicios. Creá el primero para empezar a recibir turnos.',
    inactive: 'Inactivo',
    duration: (minutes: number) => `${minutes} min`,
    edit: (name: string) => `Editar ${name}`,
    remove: (name: string) => `Eliminar ${name}`,
    confirmRemove: (name: string) => `¿Eliminar “${name}”? No se puede deshacer.`,
    confirmYes: 'Sí, eliminar',
    confirmNo: 'Cancelar',
    removing: 'Eliminando…',
    form: {
      createTitle: 'Nuevo servicio',
      editTitle: 'Editar servicio',
      name: 'Nombre',
      namePlaceholder: 'Corte de pelo',
      description: 'Descripción (opcional)',
      duration: 'Duración (minutos)',
      price: 'Precio',
      active: 'Disponible para reservar',
      save: 'Guardar',
      saving: 'Guardando…',
      cancel: 'Cancelar',
    },
    validation: {
      nameRequired: 'Ingresá el nombre del servicio.',
      nameTooLong: 'El nombre no puede superar los 120 caracteres.',
      descriptionTooLong: 'La descripción no puede superar los 500 caracteres.',
      durationRange: 'La duración debe ser de entre 5 y 480 minutos.',
      priceInvalid: 'Ingresá un precio válido, con hasta 2 decimales.',
    },
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
      service_not_found: 'El servicio ya no existe.',
    } as Record<string, string>,
  },
} as const;

export type Messages = typeof esAR;
