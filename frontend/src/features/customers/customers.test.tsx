import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { t } from '@/shared/i18n';
import { renderApp } from '@/test/render';
import { API, problem, server, sessionFixture } from '@/test/server';

const ana = {
  id: '00000000-0000-7000-8000-0000000000c1',
  name: 'Ana Pérez',
  phone: '+5491155551234',
  email: 'ana@mail.com',
  notes: 'Prefiere turnos a la tarde',
  isArchived: false,
};

const page = (items: unknown[], total = items.length) => ({ items, total, page: 1, pageSize: 25 });

function loginAs(role: 'Owner' | 'Staff') {
  server.use(
    http.post(`${API}/api/auth/refresh`, () =>
      HttpResponse.json({ ...sessionFixture, user: { ...sessionFixture.user, role } }),
    ),
  );
}

describe('customers page', () => {
  it('lists the customers of the business', async () => {
    loginAs('Staff');
    server.use(http.get(`${API}/api/customers`, () => HttpResponse.json(page([ana]))));
    renderApp('/app/customers');

    expect(await screen.findByText('Ana Pérez')).toBeInTheDocument();
    expect(screen.getByText(/\+5491155551234/)).toBeInTheDocument();
    expect(screen.getByText(t.customers.total(1))).toBeInTheDocument();
  });

  it('lets staff create customers (not only owners)', async () => {
    loginAs('Staff');
    server.use(http.get(`${API}/api/customers`, () => HttpResponse.json(page([]))));
    renderApp('/app/customers');

    expect(await screen.findByRole('button', { name: t.customers.new })).toBeInTheDocument();
  });

  it('validates the form in Spanish', async () => {
    loginAs('Owner');
    server.use(http.get(`${API}/api/customers`, () => HttpResponse.json(page([]))));
    renderApp('/app/customers');

    await userEvent.click(await screen.findByRole('button', { name: t.customers.new }));
    await userEvent.type(screen.getByLabelText(t.customers.form.phone), 'abc');
    await userEvent.type(screen.getByLabelText(t.customers.form.email), 'no-es-mail');
    await userEvent.click(screen.getByRole('button', { name: t.customers.form.save }));

    expect(await screen.findByText(t.customers.validation.nameRequired)).toBeInTheDocument();
    expect(screen.getByText(t.customers.validation.phoneInvalid)).toBeInTheDocument();
    expect(screen.getByText(t.customers.validation.emailInvalid)).toBeInTheDocument();
  });

  it('creates a customer and refreshes the list', async () => {
    loginAs('Owner');
    let items: unknown[] = [];
    let sentBody: unknown;
    server.use(
      http.get(`${API}/api/customers`, () => HttpResponse.json(page(items))),
      http.post(`${API}/api/customers`, async ({ request }) => {
        sentBody = await request.json();
        items = [ana];
        return HttpResponse.json(ana, { status: 201 });
      }),
    );
    renderApp('/app/customers');

    await userEvent.click(await screen.findByRole('button', { name: t.customers.new }));
    await userEvent.type(screen.getByLabelText(t.customers.form.name), 'Ana Pérez');
    await userEvent.type(screen.getByLabelText(t.customers.form.phone), '11 5555-1234');
    await userEvent.click(screen.getByRole('button', { name: t.customers.form.save }));

    expect(await screen.findByText('Ana Pérez')).toBeInTheDocument();
    expect(sentBody).toEqual({ name: 'Ana Pérez', phone: '11 5555-1234', email: null, notes: null });
    expect(screen.queryByRole('form')).not.toBeInTheDocument();
  });

  it('shows the duplicate phone error from the API', async () => {
    loginAs('Owner');
    server.use(
      http.get(`${API}/api/customers`, () => HttpResponse.json(page([]))),
      http.post(`${API}/api/customers`, () => problem(409, 'customer_phone_taken')),
    );
    renderApp('/app/customers');

    await userEvent.click(await screen.findByRole('button', { name: t.customers.new }));
    await userEvent.type(screen.getByLabelText(t.customers.form.name), 'Ana Pérez');
    await userEvent.click(screen.getByRole('button', { name: t.customers.form.save }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Ya hay un cliente activo con ese teléfono.');
  });

  it('sends the search term to the API', async () => {
    loginAs('Owner');
    const searches: (string | null)[] = [];
    server.use(
      http.get(`${API}/api/customers`, ({ request }) => {
        searches.push(new URL(request.url).searchParams.get('search'));
        return HttpResponse.json(page([ana]));
      }),
    );
    renderApp('/app/customers');

    await screen.findByText('Ana Pérez');
    await userEvent.type(screen.getByLabelText(t.customers.searchLabel), 'ana');

    await expect.poll(() => searches.at(-1)).toBe('ana');
  });

  it('hides delete for non-owners', async () => {
    loginAs('Staff');
    server.use(http.get(`${API}/api/customers`, () => HttpResponse.json(page([ana]))));
    renderApp('/app/customers');

    await screen.findByText('Ana Pérez');
    expect(screen.queryByRole('button', { name: t.customers.remove('Ana Pérez') })).not.toBeInTheDocument();
  });

  it('deletes a customer after confirming', async () => {
    loginAs('Owner');
    let items: unknown[] = [ana];
    server.use(
      http.get(`${API}/api/customers`, () => HttpResponse.json(page(items))),
      http.delete(`${API}/api/customers/${ana.id}`, () => {
        items = [];
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderApp('/app/customers');

    await userEvent.click(await screen.findByRole('button', { name: t.customers.remove('Ana Pérez') }));
    await userEvent.click(screen.getByRole('button', { name: t.customers.confirmYes }));

    expect(await screen.findByText(t.customers.empty)).toBeInTheDocument();
  });

  it('shows the history error when the API refuses to delete', async () => {
    loginAs('Owner');
    server.use(
      http.get(`${API}/api/customers`, () => HttpResponse.json(page([ana]))),
      http.delete(`${API}/api/customers/${ana.id}`, () => problem(409, 'customer_has_history')),
    );
    renderApp('/app/customers');

    await userEvent.click(await screen.findByRole('button', { name: t.customers.remove('Ana Pérez') }));
    await userEvent.click(screen.getByRole('button', { name: t.customers.confirmYes }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Archivalo en vez de eliminarlo');
  });

  it('archives a customer keeping the rest of its data', async () => {
    loginAs('Owner');
    let sentBody: unknown;
    server.use(
      http.get(`${API}/api/customers`, () => HttpResponse.json(page([ana]))),
      http.put(`${API}/api/customers/${ana.id}`, async ({ request }) => {
        sentBody = await request.json();
        return HttpResponse.json({ ...ana, isArchived: true });
      }),
    );
    renderApp('/app/customers');

    await userEvent.click(await screen.findByRole('button', { name: t.customers.archive('Ana Pérez') }));

    await expect
      .poll(() => sentBody)
      .toEqual({
        name: 'Ana Pérez',
        phone: ana.phone,
        email: ana.email,
        notes: ana.notes,
        isArchived: true,
      });
  });
});
