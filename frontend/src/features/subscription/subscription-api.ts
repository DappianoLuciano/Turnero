import { useQuery } from '@tanstack/react-query';
import { api, unwrap } from '@/shared/api/client';

export const subscriptionKey = ['subscription'] as const;

/** Trial / paid status of the current business. Reachable even when the subscription expired. */
export function useSubscription() {
  return useQuery({
    queryKey: subscriptionKey,
    queryFn: () => unwrap(api.GET('/api/subscription')),
    staleTime: 60_000,
    refetchOnWindowFocus: true,
  });
}
