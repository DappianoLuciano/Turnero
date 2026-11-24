import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api/client';
import type { components } from '@/shared/api/schema';

type Dto = components['schemas']['PublicAppointmentResponse'];

/** An appointment as its customer sees it through the link they were given. */
export interface ManagedAppointment {
  businessName: string;
  businessPhone: string | null;
  professionalName: string;
  serviceName: string;
  customerName: string;
  startsAt: string;
  endsAt: string;
  price: number;
  currency: string;
  status: 'Scheduled' | 'Completed' | 'Cancelled' | 'NoShow';
  canChange: boolean;
  noticeHours: number;
}

const toManaged = (d: Dto): ManagedAppointment => ({
  businessName: d.businessName,
  businessPhone: d.businessPhone ?? null,
  professionalName: d.professionalName,
  serviceName: d.serviceName,
  customerName: d.customerName,
  startsAt: d.startsAt,
  endsAt: d.endsAt,
  price: Number(d.price),
  currency: d.currency,
  status: d.status as ManagedAppointment['status'],
  canChange: d.canChange,
  noticeHours: Number(d.noticeHours),
});

const key = (slug: string, token: string) => ['public', slug, 'appointment', token] as const;

export function usePublicAppointment(slug: string, token: string) {
  return useQuery({
    queryKey: key(slug, token),
    queryFn: async () =>
      toManaged(
        await unwrap(
          api.GET('/api/public/{slug}/appointments/{token}', { params: { path: { slug, token } } }),
        ),
      ),
  });
}

/** Free times to move the appointment to (its own current time counts as free). */
export function useAppointmentSlots(slug: string, token: string, date: string, enabled: boolean) {
  return useQuery({
    enabled,
    queryKey: [...key(slug, token), 'availability', date],
    queryFn: async () => {
      const r = await unwrap(
        api.GET('/api/public/{slug}/appointments/{token}/availability', {
          params: { path: { slug, token }, query: { date } },
        }),
      );
      return {
        isClosed: r.isClosed,
        slots: r.slots.map((s) => ({ startsAt: s.startsAt, minutes: Number(s.minutes) })),
      };
    },
  });
}

export function useRescheduleAppointment(slug: string, token: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (startsAt: string) =>
      toManaged(
        await unwrap(
          api.POST('/api/public/{slug}/appointments/{token}/reschedule', {
            params: { path: { slug, token } },
            body: { startsAt },
          }),
        ),
      ),
    onSuccess: (appointment) => queryClient.setQueryData(key(slug, token), appointment),
    // The times shown may be stale now (someone else may have booked): reload them.
    onSettled: () => queryClient.invalidateQueries({ queryKey: [...key(slug, token), 'availability'] }),
  });
}

export function useCancelAppointment(slug: string, token: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () =>
      toManaged(
        await unwrap(
          api.POST('/api/public/{slug}/appointments/{token}/cancel', { params: { path: { slug, token } } }),
        ),
      ),
    onSuccess: (appointment) => queryClient.setQueryData(key(slug, token), appointment),
  });
}
