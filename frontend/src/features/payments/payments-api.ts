import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api/client';
import type { components } from '@/shared/api/schema';

type Schemas = components['schemas'];

export type PaymentMethod = Schemas['PaymentMethod'];
export const paymentMethods: PaymentMethod[] = ['Cash', 'Card', 'Transfer', 'Other'];

export type Payment = Omit<Schemas['PaymentResponse'], 'amount'> & { amount: number };

export interface AppointmentPayments {
  appointmentId: string;
  price: number;
  currency: string;
  paid: number;
  balance: number;
  acceptsPayments: boolean;
  payments: Payment[];
}

export interface DailyPayments {
  currency: string;
  total: number;
  byMethod: { method: PaymentMethod; total: number }[];
  items: { payment: Payment; customerName: string }[];
}

const paymentsKey = ['payments'] as const;

const toPayment = (p: Schemas['PaymentResponse']): Payment => ({ ...p, amount: Number(p.amount) });

const toAppointmentPayments = (r: Schemas['AppointmentPaymentsResponse']): AppointmentPayments => ({
  ...r,
  price: Number(r.price),
  paid: Number(r.paid),
  balance: Number(r.balance),
  payments: r.payments.map(toPayment),
});

export function useAppointmentPayments(appointmentId: string) {
  return useQuery({
    queryKey: [...paymentsKey, 'appointment', appointmentId],
    queryFn: async () =>
      toAppointmentPayments(await unwrap(api.GET('/api/payments', { params: { query: { appointmentId } } }))),
  });
}

export function useDailyPayments(date: string) {
  return useQuery({
    queryKey: [...paymentsKey, 'daily', date],
    queryFn: async (): Promise<DailyPayments> => {
      const r = await unwrap(api.GET('/api/payments/daily', { params: { query: { date } } }));
      return {
        currency: r.currency,
        total: Number(r.total),
        byMethod: r.byMethod.map((m) => ({ method: m.method, total: Number(m.total) })),
        items: r.items.map((i) => ({ payment: toPayment(i.payment), customerName: i.customerName })),
      };
    },
  });
}

export function useCollectPayment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { appointmentId: string; amount: number; method: PaymentMethod }) =>
      unwrap(api.POST('/api/payments', { body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: paymentsKey }),
  });
}

export function useReversePayment() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      unwrap(api.POST('/api/payments/{id}/reversal', { params: { path: { id } }, body: { reason } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: paymentsKey }),
  });
}
