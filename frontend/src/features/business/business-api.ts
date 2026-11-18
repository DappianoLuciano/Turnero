import { useQuery } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api/client';

/** The business of the current session (its time zone drives every date shown in the panel). */
export function useBusiness() {
  return useQuery({
    queryKey: ['business', 'current'],
    queryFn: () => unwrap(api.GET('/api/businesses/current')),
    staleTime: 5 * 60_000,
  });
}
