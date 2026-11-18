import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api/client';
import type { components } from '@/shared/api/schema';

type Schemas = components['schemas'];

export type CashKind = Schemas['CashKind'];
export const cashKinds: CashKind[] = ['Income', 'Expense'];

export type CashMovement = Omit<Schemas['CashMovementResponse'], 'amount'> & { amount: number };
export type CashClosing = Omit<Schemas['CashClosingResponse'], 'expected' | 'counted' | 'difference'> & {
  expected: number;
  counted: number;
  difference: number;
};

export interface CashDay {
  date: string;
  currency: string;
  cashPayments: number;
  manualNet: number;
  expected: number;
  isClosed: boolean;
  closing: CashClosing | null;
  movements: CashMovement[];
}

const cashKey = ['cash'] as const;

const toMovement = (m: Schemas['CashMovementResponse']): CashMovement => ({ ...m, amount: Number(m.amount) });

const toDay = (d: Schemas['CashDayResponse']): CashDay => ({
  date: d.date,
  currency: d.currency,
  cashPayments: Number(d.cashPayments),
  manualNet: Number(d.manualNet),
  expected: Number(d.expected),
  isClosed: d.isClosed,
  closing: d.closing
    ? {
        ...d.closing,
        expected: Number(d.closing.expected),
        counted: Number(d.closing.counted),
        difference: Number(d.closing.difference),
      }
    : null,
  movements: d.movements.map(toMovement),
});

export function useCashDay(date: string) {
  return useQuery({
    queryKey: [...cashKey, date],
    queryFn: async () => toDay(await unwrap(api.GET('/api/cash-register', { params: { query: { date } } }))),
  });
}

export function useRegisterCashMovement() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { kind: CashKind; amount: number; concept: string }) =>
      unwrap(api.POST('/api/cash-register/movements', { body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: cashKey }),
  });
}

export function useReverseCashMovement() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) =>
      unwrap(
        api.POST('/api/cash-register/movements/{id}/reversal', {
          params: { path: { id } },
          body: { reason },
        }),
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: cashKey }),
  });
}

export function useCloseCashDay() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { date: string; countedAmount: number; note: string | null }) =>
      unwrap(api.POST('/api/cash-register/closings', { body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: cashKey }),
  });
}
