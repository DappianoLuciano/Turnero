import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { t } from '@/shared/i18n';
import { renderApp } from '@/test/render';
import { API, problem, server, sessionFixture } from '@/test/server';

const cut = {
  id: '00000000-0000-7000-8000-0000000000a1',
  name: 'Corte',
  description: null,
  durationMinutes: 30,
  price: 8500,
  currency: 'ARS',
  isActive: true,
};
const beard = { ...cut, id: '00000000-0000-7000-8000-0000000000a2', name: 'Barba' };

const lucas = {
  id: '00000000-0000-7000-8000-0000000000b1',
  name: 'Lucas',
  isActive: true,
  serviceIds: [cut.id],
};

function loginAs(role: 'Owner' | 'Staff') {
  server.use(
    http.post(`${API}/api/auth/refresh`, () =>
      HttpResponse.json({ ...sessionFixture, user: { ...sessionFixture.user, role } }),
    ),
    http.get(`${API}/api/services`, () => HttpResponse.json([cut, beard])),
  );
}

describe('staff page', () => {
  it('lists professionals with the names of their services', async () => {
    loginAs('Owner');
    server.use(http.get(`${API}/api/professionals`, () => HttpResponse.json([lucas])));
    renderApp('/app/staff');

    expect(await screen.findByText('Lucas')).toBeInTheDocument();
    expect(await screen.findByText('Corte')).toBeInTheDocument();
  });

  it('hides edit actions for non-owners', async () => {
    loginAs('Staff');
    server.use(http.get(`${API}/api/professionals`, () => HttpResponse.json([lucas])));
    renderApp('/app/staff');

    await screen.findByText('Lucas');
    expect(screen.queryByRole('button', { name: t.staff.new })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t.staff.edit('Lucas') })).not.toBeInTheDocument();
  });

  it('validates the name in Spanish', async () => {
    loginAs('Owner');
    server.use(http.get(`${API}/api/professionals`, () => HttpResponse.json([])));
    renderApp('/app/staff');

    await userEvent.click(await screen.findByRole('button', { name: t.staff.new }));
    await userEvent.click(screen.getByRole('button', { name: t.staff.form.save }));

    expect(await screen.findByText(t.staff.validation.nameRequired)).toBeInTheDocument();
  });

  it('creates a professional with the selected services', async () => {
    loginAs('Owner');
    let items: unknown[] = [];
    let sentBody: unknown;
    server.use(
      http.get(`${API}/api/professionals`, () => HttpResponse.json(items)),
      http.post(`${API}/api/professionals`, async ({ request }) => {
        sentBody = await request.json();
        items = [lucas];
        return HttpResponse.json(lucas, { status: 201 });
      }),
    );
    renderApp('/app/staff');

    await userEvent.click(await screen.findByRole('button', { name: t.staff.new }));
    await userEvent.type(screen.getByLabelText(t.staff.form.name), 'Lucas');
    await userEvent.click(await screen.findByRole('checkbox', { name: 'Barba' }));
    await userEvent.click(screen.getByRole('button', { name: t.staff.form.save }));

    expect(await screen.findByText('Lucas')).toBeInTheDocument();
    expect(sentBody).toEqual({ name: 'Lucas', serviceIds: [beard.id] });
  });

  it('deletes a professional after confirming and shows the history error', async () => {
    loginAs('Owner');
    server.use(
      http.get(`${API}/api/professionals`, () => HttpResponse.json([lucas])),
      http.delete(`${API}/api/professionals/${lucas.id}`, () => problem(409, 'professional_has_history')),
    );
    renderApp('/app/staff');

    await userEvent.click(await screen.findByRole('button', { name: t.staff.remove('Lucas') }));
    await userEvent.click(screen.getByRole('button', { name: t.staff.confirmYes }));

    expect(await screen.findByRole('alert')).toHaveTextContent('Desactivalo en vez de eliminarlo');
  });
});
