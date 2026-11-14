import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { sessionStore } from '@/features/auth/session-store';
import { API, problem, server, sessionFixture } from '@/test/server';
import { api, unwrap } from './client';
import { ApiError } from './problem';

describe('api client', () => {
  it('sends the bearer token', async () => {
    sessionStore.setSession(sessionFixture);
    let auth: string | null = null;
    server.use(
      http.get(`${API}/api/me`, ({ request }) => {
        auth = request.headers.get('Authorization');
        return HttpResponse.json(sessionFixture.user);
      }),
    );

    await unwrap(api.GET('/api/me'));
    expect(auth).toBe('Bearer access-1');
  });

  it('refreshes once on 401 and retries with the new token', async () => {
    sessionStore.setSession({ ...sessionFixture, accessToken: 'expired' });
    let refreshCalls = 0;
    server.use(
      http.post(`${API}/api/auth/refresh`, () => {
        refreshCalls++;
        return HttpResponse.json({ ...sessionFixture, accessToken: 'fresh' });
      }),
      http.get(`${API}/api/me`, ({ request }) =>
        request.headers.get('Authorization') === 'Bearer fresh'
          ? HttpResponse.json(sessionFixture.user)
          : problem(401, 'unauthorized'),
      ),
    );

    const [a, b] = await Promise.all([unwrap(api.GET('/api/me')), unwrap(api.GET('/api/me'))]);

    expect(a.id).toBe(sessionFixture.user.id);
    expect(b.id).toBe(sessionFixture.user.id);
    expect(refreshCalls).toBe(1);
    expect(sessionStore.accessToken()).toBe('fresh');
  });

  it('clears the session when refresh fails and surfaces the problem', async () => {
    sessionStore.setSession({ ...sessionFixture, accessToken: 'expired' });
    server.use(http.get(`${API}/api/me`, () => problem(401, 'session_invalid')));

    const error = await unwrap(api.GET('/api/me')).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).code).toBe('session_invalid');
    expect(sessionStore.get().status).toBe('anonymous');
  });
});
