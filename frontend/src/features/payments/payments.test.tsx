import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { t } from '@/shared/i18n';
import { renderApp } from '@/test/render';
import { API, problem, server, sessionFixture } from '@/test/server';

const business = {
  id: sessionFixture.user.businessId,
  name: 'Barbería Demo',
  slug: 'demo',
  timeZone: 'America/Argentina/Buenos_Aires',
  currency: 'ARS',
};

const appointmentId = '00000000-0000-7000-8000-0000000000d1';
const ana = '00000000-0000-7000-8000-0000000000c1';

const tenAm = {
  id: appointmentId,
  customerId: ana,
  customerName: 'Ana Pérez',
  professionalId: '00000000-0000-7000-8000-0000000000b1',
  professionalName: 'Lucas',
  serviceId: '00000000-0000-7000-8000-0000000000a1',
  serviceName: 'Corte',
  startsAt: '2026-11-18T13:00:00Z',
  endsAt: '2026-11-18T13:30:00Z',
  status: 'Scheduled',
  price: 8500,
  currency: 'ARS',
  notes: null,
};

const cash = {
  id: '00000000-0000-7000-8000-0000000000e1',
  appointmentId,
  amount: 3000,
  currency: 'ARS',
  method: 'Cash',
  reversesId: null,
  reason: null,
  createdAt: '2026-11-18T14:00:00Z',
};

function summary(payments: unknown[], paid: number, extra: object = {}) {
  return {
    appointmentId,
    price: 8500,
    currency: 'ARS',
    paid,
    balance: 8500 - paid,
    acceptsPayments: true,
    payments,
    ...extra,
  };
}

function setup(payments: ReturnType<typeof summary>, role: 'Owner' | 'Staff' = 'Owner') {
  server.use(
    http.post(`${API}/api/auth/refresh`, () =>
      HttpResponse.json({ ...sessionFixture, user: { ...sessionFixture.user, role } }),
    ),
    http.get(`${API}/api/businesses/current`, () => HttpResponse.json(business)),
    // The agenda only draws columns (and so appointments) when there is a professional.
    http.get(`${API}/api/professionals`, () =>
      HttpResponse.json([
        { id: tenAm.professionalId, name: 'Lucas', isActive: true, serviceIds: [tenAm.serviceId] },
      ]),
    ),
    http.get(`${API}/api/services`, () => HttpResponse.json([])),
    http.get(`${API}/api/appointments`, () => HttpResponse.json([tenAm])),
    http.get(`${API}/api/payments`, () => HttpResponse.json(payments)),
  );
}

async function openAppointment() {
  renderApp('/app/agenda?date=2026-11-18');
  await userEvent.click(
    await screen.findByRole('button', { name: t.agenda.appointmentLabel('Ana Pérez', '10:00') }),
  );
}

