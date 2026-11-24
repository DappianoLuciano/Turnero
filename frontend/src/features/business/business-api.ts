import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api/client';

/** The business of the current session (its time zone drives every date shown in the panel). */
export function useBusiness() {
  return useQuery({
    queryKey: ['business', 'current'],
    queryFn: () => unwrap(api.GET('/api/businesses/current')),
    staleTime: 5 * 60_000,
  });
}

/** Contact phone and the notice customers must respect to change or cancel on their own (owner only). */
export function useUpdateBusinessSettings() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { phone: string | null; customerChangeNoticeHours: number }) =>
      unwrap(api.PUT('/api/businesses/current/settings', { body })),
    onSuccess: (business) => queryClient.setQueryData(['business', 'current'], business),
  });
}
