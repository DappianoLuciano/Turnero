import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api/client';
import type { components } from '@/shared/api/schema';

export type Appointment = Omit<components['schemas']['AppointmentResponse'], 'price'> & { price: number };
export type AppointmentStatus = components['schemas']['AppointmentStatus'];

export interface NewAppointment {
  customerId: string;
  professionalId: string;
  serviceId: string;
  /** UTC instant (ISO 8601). */
  startsAt: string;
  notes: string | null;
}

export type AppointmentChanges = Omit<NewAppointment, 'customerId'>;

const appointmentsKey = ['appointments'] as const;

export function useAppointments(date: string | null) {
  return useQuery({
    queryKey: [...appointmentsKey, date],
    enabled: date !== null,
    queryFn: async () =>
      (await unwrap(api.GET('/api/appointments', { params: { query: { date: date ?? '' } } }))).map(
        (a): Appointment => ({ ...a, price: Number(a.price) }),
      ),
  });
}

export function useCreateAppointment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: NewAppointment) => unwrap(api.POST('/api/appointments', { body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: appointmentsKey }),
  });
}

export function useUpdateAppointment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, ...input }: AppointmentChanges & { id: string }) =>
      unwrap(api.PUT('/api/appointments/{id}', { params: { path: { id } }, body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: appointmentsKey }),
  });
}

export function useChangeAppointmentStatus() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, status }: { id: string; status: AppointmentStatus }) =>
      unwrap(api.PUT('/api/appointments/{id}/status', { params: { path: { id } }, body: { status } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: appointmentsKey }),
  });
}
