import { useMutation, useQuery } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api/client';
import type { PaymentMethod } from '@/features/payments/payments-api';
import type { components } from '@/shared/api/schema';
import { downloadBlob } from '@/shared/files/download';

type Schemas = components['schemas'];

export interface ReportGroup {
  id: string;
  name: string;
  count: number;
  value: number;
}

export interface SummaryReport {
  from: string;
  to: string;
  currency: string;
  collected: number;
  byMethod: { method: PaymentMethod; total: number }[];
  appointmentsByStatus: { status: Schemas['AppointmentStatus']; count: number }[];
  completedValue: number;
  byProfessional: ReportGroup[];
  byService: ReportGroup[];
}

const toGroup = (g: Schemas['ReportGroup']): ReportGroup => ({
  ...g,
  count: Number(g.count),
  value: Number(g.value),
});

export function useSummaryReport(from: string, to: string, enabled: boolean) {
  return useQuery({
    queryKey: ['reports', 'summary', from, to],
    enabled,
    queryFn: async (): Promise<SummaryReport> => {
      const r = await unwrap(api.GET('/api/reports/summary', { params: { query: { from, to } } }));
      return {
        from: r.from,
        to: r.to,
        currency: r.currency,
        collected: Number(r.collected),
        byMethod: r.byMethod.map((m) => ({ method: m.method as PaymentMethod, total: Number(m.total) })),
        appointmentsByStatus: r.appointmentsByStatus.map((s) => ({
          status: s.status as Schemas['AppointmentStatus'],
          count: Number(s.count),
        })),
        completedValue: Number(r.completedValue),
        byProfessional: r.byProfessional.map(toGroup),
        byService: r.byService.map(toGroup),
      };
    },
  });
}

/** Downloads the summary of a range as an Excel workbook. */
export function useExportSummaryReport() {
  return useMutation({
    mutationFn: async ({ from, to }: { from: string; to: string }) => {
      const blob = await unwrap(
        api.GET('/api/reports/summary/export', { params: { query: { from, to } }, parseAs: 'blob' }),
      );
      downloadBlob(blob as Blob, `reporte-${from}-${to}.xlsx`);
    },
  });
}