describe('appointment payments', () => {
  it('shows the balance and collects the pending amount by default', async () => {
    setup(summary([], 0));
    let sentBody: unknown;
    server.use(
      http.post(`${API}/api/payments`, async ({ request }) => {
        sentBody = await request.json();
        return HttpResponse.json(summary([cash], 8500), { status: 201 });
      }),
    );
    await openAppointment();

    expect(await screen.findByLabelText(t.payments.amount)).toHaveValue('8500');
    await userEvent.selectOptions(screen.getByLabelText(t.payments.method), 'Transfer');
    await userEvent.click(screen.getByRole('button', { name: t.payments.collect }));

    await expect.poll(() => sentBody).toEqual({ appointmentId, amount: 8500, method: 'Transfer' });
  });

  it('accepts amounts with decimal comma', async () => {
    setup(summary([], 0));
    let sentBody: unknown;
    server.use(
      http.post(`${API}/api/payments`, async ({ request }) => {
        sentBody = await request.json();
        return HttpResponse.json(summary([cash], 3000), { status: 201 });
      }),
    );
    await openAppointment();

    const amount = await screen.findByLabelText(t.payments.amount);
    await userEvent.clear(amount);
    await userEvent.type(amount, '1500,50');
    await userEvent.click(screen.getByRole('button', { name: t.payments.collect }));

    await expect.poll(() => sentBody).toEqual({ appointmentId, amount: 1500.5, method: 'Cash' });
  });

  it('rejects an invalid amount without calling the API', async () => {
    setup(summary([], 0));
    await openAppointment();

    const amount = await screen.findByLabelText(t.payments.amount);
    await userEvent.clear(amount);
    await userEvent.type(amount, 'abc');
    await userEvent.click(screen.getByRole('button', { name: t.payments.collect }));

    expect(await screen.findByText(t.payments.validation.amountInvalid)).toBeInTheDocument();
  });

  it('shows the API error when the amount exceeds the balance', async () => {
    setup(summary([cash], 3000));
    server.use(http.post(`${API}/api/payments`, () => problem(409, 'payment_exceeds_balance')));
    await openAppointment();

    await userEvent.click(await screen.findByRole('button', { name: t.payments.collect }));

    expect(await screen.findByText('El monto supera el saldo pendiente del turno.')).toBeInTheDocument();
  });

  it('lists payments and shows the paid state with no collect form once settled', async () => {
    setup(summary([cash, { ...cash, id: 'x2', amount: 5500, method: 'Card' }], 8500));
    await openAppointment();

    expect(await screen.findByText(t.payments.fullyPaid)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t.payments.collect })).not.toBeInTheDocument();
    expect(screen.getByText(new RegExp(t.payments.methods.Card))).toBeInTheDocument();
  });

  it('lets the owner reverse a payment with a reason, and only once', async () => {
    const reversal = {
      ...cash,
      id: '00000000-0000-7000-8000-0000000000e2',
      amount: -3000,
      reversesId: cash.id,
      reason: 'Cobro duplicado',
    };
    setup(summary([cash], 3000));
    let sentBody: unknown;
    server.use(
      http.post(`${API}/api/payments/${cash.id}/reversal`, async ({ request }) => {
        sentBody = await request.json();
        return HttpResponse.json(summary([cash, reversal], 0), { status: 201 });
      }),
    );
    await openAppointment();

    await userEvent.click(await screen.findByRole('button', { name: new RegExp(t.payments.reverse) }));
    await userEvent.click(screen.getByRole('button', { name: t.payments.confirmReverse }));
    expect(await screen.findByText(t.payments.validation.reasonRequired)).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText(t.payments.reasonLabel), 'Cobro duplicado');
    await userEvent.click(screen.getByRole('button', { name: t.payments.confirmReverse }));

    await expect.poll(() => sentBody).toEqual({ reason: 'Cobro duplicado' });
  });

  it('hides the reverse action from staff and from already reversed payments', async () => {
    setup(summary([cash], 3000), 'Staff');
    await openAppointment();

    await screen.findByText(t.payments.section);
    expect(screen.queryByRole('button', { name: new RegExp(t.payments.reverse) })).not.toBeInTheDocument();
  });

  it('explains that cancelled appointments cannot be paid', async () => {
    setup(summary([], 0, { acceptsPayments: false }));
    await openAppointment();

    expect(await screen.findByText(t.payments.notPayable)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t.payments.collect })).not.toBeInTheDocument();
  });
});

describe('payments page', () => {
  it('shows the total of the day, by method, and each movement in business time', async () => {
    server.use(
      http.post(`${API}/api/auth/refresh`, () => HttpResponse.json(sessionFixture)),
      http.get(`${API}/api/businesses/current`, () => HttpResponse.json(business)),
      http.get(`${API}/api/payments/daily`, () =>
        HttpResponse.json({
          currency: 'ARS',
          total: 3000,
          byMethod: [{ method: 'Cash', total: 3000 }],
          items: [{ payment: cash, customerName: 'Ana Pérez' }],
        }),
      ),
    );
    renderApp('/app/payments?date=2026-11-18');

    // The formatted amount contains a non-breaking space, so match on the digits.
    expect(await screen.findByText(/3\.000,00/, { selector: 'p' })).toBeInTheDocument();
    expect(screen.getByLabelText(t.agenda.dateLabel)).toHaveValue('18/11/2026');
    // 14:00Z is 11:00 in Buenos Aires.
    expect(screen.getByText(/11:00 · Ana Pérez · Efectivo/)).toBeInTheDocument();
  });

  it('says so when there was no money movement', async () => {
    server.use(
      http.post(`${API}/api/auth/refresh`, () => HttpResponse.json(sessionFixture)),
      http.get(`${API}/api/businesses/current`, () => HttpResponse.json(business)),
      http.get(`${API}/api/payments/daily`, () =>
        HttpResponse.json({ currency: 'ARS', total: 0, byMethod: [], items: [] }),
      ),
    );
    renderApp('/app/payments?date=2026-11-18');

    expect(await screen.findByText(t.payments.daily.empty)).toBeInTheDocument();
  });
});
