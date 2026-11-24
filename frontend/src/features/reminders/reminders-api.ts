import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api/client';

export function useReminders(date: string) {
  return useQuery({
    queryKey: ['reminders', date],
    queryFn: () => unwrap(api.GET('/api/appointments/reminders', { params: { query: { date } } })),
  });
}

/** Marks the reminder as sent (the business opened WhatsApp for it). */
export function useMarkReminderSent(date: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/appointments/{id}/reminder-sent', { params: { path: { id } } })),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['reminders', date] }),
  });
}
