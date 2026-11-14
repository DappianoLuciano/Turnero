import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { t } from '@/shared/i18n';
import { renderApp } from '@/test/render';
import { API, problem, server, sessionFixture } from '@/test/server';

describe('auth flow', () => {
  it('redirects anonymous users from the panel to the login page', async () => {
    const { router } = renderApp('/app/agenda');

    expect(await screen.findByRole('heading', { name: t.auth.login.title })).toBeInTheDocument();
    expect(router.state.location.pathname).toBe('/app/login');
  });

  it('shows validation messages in Spanish', async () => {
    renderApp('/app/login');
    await userEvent.click(await screen.findByRole('button', { name: t.auth.login.submit }));

    expect(await screen.findByText(t.auth.validation.emailRequired)).toBeInTheDocument();
    expect(screen.getByText(t.auth.validation.passwordRequired)).toBeInTheDocument();
  });

  it('logs in and lands on the panel home', async () => {
    let sentBody: unknown;
    server.use(
      http.post(`${API}/api/auth/login`, async ({ request }) => {
        sentBody = await request.json();
        return HttpResponse.json(sessionFixture);
      }),
    );
    renderApp('/app/login');

    await userEvent.type(await screen.findByLabelText(t.auth.login.email), 'owner@demo.local');
    await userEvent.type(screen.getByLabelText(t.auth.login.password), 'Demo1234!');
    await userEvent.click(screen.getByRole('button', { name: t.auth.login.submit }));

    expect(
      await screen.findByRole('heading', { name: t.panel.home.greeting(sessionFixture.user.fullName) }),
    ).toBeInTheDocument();
    expect(screen.getByRole('link', { name: t.panel.nav.agenda })).toBeInTheDocument();
    expect(sentBody).toEqual({ email: 'owner@demo.local', password: 'Demo1234!' });
  });

  it('maps the API problem code to a localized error', async () => {
    server.use(http.post(`${API}/api/auth/login`, () => problem(401, 'invalid_credentials')));
    renderApp('/app/login');

    await userEvent.type(await screen.findByLabelText(t.auth.login.email), 'owner@demo.local');
    await userEvent.type(screen.getByLabelText(t.auth.login.password), 'wrong');
    await userEvent.click(screen.getByRole('button', { name: t.auth.login.submit }));

    expect(await screen.findByRole('alert')).toHaveTextContent(t.errors.codes.invalid_credentials!);
  });

  it('restores the session from the refresh cookie on load', async () => {
    server.use(http.post(`${API}/api/auth/refresh`, () => HttpResponse.json(sessionFixture)));
    renderApp('/app');

    expect(
      await screen.findByRole('heading', { name: t.panel.home.greeting(sessionFixture.user.fullName) }),
    ).toBeInTheDocument();
  });
});
