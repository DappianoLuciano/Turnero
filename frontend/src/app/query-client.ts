import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query';
import { subscriptionKey } from '@/features/subscription/subscription-api';
import { ApiError } from '@/shared/api/problem';

export function createQueryClient() {
  // The subscription can end while the panel is open: any 402 re-checks it, and the panel shows the paywall.
  const onError = (error: unknown) => {
    if (error instanceof ApiError && error.status === 402 && error.code === 'subscription_required') {
      void client.invalidateQueries({ queryKey: subscriptionKey });
    }
  };

  const client: QueryClient = new QueryClient({
    queryCache: new QueryCache({ onError }),
    mutationCache: new MutationCache({ onError }),
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        // Do not retry client errors (4xx): they will not fix themselves.
        retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2,
      },
    },
  });
  return client;
}
