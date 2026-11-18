import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { t } from '@/shared/i18n';
import { todayIn } from '@/shared/time/business-time';
import { renderApp } from '@/test/render';
import { API, problem, server, sessionFixture } from '@/test/server';

const timeZone = 'America/Argentina/Buenos_Aires';
const business = {
  id: sessionFixture.user.businessId,
  name: 'Demo',
  slug: 'demo',
  timeZone,
  currency: 'ARS',
};
const today = todayIn(timeZone);

const income = {
  id: '00000000-0000-7000-8000-0000000000f1',
  kind: 'Income',
  amount: 1000,
  currency: 'ARS',
  concept: 'Cambio inicial',
  reversesId: null,
  reason: null,
  createdAt: `${today}T15:00:00Z`,
};

function day(extra: object = {}) {
  return {
    date: today,
    currency: 'ARS',
    cashPayments: 3000,
    manualNet: 1000,
    expected: 4000,
    isClosed: false,
    closing: null,
    movements: [income],
    ...extra,
  };
}

function setup(payload: object, role: 'Owner' | 'Staff' = 'Owner') {
  server.use(
    http.post(`${API}/api/auth/refresh`, () =>
      HttpResponse.json({ ...sessionFixture, user: { ...sessionFixture.user, role } }),
    ),
    http.get(`${API}/api/businesses/current`, () => HttpResponse.json(business)),
    http.get(`${API}/api/cash-register`, () => HttpResponse.json(payload)),
  );
}

describe('cash register page', () => {
  it('shows the expected cash and the manual movements', async () => {
    setup(day());
    renderApp(`/app/cash-register?date=${today}`);

    expect(await screen.findByText(/4\.000,00/, { selector: 'p' })).toBeInTheDocument();
    expect(screen.getByText(/Cambio inicial/)).toBeInTheDocument();
  });

  it('registers a movement with a decimal comma amount', async () => {
    setup(day({ movements: [], manualNet: 0, expected: 3000 }));
    let sentBody: unknown;
    server.use(
      http.post(`${API}/api/cash-register/movements`, async ({ request }) => {
        sentBody = await request.json();
        return HttpResponse.json(income, { status: 201 });
      }),
    );
    renderApp(`/app/cash-register?date=${today}`);

    await userEvent.selectOptions(await screen.findByLabelText(t.cash.form.kind), 'Expense');
    await userEvent.type(screen.getByLabelText(t.cash.form.amount), '250,50');
    await userEvent.type(screen.getByLabelText(t.cash.form.concept), 'Insumos');
    await userEvent.click(screen.getByRole('button', { name: t.cash.form.submit }));

    await expect.poll(() => sentBody).toEqual({ kind: 'Expense', amount: 250.5, concept: 'Insumos' });
  });

  it('validates the amount and the concept before calling the API', async () => {
    setup(day({ movements: [] }));
    renderApp(`/app/cash-register?date=${today}`);

    await userEvent.click(await screen.findByRole('button', { name: t.cash.form.submit }));
    expect(await screen.findByText(t.cash.validation.amountInvalid)).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText(t.cash.form.amount), '10');
    await userEvent.click(screen.getByRole('button', { name: t.cash.form.submit }));
    expect(await screen.findByText(t.cash.validation.conceptRequired)).toBeInTheDocument();
  });

  it('closes the day with the counted cash, accepting zero', async () => {
    setup(day());
    let sentBody: unknown;
    server.use(
      http.post(`${API}/api/cash-register/closings`, async ({ request }) => {
        sentBody = await request.json();
        return HttpResponse.json(day(), { status: 201 });
      }),
    );
    renderApp(`/app/cash-register?date=${today}`);

    await userEvent.type(await screen.findByLabelText(t.cash.closing.counted), '0');
    await userEvent.type(screen.getByLabelText(t.cash.closing.note), 'Sin efectivo');
    await userEvent.click(screen.getByRole('button', { name: t.cash.closing.submit }));

    await expect.poll(() => sentBody).toEqual({ date: today, countedAmount: 0, note: 'Sin efectivo' });
  });

  it('shows the frozen closing and no forms once the day is closed', async () => {
    setup(
      day({
        isClosed: true,
        closing: {
          id: 'c1',
          date: today,
          expected: 4000,
          counted: 3900,
          difference: -100,
          currency: 'ARS',
          note: 'Faltan 100',
          createdAt: `${today}T22:00:00Z`,
        },
      }),
    );
    renderApp(`/app/cash-register?date=${today}`);

    expect(await screen.findByText(t.cash.closing.short)).toBeInTheDocument();
    expect(screen.getByText('Faltan 100')).toBeInTheDocument();
    expect(screen.queryByLabelText(t.cash.form.amount)).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t.cash.closing.submit })).not.toBeInTheDocument();
  });

  it('lets only the owner reverse a movement, with a reason', async () => {
    setup(day());
    let sentBody: unknown;
    server.use(
      http.post(`${API}/api/cash-register/movements/${income.id}/reversal`, async ({ request }) => {
        sentBody = await request.json();
        return HttpResponse.json(
          { ...income, amount: -1000, reversesId: income.id, reason: 'Error' },
          { status: 201 },
        );
      }),
    );
    renderApp(`/app/cash-register?date=${today}`);

    await userEvent.click(await screen.findByRole('button', { name: new RegExp(t.payments.reverse) }));
    await userEvent.type(screen.getByLabelText(t.payments.reasonLabel), 'Error');
    await userEvent.click(screen.getByRole('button', { name: t.payments.confirmReverse }));

    await expect.poll(() => sentBody).toEqual({ reason: 'Error' });
  });

  it('hides the reverse action from staff', async () => {
    setup(day(), 'Staff');
    renderApp(`/app/cash-register?date=${today}`);

    await screen.findByText(/Cambio inicial/);
    expect(screen.queryByRole('button', { name: new RegExp(t.payments.reverse) })).not.toBeInTheDocument();
  });

  it('shows the API error when closing fails', async () => {
    setup(day());
    server.use(http.post(`${API}/api/cash-register/closings`, () => problem(409, 'cash_day_already_closed')));
    renderApp(`/app/cash-register?date=${today}`);

    await userEvent.type(await screen.findByLabelText(t.cash.closing.counted), '4000');
    await userEvent.click(screen.getByRole('button', { name: t.cash.closing.submit }));

    expect(await screen.findByText('Ese día ya tiene un cierre de caja.')).toBeInTheDocument();
  });

  it('does not offer manual movements on a past day', async () => {
    setup(day({ date: '2026-11-01' }));
    renderApp('/app/cash-register?date=2026-11-01');

    await screen.findByText(/Cambio inicial/);
    expect(screen.queryByLabelText(t.cash.form.amount)).not.toBeInTheDocument();
  });
});
