import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { t } from '@/shared/i18n';
import { renderApp } from '@/test/render';
import { API, problem, server } from '@/test/server';

const TOKEN = 'a'.repeat(64);
const PATH = `/r/demo/turno/${TOKEN}`;
const URL_BASE = `${API}/api/public/demo/appointments/${TOKEN}`;

const business = {
  name: 'Barbería Demo',
  slug: 'demo',
  timeZone: 'America/Argentina/Buenos_Aires',
  currency: 'ARS',
  phone: null,
};

// 10:00 Buenos Aires (UTC-3) on a Monday.
const appointment = {
  businessName: 'Barbería Demo',
  businessPhone: '+5491100009999',
  professionalId: 'p1',
  professionalName: 'Lucas',
  serviceId: 's1',
  serviceName: 'Corte',
  customerName: 'Ana Pérez',
  startsAt: '2030-01-07T13:00:00Z',
  endsAt: '2030-01-07T13:30:00Z',
  price: 8500,
  currency: 'ARS',
  status: 'Scheduled',
  canChange: true,
  changeDeadline: '2030-01-07T01:00:00Z',
  noticeHours: 12,
};

function setup(overrides: Partial<typeof appointment> = {}) {
  server.use(
    http.get(`${API}/api/public/demo/catalog`, () =>
      HttpResponse.json({ business, services: [], professionals: [] }),
    ),
    http.get(URL_BASE, () => HttpResponse.json({ ...appointment, ...overrides })),
  );
}

describe('manage appointment page', () => {
  it('shows the appointment with change and cancel options', async () => {
    setup();
    renderApp(PATH);

    expect(await screen.findByRole('heading', { name: t.booking.manage.title })).toBeInTheDocument();
    expect(screen.getByText('Corte')).toBeInTheDocument();
    expect(screen.getByText('Lucas')).toBeInTheDocument();
    expect(screen.getByText(/10:00/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: t.booking.manage.change })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: t.booking.manage.cancel })).toBeInTheDocument();
  });

  it('says the token is unknown for a bad link', async () => {
    server.use(
      http.get(`${API}/api/public/demo/catalog`, () =>
        HttpResponse.json({ business, services: [], professionals: [] }),
      ),
      http.get(URL_BASE, () => problem(404, 'appointment_not_found')),
    );
    renderApp(PATH);

    expect(await screen.findByRole('heading', { name: t.booking.manage.notFound.title })).toBeInTheDocument();
  });

  it('explains the notice window and shows the business phone when it is too late', async () => {
    setup({ canChange: false });
    renderApp(PATH);

    expect(await screen.findByText(new RegExp(t.booking.manage.tooLate(12)))).toBeInTheDocument();
    expect(screen.getByText(new RegExp('\\+5491100009999'))).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t.booking.manage.change })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t.booking.manage.cancel })).not.toBeInTheDocument();
  });

  it('moves the appointment to a new time after confirming', async () => {
    let sent: unknown;
    setup();
    server.use(
      http.get(`${URL_BASE}/availability`, () =>
        HttpResponse.json({
          date: '2030-01-08',
          isClosed: false,
          slots: [{ startsAt: '2030-01-08T14:00:00Z', minutes: 660 }],
        }),
      ),
      http.post(`${URL_BASE}/reschedule`, async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json({
          ...appointment,
          startsAt: '2030-01-08T14:00:00Z',
          endsAt: '2030-01-08T14:30:00Z',
        });
      }),
    );
    renderApp(PATH);

    await userEvent.click(await screen.findByRole('button', { name: t.booking.manage.change }));
    await userEvent.click(await screen.findByRole('button', { name: t.booking.time.slotLabel('11:00') }));
    const confirm = screen.getByRole('group', { name: t.booking.manage.confirmMoveTitle });
    await userEvent.click(within(confirm).getByRole('button', { name: t.booking.manage.confirmMoveYes }));

    expect(await screen.findByRole('status')).toHaveTextContent(t.booking.manage.moved);
    expect(sent).toEqual({ startsAt: '2030-01-08T14:00:00Z' });
    expect(screen.getByText(/11:00/)).toBeInTheDocument();
  });

  it('goes back to choosing a time when the chosen one was just taken', async () => {
    setup();
    server.use(
      http.get(`${URL_BASE}/availability`, () =>
        HttpResponse.json({
          date: '2030-01-08',
          isClosed: false,
          slots: [{ startsAt: '2030-01-08T14:00:00Z', minutes: 660 }],
        }),
      ),
      http.post(`${URL_BASE}/reschedule`, () => problem(409, 'slot_unavailable')),
    );
    renderApp(PATH);

    await userEvent.click(await screen.findByRole('button', { name: t.booking.manage.change }));
    await userEvent.click(await screen.findByRole('button', { name: t.booking.time.slotLabel('11:00') }));
    await userEvent.click(screen.getByRole('button', { name: t.booking.manage.confirmMoveYes }));

    expect(await screen.findByRole('alert')).toHaveTextContent(t.errors.codes.slot_unavailable!);
    expect(screen.getByRole('button', { name: t.booking.time.slotLabel('11:00') })).toBeInTheDocument();
  });

  it('cancels only after confirming', async () => {
    let cancelled = false;
    setup();
    server.use(
      http.post(`${URL_BASE}/cancel`, () => {
        cancelled = true;
        return HttpResponse.json({ ...appointment, status: 'Cancelled', canChange: false });
      }),
    );
    renderApp(PATH);

    await userEvent.click(await screen.findByRole('button', { name: t.booking.manage.cancel }));
    expect(cancelled).toBe(false);
    await userEvent.click(screen.getByRole('button', { name: t.booking.manage.confirmCancelYes }));

    expect(await screen.findByRole('status')).toHaveTextContent(t.booking.manage.cancelled);
    expect(cancelled).toBe(true);
    expect(screen.queryByRole('button', { name: t.booking.manage.change })).not.toBeInTheDocument();
  });

  it('keeps the appointment when the customer backs out of cancelling', async () => {
    setup();
    renderApp(PATH);

    await userEvent.click(await screen.findByRole('button', { name: t.booking.manage.cancel }));
    await userEvent.click(screen.getByRole('button', { name: t.booking.manage.keep }));

    expect(screen.getByRole('button', { name: t.booking.manage.change })).toBeInTheDocument();
  });
});
