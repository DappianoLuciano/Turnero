import { screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { t } from '@/shared/i18n';
import { renderApp } from '@/test/render';
import { API, problem, server, sessionFixture } from '@/test/server';

const haircut = {
  id: '00000000-0000-7000-8000-0000000000a1',
  name: 'Corte',
  description: 'Clásico',
  durationMinutes: 30,
  price: 8500,
  currency: 'ARS',
  isActive: true,
};

function loginAs(role: 'Owner' | 'Staff') {
  server.use(
    http.post(`${API}/api/auth/refresh`, () =>
      HttpResponse.json({ ...sessionFixture, user: { ...sessionFixture.user, role } }),
    ),
  );
}

describe('services page', () => {
  it('lists the services of the business', async () => {
    loginAs('Owner');
    server.use(http.get(`${API}/api/services`, () => HttpResponse.json([haircut])));
    renderApp('/app/services');

    expect(await screen.findByText('Corte')).toBeInTheDocument();
    expect(screen.getByText('Clásico')).toBeInTheDocument();
    expect(screen.getByText(/30 min/)).toBeInTheDocument();
  });

  it('hides edit actions for non-owners', async () => {
    loginAs('Staff');
    server.use(http.get(`${API}/api/services`, () => HttpResponse.json([haircut])));
    renderApp('/app/services');

    await screen.findByText('Corte');
    expect(screen.queryByRole('button', { name: t.services.new })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t.services.edit('Corte') })).not.toBeInTheDocument();
  });

  it('validates the form in Spanish', async () => {
    loginAs('Owner');
    server.use(http.get(`${API}/api/services`, () => HttpResponse.json([])));
    renderApp('/app/services');

    await userEvent.click(await screen.findByRole('button', { name: t.services.new }));
    await userEvent.click(screen.getByRole('button', { name: t.services.form.save }));

    expect(await screen.findByText(t.services.validation.nameRequired)).toBeInTheDocument();
  });

  it('creates a service and refreshes the list', async () => {
    loginAs('Owner');
    let items: unknown[] = [];
    let sentBody: unknown;
    server.use(
      http.get(`${API}/api/services`, () => HttpResponse.json(items)),
      http.post(`${API}/api/services`, async ({ request }) => {
        sentBody = await request.json();
        items = [haircut];
        return HttpResponse.json(haircut, { status: 201 });
      }),
    );
    renderApp('/app/services');

    await userEvent.click(await screen.findByRole('button', { name: t.services.new }));
    const form = screen.getByRole('form', { name: t.services.form.createTitle });
    await userEvent.type(within(form).getByLabelText(t.services.form.name), 'Corte');
    await userEvent.clear(within(form).getByLabelText(t.services.form.price));
    await userEvent.type(within(form).getByLabelText(t.services.form.price), '8500');
    await userEvent.click(within(form).getByRole('button', { name: t.services.form.save }));

    expect(await screen.findByText(/30 min/)).toBeInTheDocument();
    expect(sentBody).toEqual({ name: 'Corte', description: null, durationMinutes: 30, price: 8500 });
    expect(screen.queryByRole('form')).not.toBeInTheDocument();
  });

  it('edits a service and deactivates it', async () => {
    loginAs('Owner');
    let sentBody: unknown;
    server.use(
      http.get(`${API}/api/services`, () => HttpResponse.json([haircut])),
      http.put(`${API}/api/services/${haircut.id}`, async ({ request }) => {
        sentBody = await request.json();
        return HttpResponse.json(haircut);
      }),
    );
    renderApp('/app/services');

    await userEvent.click(await screen.findByRole('button', { name: t.services.edit('Corte') }));
    await userEvent.click(screen.getByLabelText(t.services.form.active));
    await userEvent.click(screen.getByRole('button', { name: t.services.form.save }));

    await screen.findByText('Corte');
    expect(sentBody).toEqual({
      name: 'Corte',
      description: 'Clásico',
      durationMinutes: 30,
      price: 8500,
      isActive: false,
    });
  });

  it('shows the localized API error', async () => {
    loginAs('Owner');
    server.use(
      http.get(`${API}/api/services`, () => HttpResponse.json([haircut])),
      http.put(`${API}/api/services/${haircut.id}`, () => problem(404, 'service_not_found')),
    );
    renderApp('/app/services');

    await userEvent.click(await screen.findByRole('button', { name: t.services.edit('Corte') }));
    await userEvent.click(screen.getByRole('button', { name: t.services.form.save }));

    expect(await screen.findByRole('alert')).toHaveTextContent(t.errors.codes.service_not_found!);
  });

  it('deletes a service after confirming', async () => {
    loginAs('Owner');
    let items = [haircut];
    let deleted = false;
    server.use(
      http.get(`${API}/api/services`, () => HttpResponse.json(items)),
      http.delete(`${API}/api/services/${haircut.id}`, () => {
        deleted = true;
        items = [];
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderApp('/app/services');

    await userEvent.click(await screen.findByRole('button', { name: t.services.remove('Corte') }));
    expect(deleted).toBe(false);
    await userEvent.click(screen.getByRole('button', { name: t.services.confirmYes }));

    expect(await screen.findByText(t.services.emptyOwner)).toBeInTheDocument();
    expect(deleted).toBe(true);
  });

  it('does not delete when the confirmation is cancelled', async () => {
    loginAs('Owner');
    server.use(http.get(`${API}/api/services`, () => HttpResponse.json([haircut])));
    renderApp('/app/services');

    await userEvent.click(await screen.findByRole('button', { name: t.services.remove('Corte') }));
    await userEvent.click(screen.getByRole('button', { name: t.services.confirmNo }));

    expect(screen.getByRole('button', { name: t.services.remove('Corte') })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t.services.confirmYes })).not.toBeInTheDocument();
  });
});
