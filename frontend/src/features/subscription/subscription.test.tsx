import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { t } from '@/shared/i18n';
import { renderApp } from '@/test/render';
import { API, problem, server, sessionFixture, subscriptionFixture } from '@/test/server';

const loggedIn = () =>
  server.use(
    http.post(`${API}/api/auth/refresh`, () => HttpResponse.json(sessionFixture)),
    http.get(`${API}/api/services`, () => HttpResponse.json([])),
  );

describe('subscription', () => {
  it('shows the remaining trial days on top of the panel', async () => {
    loggedIn();
    server.use(http.get(`${API}/api/subscription`, () => HttpResponse.json(subscriptionFixture('Trial', 2))));
    renderApp('/app/services');

    expect(await screen.findByText(t.subscription.banner.trial(2))).toBeInTheDocument();
  });

  it('reminds when a paid period is about to end', async () => {
    loggedIn();
    server.use(
      http.get(`${API}/api/subscription`, () => HttpResponse.json(subscriptionFixture('Active', 3))),
    );
    renderApp('/app/services');

    expect(await screen.findByText(t.subscription.banner.renewal(3))).toBeInTheDocument();
  });

  it('replaces the panel with the paywall when the subscription expired', async () => {
    loggedIn();
    server.use(
      http.get(`${API}/api/subscription`, () => HttpResponse.json(subscriptionFixture('Expired', 0))),
      http.get(`${API}/api/businesses/current`, () =>
        HttpResponse.json({
          id: 'b',
          name: 'Demo',
          slug: 'demo',
          timeZone: 'America/Argentina/Buenos_Aires',
          currency: 'ARS',
        }),
      ),
    );
    renderApp('/app/services');

    expect(await screen.findByRole('heading', { name: t.subscription.expired.title })).toBeInTheDocument();
    expect(await screen.findByText(t.subscription.expired.yourId('demo'))).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: t.services.title })).not.toBeInTheDocument();
    // The owner can still sign out.
    expect(screen.getByRole('button', { name: t.auth.logout })).toBeInTheDocument();
  });

  it('shows the paywall when the API rejects a call with 402 during the session', async () => {
    loggedIn();
    let expired = false;
    server.use(
      http.get(`${API}/api/subscription`, () =>
        HttpResponse.json(expired ? subscriptionFixture('Expired', 0) : subscriptionFixture('Active', 20)),
      ),
      http.get(`${API}/api/services`, () => {
        expired = true;
        return problem(402, 'subscription_required');
      }),
    );
    renderApp('/app/services');

    expect(await screen.findByRole('heading', { name: t.subscription.expired.title })).toBeInTheDocument();
  });

  it('tells the public booking visitor that the business is not taking bookings', async () => {
    server.use(http.get(`${API}/api/public/demo/catalog`, () => problem(402, 'subscription_required')));
    renderApp('/r/demo');

    expect(await screen.findByRole('heading', { name: t.booking.closed.title })).toBeInTheDocument();
  });
});

describe('signup', () => {
  it('validates the form in Spanish', async () => {
    renderApp('/app/signup');
    await userEvent.click(await screen.findByRole('button', { name: t.auth.signup.submit }));

    expect(await screen.findByText(t.auth.signup.validation.businessRequired)).toBeInTheDocument();
    expect(screen.getByText(t.auth.signup.validation.nameRequired)).toBeInTheDocument();
    expect(screen.getByText(t.auth.validation.emailRequired)).toBeInTheDocument();
    expect(screen.getByText(t.auth.signup.validation.passwordShort)).toBeInTheDocument();
  });

  it('creates the account, signs in and lands on the panel', async () => {
    let signupBody: unknown;
    server.use(
      http.post(`${API}/api/signup`, async ({ request }) => {
        signupBody = await request.json();
        return HttpResponse.json({ businessId: 'b', slug: 'mi-negocio' }, { status: 201 });
      }),
      http.post(`${API}/api/auth/login`, () => HttpResponse.json(sessionFixture)),
    );
    renderApp('/app/signup');

    await userEvent.type(await screen.findByLabelText(t.auth.signup.businessName), 'Mi Negocio');
    await userEvent.type(screen.getByLabelText(t.auth.signup.fullName), 'Ana Pérez');
    await userEvent.type(screen.getByLabelText(t.auth.login.email), 'ana@test.local');
    await userEvent.type(screen.getByLabelText(t.auth.login.password), 'Passw0rd!');
    await userEvent.click(screen.getByRole('button', { name: t.auth.signup.submit }));

    expect(
      await screen.findByRole('heading', { name: t.panel.home.greeting(sessionFixture.user.fullName) }),
    ).toBeInTheDocument();
    expect(signupBody).toEqual({
      businessName: 'Mi Negocio',
      fullName: 'Ana Pérez',
      email: 'ana@test.local',
      password: 'Passw0rd!',
    });
  });

  it('shows the localized error when the email is already registered', async () => {
    server.use(http.post(`${API}/api/signup`, () => problem(409, 'email_taken')));
    renderApp('/app/signup');

    await userEvent.type(await screen.findByLabelText(t.auth.signup.businessName), 'Mi Negocio');
    await userEvent.type(screen.getByLabelText(t.auth.signup.fullName), 'Ana Pérez');
    await userEvent.type(screen.getByLabelText(t.auth.login.email), 'ana@test.local');
    await userEvent.type(screen.getByLabelText(t.auth.login.password), 'Passw0rd!');
    await userEvent.click(screen.getByRole('button', { name: t.auth.signup.submit }));

    expect(await screen.findByRole('alert')).toHaveTextContent(t.errors.codes.email_taken!);
  });

  it('links between login and signup', async () => {
    renderApp('/app/login');
    await userEvent.click(await screen.findByRole('link', { name: t.auth.goToSignup }));

    expect(await screen.findByRole('heading', { name: t.auth.signup.title })).toBeInTheDocument();
  });
});
