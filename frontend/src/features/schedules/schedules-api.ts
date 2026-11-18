import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api/client';
import type { components } from '@/shared/api/schema';

type Schemas = components['schemas'];

/** `dayOfWeek`: 0 = Sunday ... 6 = Saturday. Minutes are since local midnight of the business. */
export interface WorkingBlock {
  professionalId: string;
  dayOfWeek: number;
  startMinute: number;
  endMinute: number;
}

export type ScheduleException = Schemas['ScheduleExceptionResponse'];

const schedulesKey = ['schedules'] as const;

export function useSchedules() {
  return useQuery({
    queryKey: [...schedulesKey, 'weekly'],
    queryFn: async (): Promise<WorkingBlock[]> =>
      (await unwrap(api.GET('/api/schedules'))).blocks.map((b) => ({
        professionalId: b.professionalId,
        dayOfWeek: Number(b.dayOfWeek),
        startMinute: Number(b.startMinute),
        endMinute: Number(b.endMinute),
      })),
  });
}

export function useScheduleExceptions(from: string | null, to: string | null) {
  return useQuery({
    queryKey: [...schedulesKey, 'exceptions', from, to],
    enabled: from !== null && to !== null,
    queryFn: () =>
      unwrap(api.GET('/api/schedules/exceptions', { params: { query: { from: from ?? '', to: to ?? '' } } })),
  });
}

export function useReplaceSchedule() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({
      professionalId,
      blocks,
    }: {
      professionalId: string;
      blocks: { dayOfWeek: number; startMinute: number; endMinute: number }[];
    }) =>
      unwrap(
        api.PUT('/api/schedules/professionals/{professionalId}', {
          params: { path: { professionalId } },
          body: { blocks },
        }),
      ),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: schedulesKey }),
  });
}

export function useCreateScheduleException() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: { date: string; professionalId: string | null; reason: string | null }) =>
      unwrap(api.POST('/api/schedules/exceptions', { body: input })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: schedulesKey }),
  });
}

export function useDeleteScheduleException() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.DELETE('/api/schedules/exceptions/{id}', { params: { path: { id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: schedulesKey }),
  });
}
