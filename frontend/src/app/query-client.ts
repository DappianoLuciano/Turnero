import { QueryClient } from '@tanstack/react-query';
import { ApiError } from '@/shared/api/problem';

export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        // Do not retry client errors (4xx): they will not fix themselves.
        retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2,
      },
    },
  });
}
