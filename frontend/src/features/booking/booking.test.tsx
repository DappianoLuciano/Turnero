import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { t } from '@/shared/i18n';
import { renderApp } from '@/test/render';
import { API, problem, server } from '@/test/server';

const STORAGE_KEY = 'turnero.booking.customer';

const cut = { id: 's1', name: 'Corte', durationMinutes: 30, price: 8500, currency: 'ARS' };
const beard = { id: 's2', name: 'Barba', durationMinutes: 20, price: 5000, currency: 'ARS' };
const lucas = { id: 'p1', name: 'Lucas', serviceIds: [cut.id, beard.id] };
const mateo = { id: 'p2', name: 'Mateo', serviceIds: [cut.id] };

const business = {
  name: 'Barbería Demo',
  slug: 'demo',
  timeZone: 'America/Argentina/Buenos_Aires',
  currency: 'ARS',
};

// Instants whose Buenos Aires wall clock is 10:00 and 11:30 (UTC-3).
const tenAm = { startsAt: '2030-01-07T13:00:00Z', minutes: 600 };
const elevenThirty = { startsAt: '2030-01-07T14:30:00Z', minutes: 690 };

const confirmation = {
  appointmentId: 'a1',
  businessName: business.name,
  serviceName: 'Corte',
  professionalName: 'Lucas',
  customerName: 'Ana Pérez',
  startsAt: tenAm.startsAt,
  endsAt: '2030-01-07T13:30:00Z',
  price: 8500,
  currency: 'ARS',
};

function setup(
  options: { services?: object[]; professionals?: object[]; slots?: Record<string, object[]> } = {},
) {
  const {
    services = [cut, beard],
    professionals = [lucas, mateo],
    slots = { p1: [tenAm], p2: [tenAm, elevenThirty] },
  } = options;
  server.use(
    http.get(`${API}/api/public/demo/catalog`, () =>
      HttpResponse.json({ business, services, professionals }),
    ),
    http.get(`${API}/api/public/demo/availability`, ({ request }) => {
      const professionalId = new URL(request.url).searchParams.get('professionalId') ?? '';
      return HttpResponse.json({ date: '2030-01-07', isClosed: false, slots: slots[professionalId] ?? [] });
    }),
  );
}

function captureBooking(
  response: () => Response | Promise<Response> = () => HttpResponse.json(confirmation, { status: 201 }),
) {
  const sent: { body?: unknown } = {};
  server.use(
    http.post(`${API}/api/public/demo/appointments`, async ({ request }) => {
      sent.body = await request.json();
      return response();
    }),
  );
  return sent;
}

beforeEach(() => localStorage.clear());
afterEach(() => vi.restoreAllMocks());

