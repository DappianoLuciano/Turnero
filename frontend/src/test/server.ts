import { http, HttpResponse } from 'msw';
import { setupServer } from 'msw/node';
import type { SessionResponse } from '@/features/auth/session-store';

export const API = 'http://localhost:3000';

export const sessionFixture: SessionResponse = {
  accessToken: 'access-1',
  accessTokenExpiresAt: '2030-01-01T00:00:00Z',
  user: {
    id: '00000000-0000-7000-8000-000000000001',
    email: 'owner@demo.local',
    fullName: 'Dueño Demo',
    businessId: '00000000-0000-7000-8000-0000000000b1',
    role: 'Owner',
  },
};

export const subscriptionFixture = (state: 'Trial' | 'Active' | 'Expired', daysLeft: number) => ({
  state,
  trialEndsAt: '2026-11-20T00:00:00Z',
  paidUntil: state === 'Active' ? '2026-12-20T00:00:00Z' : null,
  daysLeft,
});

export const problem = (status: number, code: string) =>
  HttpResponse.json(
    { status, title: code, code },
    { status, headers: { 'Content-Type': 'application/problem+json' } },
  );

/** Default: no session (refresh fails). Tests override with server.use(...). */
export const server = setupServer(
  http.post(`${API}/api/auth/refresh`, () => problem(401, 'invalid_refresh_token')),
  // Default: a paid, far-from-expiring subscription (no banner, no paywall).
  http.get(`${API}/api/subscription`, () => HttpResponse.json(subscriptionFixture('Active', 20))),
);
