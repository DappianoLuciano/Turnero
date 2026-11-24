import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api/client';
import type { components } from '@/shared/api/schema';

type Schemas = components['schemas'];

export interface PublicService {
  id: string;
  name: string;
  durationMinutes: number;
  price: number;
  currency: string;
}

export interface PublicProfessional {
  id: string;
  name: string;
  serviceIds: string[];
}

export interface PublicCatalog {
  business: Schemas['PublicBusinessResponse'];
  services: PublicService[];
  professionals: PublicProfessional[];
}

/** A free start time and who would attend it (relevant when "any professional" is chosen). */
export interface BookableSlot {
  startsAt: string;
  minutes: number;
  professionalId: string;
}

export interface PublicBooking {
  appointmentId: string;
  businessName: string;
  serviceName: string;
  professionalName: string;
  customerName: string;
  startsAt: string;
  endsAt: string;
  price: number;
  currency: string;
  /** Secret of the link where the customer can change or cancel (`/r/:slug/turno/:token`). */
  manageToken: string;
}

export interface BookingRequest {
  professionalId: string;
  serviceId: string;
  startsAt: string;
  customerName: string;
  customerPhone: string;
}

export function usePublicCatalog(slug: string) {
  return useQuery({
    queryKey: ['public', slug, 'catalog'],
    queryFn: async (): Promise<PublicCatalog> => {
      const c = await unwrap(api.GET('/api/public/{slug}/catalog', { params: { path: { slug } } }));
      return {
        business: c.business,
        services: c.services.map((s) => ({
          id: s.id,
          name: s.name,
          durationMinutes: Number(s.durationMinutes),
          price: Number(s.price),
          currency: s.currency,
        })),
        professionals: c.professionals.map((p) => ({ id: p.id, name: p.name, serviceIds: p.serviceIds })),
      };
    },
  });
}

async function fetchSlots(slug: string, professionalId: string, serviceId: string, date: string) {
  const r = await unwrap(
    api.GET('/api/public/{slug}/availability', {
      params: { path: { slug }, query: { professionalId, serviceId, date } },
    }),
  );
  return {
    isClosed: r.isClosed,
    slots: r.slots.map((s): BookableSlot => ({
      startsAt: s.startsAt,
      minutes: Number(s.minutes),
      professionalId,
    })),
  };
}

/**
 * Free times of a day for one or several professionals. With several ("any professional"), times are merged and
 * each one is assigned to the first professional that has it free.
 */
export function useDaySlots(slug: string, professionalIds: string[], serviceId: string, date: string) {
  const results = useQueries({
    queries: professionalIds.map((professionalId) => ({
      queryKey: ['public', slug, 'availability', professionalId, serviceId, date],
      queryFn: () => fetchSlots(slug, professionalId, serviceId, date),
    })),
  });

  const isPending = results.some((r) => r.isPending);
  const error = results.find((r) => r.isError)?.error ?? null;
  const loaded = results.flatMap((r) => (r.data ? [r.data] : []));
  const byTime = new Map<string, BookableSlot>();
  for (const { slots } of loaded) {
    for (const slot of slots) {
      if (!byTime.has(slot.startsAt)) byTime.set(slot.startsAt, slot);
    }
  }
  const slots = [...byTime.values()].sort((a, b) => a.minutes - b.minutes);
  const isClosed = loaded.length > 0 && loaded.every((r) => r.isClosed);
  return { isPending, error, slots, isClosed };
}

export function useCreateBooking(slug: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (input: BookingRequest): Promise<PublicBooking> => {
      const r = await unwrap(
        api.POST('/api/public/{slug}/appointments', { params: { path: { slug } }, body: input }),
      );
      return { ...r, price: Number(r.price) };
    },
    // Whatever happens, the times shown may be stale now (someone else may have booked): reload them.
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['public', slug, 'availability'] }),
  });
}