describe('public booking site', () => {
  it('needs no login: lists the services of the business with duration and price', async () => {
    setup();
    renderApp('/r/demo');

    expect(await screen.findByRole('heading', { name: business.name })).toBeInTheDocument();
    expect(screen.getByText(t.booking.step(1, 4))).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Corte/ })).toHaveTextContent('30 min');
    expect(screen.getByRole('button', { name: /Barba/ })).toHaveTextContent(/5\.000,00/);
  });

  it('says the business does not exist for an unknown link', async () => {
    server.use(http.get(`${API}/api/public/nada/catalog`, () => problem(404, 'business_not_found')));
    renderApp('/r/nada');

    expect(await screen.findByRole('heading', { name: t.booking.notFound.title })).toBeInTheDocument();
  });

  it('says there is nothing to book when the business offers nothing yet', async () => {
    setup({ services: [], professionals: [] });
    renderApp('/r/demo');

    expect(await screen.findByText(t.booking.nothingToBook)).toBeInTheDocument();
  });

  it('only offers the professionals who perform the chosen service, plus "any available"', async () => {
    setup();
    renderApp('/r/demo');

    await userEvent.click(await screen.findByRole('button', { name: /Barba/ }));

    // Only Lucas performs the beard: nothing to choose, so it goes straight to the time.
    expect(await screen.findByRole('heading', { name: t.booking.steps.time })).toBeInTheDocument();
  });

  it('lets the customer choose between professionals when several perform the service', async () => {
    setup();
    renderApp('/r/demo');

    await userEvent.click(await screen.findByRole('button', { name: /Corte/ }));

    expect(
      await screen.findByRole('button', { name: new RegExp(t.booking.anyProfessional) }),
    ).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Lucas' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Mateo' })).toBeInTheDocument();
  });

  it('books a time end to end without an account and remembers the customer on this device', async () => {
    setup();
    const sent = captureBooking();
    renderApp('/r/demo');

    await userEvent.click(await screen.findByRole('button', { name: /Corte/ }));
    await userEvent.click(await screen.findByRole('button', { name: 'Lucas' }));
    await userEvent.click(await screen.findByRole('button', { name: t.booking.time.slotLabel('10:00') }));
    await userEvent.type(await screen.findByLabelText(t.booking.details.name), 'Ana Pérez');
    await userEvent.type(screen.getByLabelText(t.booking.details.phone), '11 5555-1234');
    await userEvent.click(screen.getByRole('button', { name: t.booking.details.confirm }));

    expect(await screen.findByRole('heading', { name: t.booking.confirmed.title })).toBeInTheDocument();
    expect(sent.body).toEqual({
      professionalId: 'p1',
      serviceId: 's1',
      startsAt: tenAm.startsAt,
      customerName: 'Ana Pérez',
      customerPhone: '11 5555-1234',
    });
    // 13:00Z is 10:00 in the business's zone, written as day/month/year.
    expect(screen.getByText(/07\/01\/2030 · 10:00/)).toBeInTheDocument();
    expect(screen.getByText(t.booking.confirmed.payAtPlace)).toBeInTheDocument();
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY) ?? 'null')).toEqual({
      name: 'Ana Pérez',
      phone: '11 5555-1234',
    });
  });

  it('greets a returning customer and fills in their data, which they can erase', async () => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ name: 'Ana Pérez', phone: '1155551234' }));
    setup({ services: [beard], professionals: [lucas] });
    renderApp('/r/demo');

    expect(await screen.findByText(t.booking.welcomeBack('Ana'))).toBeInTheDocument();
    await userEvent.click(await screen.findByRole('button', { name: /Barba/ }));
    await userEvent.click(await screen.findByRole('button', { name: t.booking.time.slotLabel('10:00') }));

    expect(await screen.findByLabelText(t.booking.details.name)).toHaveValue('Ana Pérez');
    expect(screen.getByLabelText(t.booking.details.phone)).toHaveValue('1155551234');

    await userEvent.click(screen.getByRole('button', { name: t.booking.details.forget }));

    expect(screen.getByLabelText(t.booking.details.name)).toHaveValue('');
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
    expect(screen.queryByText(t.booking.welcomeBack('Ana'))).not.toBeInTheDocument();
  });

  it('works when the browser storage is unavailable', async () => {
    vi.spyOn(Storage.prototype, 'getItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    vi.spyOn(Storage.prototype, 'setItem').mockImplementation(() => {
      throw new Error('blocked');
    });
    setup({ services: [beard], professionals: [lucas] });
    captureBooking();
    renderApp('/r/demo');

    await userEvent.click(await screen.findByRole('button', { name: /Barba/ }));
    await userEvent.click(await screen.findByRole('button', { name: t.booking.time.slotLabel('10:00') }));
    await userEvent.type(await screen.findByLabelText(t.booking.details.name), 'Ana Pérez');
    await userEvent.type(screen.getByLabelText(t.booking.details.phone), '1155551234');
    await userEvent.click(screen.getByRole('button', { name: t.booking.details.confirm }));

    expect(await screen.findByRole('heading', { name: t.booking.confirmed.title })).toBeInTheDocument();
  });

  it('asks for a name and a valid phone before calling the API', async () => {
    setup({ services: [beard], professionals: [lucas] });
    const sent = captureBooking();
    renderApp('/r/demo');

    await userEvent.click(await screen.findByRole('button', { name: /Barba/ }));
    await userEvent.click(await screen.findByRole('button', { name: t.booking.time.slotLabel('10:00') }));
    await userEvent.type(await screen.findByLabelText(t.booking.details.phone), '12');
    await userEvent.click(screen.getByRole('button', { name: t.booking.details.confirm }));

    expect(await screen.findByText(t.booking.details.nameRequired)).toBeInTheDocument();
    expect(screen.getByText(t.booking.details.phoneInvalid)).toBeInTheDocument();
    expect(sent.body).toBeUndefined();
  });

  it('with "any available" merges the times and books the professional who has that time', async () => {
    setup();
    const sent = captureBooking();
    renderApp('/r/demo');

    await userEvent.click(await screen.findByRole('button', { name: /Corte/ }));
    await userEvent.click(await screen.findByRole('button', { name: new RegExp(t.booking.anyProfessional) }));

    // 10:00 is free for both, 11:30 only for Mateo: each time appears once.
    expect(await screen.findAllByRole('button', { name: t.booking.time.slotLabel('10:00') })).toHaveLength(1);
    await userEvent.click(screen.getByRole('button', { name: t.booking.time.slotLabel('11:30') }));
    await userEvent.type(await screen.findByLabelText(t.booking.details.name), 'Ana Pérez');
    await userEvent.type(screen.getByLabelText(t.booking.details.phone), '1155551234');
    await userEvent.click(screen.getByRole('button', { name: t.booking.details.confirm }));

    await screen.findByRole('heading', { name: t.booking.confirmed.title });
    expect(sent.body).toMatchObject({ professionalId: 'p2', startsAt: elevenThirty.startsAt });
  });

  it('goes back to the times with a notice when the time was just taken', async () => {
    setup({ services: [beard], professionals: [lucas] });
    captureBooking(() => problem(409, 'slot_unavailable'));
    renderApp('/r/demo');

    await userEvent.click(await screen.findByRole('button', { name: /Barba/ }));
    await userEvent.click(await screen.findByRole('button', { name: t.booking.time.slotLabel('10:00') }));
    await userEvent.type(await screen.findByLabelText(t.booking.details.name), 'Ana Pérez');
    await userEvent.type(screen.getByLabelText(t.booking.details.phone), '1155551234');
    await userEvent.click(screen.getByRole('button', { name: t.booking.details.confirm }));

    expect(await screen.findByText(t.booking.unavailable)).toBeInTheDocument();
    expect(await screen.findByRole('heading', { name: t.booking.steps.time })).toBeInTheDocument();
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('shows a message when the chosen day has no times, and when it is closed', async () => {
    server.use(
      http.get(`${API}/api/public/demo/catalog`, () =>
        HttpResponse.json({ business, services: [beard], professionals: [lucas] }),
      ),
      http.get(`${API}/api/public/demo/availability`, () =>
        HttpResponse.json({ date: '2030-01-07', isClosed: true, slots: [] }),
      ),
    );
    renderApp('/r/demo');

    await userEvent.click(await screen.findByRole('button', { name: /Barba/ }));

    expect(await screen.findByText(t.booking.time.closed)).toBeInTheDocument();
  });

  it('can go back a step and book another time after confirming', async () => {
    setup({ services: [beard], professionals: [lucas] });
    captureBooking();
    renderApp('/r/demo');

    await userEvent.click(await screen.findByRole('button', { name: /Barba/ }));
    await userEvent.click(await screen.findByRole('button', { name: t.booking.time.slotLabel('10:00') }));
    await userEvent.click(await screen.findByRole('button', { name: t.booking.back }));
    expect(await screen.findByRole('heading', { name: t.booking.steps.time })).toBeInTheDocument();

    await userEvent.click(screen.getByRole('button', { name: t.booking.time.slotLabel('10:00') }));
    await userEvent.type(await screen.findByLabelText(t.booking.details.name), 'Ana Pérez');
    await userEvent.type(screen.getByLabelText(t.booking.details.phone), '1155551234');
    await userEvent.click(screen.getByRole('button', { name: t.booking.details.confirm }));
    await userEvent.click(await screen.findByRole('button', { name: t.booking.confirmed.another }));

    expect(await screen.findByText(t.booking.step(1, 4))).toBeInTheDocument();
  });
});
