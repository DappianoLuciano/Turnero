import createClient from 'openapi-fetch';
import { sessionStore, type SessionResponse } from '@/features/auth/session-store';
import { ApiError, isProblemDetails } from './problem';
import type { paths } from './schema';

const AUTH_PATHS = ['/api/auth/login', '/api/auth/refresh', '/api/auth/logout'];

/** Same-origin in every environment: Vite proxies /api in dev; production serves API under /api. */
const baseUrl = globalThis.location?.origin ?? 'http://localhost';

let refreshInFlight: Promise<boolean> | null = null;

/**
 * Exchanges the httpOnly refresh cookie for a new access token. Single-flight: concurrent
 * 401s share one refresh request (the refresh token is rotated on every use).
 */
export function refreshSession(): Promise<boolean> {
  refreshInFlight ??= (async () => {
    try {
      const response = await fetch(`${baseUrl}/api/auth/refresh`, { method: 'POST', credentials: 'include' });
      if (!response.ok) {
        sessionStore.clear();
        return false;
      }
      sessionStore.setSession((await response.json()) as SessionResponse);
      return true;
    } catch {
      sessionStore.clear();
      return false;
    } finally {
      refreshInFlight = null;
    }
  })();
  return refreshInFlight;
}

function withAuth(request: Request): Request {
  const token = sessionStore.accessToken();
  if (!token) return request;
  const headers = new Headers(request.headers);
  headers.set('Authorization', `Bearer ${token}`);
  return new Request(request, { headers });
}

/** fetch wrapper: adds the bearer token and transparently refreshes once on 401. */
async function authFetch(input: Request): Promise<Response> {
  const isAuthCall = AUTH_PATHS.some((p) => new URL(input.url).pathname === p);
  const retry = input.clone();
  const response = await fetch(withAuth(input));

  if (response.status !== 401 || isAuthCall || !sessionStore.accessToken()) {
    return response;
  }
  return (await refreshSession()) ? fetch(withAuth(retry)) : response;
}

/** Typed client generated from the backend OpenAPI document (`pnpm gen:api`). */
export const api = createClient<paths>({
  baseUrl,
  credentials: 'include',
  fetch: authFetch,
});

/** Unwraps an openapi-fetch result: returns data or throws ApiError with the Problem Details. */
export async function unwrap<T>(
  promise: Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<T> {
  const { data, error, response } = await promise;
  if (!response.ok) {
    throw new ApiError(response.status, isProblemDetails(error) ? error : null);
  }
  return data as T;
}
