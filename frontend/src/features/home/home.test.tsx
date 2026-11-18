import { screen, within } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { t } from '@/shared/i18n';
import { todayIn } from '@/shared/time/business-time';
import { renderApp } from '@/test/render';
import { API, server, sessionFixture } from '@/test/server';

const timeZone = 'America/Argentina/Buenos_Aires';
const business = {
  id: sessionFixture.user.businessId,
  name: 'Demo',
  slug: 'demo',
  timeZone,
  currency: 'ARS',
};
const today = todayIn(timeZone);

const appointment = (id: string, status: string, startsAt: string, customerName: string) => ({
  id,
  customerId: 'c1',
  customerName,
  professionalId: 'p1',
  professionalName: 'Lucas',
  serviceId: 's1',
  serviceName: 'Corte',
  startsAt,
  endsAt: startsAt,
  status,
  price: 8500,
  currency: 'ARS',
  notes: null,
});

function setup(appointments: unknown[]) {
  server.use(
    http.post(`${API}/api/auth/refresh`, () => HttpResponse.json(sessionFixture)),
    http.get(`${API}/api/businesses/current`, () => HttpResponse.json(business)),
    http.get(`${API}/api/appointments`, () => HttpResponse.json(appointments)),
    http.get(`${API}/api/payments/daily`, () =>
      HttpResponse.json({ currency: 'ARS', total: 12500, byMethod: [], items: [] }),
    ),
    http.get(`${API}/api/cash-register`, () =>
      HttpResponse.json({
        date: today,
        currency: 'ARS',
        cashPayments: 0,
        manualNet: 0,
        expected: 4200,
        isClosed: false,
        closing: null,
        movements: [],
      }),
    ),
  );
}

describe('home page', () => {
  it('greets the user and summarizes today: appointments, collected money and cash', async () => {
    // Late in the day (23:00 local) so it is still "upcoming" whatever time the test runs.
    const late = `${addDay(today)}T02:00:00Z`;
    setup([
      appointment('a1', 'Scheduled', late, 'Ana Pérez'),
      appointment('a2', 'Completed', `${today}T12:00:00Z`, 'Beto Gómez'),
      appointment('a3', 'Cancelled', `${today}T13:00:00Z`, 'Carla Díaz'),
    ]);
    renderApp('/app');

    expect(
      await screen.findByRole('heading', { name: t.panel.home.greeting(sessionFixture.user.fullName) }),
    ).toBeInTheDocument();
    expect(await screen.findByText('3')).toBeInTheDocument();
    expect(screen.getByText(t.panel.home.pending(1))).toBeInTheDocument();
    expect(screen.getByText(/12\.500,00/)).toBeInTheDocument();
    expect(screen.getByText(/4\.200,00/)).toBeInTheDocument();
    expect(screen.getByText(/Ana Pérez/)).toBeInTheDocument();
    // Only scheduled ones are upcoming.
    expect(screen.queryByText(/Beto Gómez/)).not.toBeInTheDocument();
    expect(screen.queryByText(/Carla Díaz/)).not.toBeInTheDocument();
  });

  it('shows the public booking link of the business and copies it', async () => {
    setup([]);
    const writeText = vi.fn().mockResolvedValue(undefined);
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    renderApp('/app');

    const card = await screen.findByRole('region', { name: t.panel.home.bookingLink.title });
    expect(within(card).getByText(/\/r\/demo$/)).toBeInTheDocument();
    await userEvent.click(within(card).getByRole('button', { name: t.panel.home.bookingLink.copy }));

    expect(await within(card).findByText(t.panel.home.bookingLink.copied)).toBeInTheDocument();
    expect(writeText).toHaveBeenCalledWith(expect.stringMatching(/\/r\/demo$/));
  });

  it('says there is nothing left today and links to the main sections', async () => {
    setup([]);
    renderApp('/app');

    expect(await screen.findByText(t.panel.home.noUpcoming)).toBeInTheDocument();
    expect(within(screen.getByRole('main')).getByRole('link', { name: t.panel.nav.agenda })).toHaveAttribute(
      'href',
      '/app/agenda',
    );
    expect(screen.getByText(t.panel.home.pending(0))).toBeInTheDocument();
  });
});

/** The next calendar day as YYYY-MM-DD (the 02:00Z of tomorrow is 23:00 of today in Buenos Aires). */
function addDay(date: string): string {
  const next = new Date(`${date}T00:00:00Z`);
  next.setUTCDate(next.getUTCDate() + 1);
  return next.toISOString().slice(0, 10);
}
