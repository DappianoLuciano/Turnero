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

const cut = {
  id: '00000000-0000-7000-8000-0000000000a1',
  name: 'Corte',
  description: null,
  durationMinutes: 30,
  price: 8500,
  currency: 'ARS',
  isActive: true,
};

const lucas = {
  id: '00000000-0000-7000-8000-0000000000b1',
  name: 'Lucas',
  isActive: true,
  serviceIds: [cut.id],
};
const mateo = {
  id: '00000000-0000-7000-8000-0000000000b2',
  name: 'Mateo',
  isActive: true,
  serviceIds: [cut.id],
};

const ana = {
  id: '00000000-0000-7000-8000-0000000000c1',
  name: 'Ana Pérez',
  phone: '+5491155551234',
  email: null,
  notes: null,
  isArchived: false,
};

// 10:00 in Buenos Aires (UTC-3).
const tenAm = {
  id: '00000000-0000-7000-8000-0000000000d1',
  customerId: ana.id,
  customerName: ana.name,
  professionalId: lucas.id,
  professionalName: lucas.name,
  serviceId: cut.id,
  serviceName: cut.name,
  startsAt: '2026-11-18T13:00:00Z',
  endsAt: '2026-11-18T13:30:00Z',
  status: 'Scheduled',
  price: 8500,
  currency: 'ARS',
  notes: null,
};

function setup(appointments: unknown[] = [], professionals: unknown[] = [lucas, mateo]) {
  server.use(
    http.post(`${API}/api/auth/refresh`, () => HttpResponse.json(sessionFixture)),
    http.get(`${API}/api/businesses/current`, () => HttpResponse.json(business)),
    http.get(`${API}/api/professionals`, () => HttpResponse.json(professionals)),
    http.get(`${API}/api/services`, () => HttpResponse.json([cut])),
    http.get(`${API}/api/appointments`, () => HttpResponse.json(appointments)),
    http.get(`${API}/api/payments`, () =>
      HttpResponse.json({
        appointmentId: tenAm.id,
        price: 8500,
        currency: 'ARS',
        paid: 0,
        balance: 8500,
        acceptsPayments: true,
        payments: [],
      }),
    ),
    http.get(`${API}/api/customers`, () =>
      HttpResponse.json({ items: [ana], total: 1, page: 1, pageSize: 25 }),
    ),
  );
}

const DAY = '/app/agenda?date=2026-11-18';

describe('agenda page', () => {
  it('shows a column per professional and places appointments at the business local time', async () => {
    setup([tenAm]);
    renderApp(DAY);

    expect(
      await screen.findByRole('button', { name: t.agenda.appointmentLabel('Ana Pérez', '10:00') }),
    ).toBeVisible();
    expect(screen.getByText('Lucas')).toBeInTheDocument();
    expect(screen.getByText('Mateo')).toBeInTheDocument();
  });

  it('asks to create professionals first when there are none', async () => {
    setup([], []);
    renderApp(DAY);

    expect(await screen.findByText(t.agenda.noProfessionals)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t.agenda.new })).not.toBeInTheDocument();
  });

  it('books an appointment from a grid slot, sending the UTC instant of the local time', async () => {
    setup();
    let sentBody: unknown;
    server.use(
      http.post(`${API}/api/appointments`, async ({ request }) => {
        sentBody = await request.json();
        return HttpResponse.json(tenAm, { status: 201 });
      }),
    );
    renderApp(DAY);

    await userEvent.click(await screen.findByRole('button', { name: t.agenda.slotLabel('Lucas', '10:00') }));
    expect(screen.getByLabelText(t.agenda.form.time)).toHaveValue('10:00');
    expect(screen.getByLabelText(t.agenda.form.professional)).toHaveValue(lucas.id);

    await userEvent.click(await screen.findByRole('button', { name: /Ana Pérez/ }));
    await userEvent.selectOptions(screen.getByLabelText(t.agenda.form.service), cut.id);
    await userEvent.click(screen.getByRole('button', { name: t.agenda.form.save }));

    await expect
      .poll(() => sentBody)
      .toEqual({
        customerId: ana.id,
        professionalId: lucas.id,
        serviceId: cut.id,
        startsAt: '2026-11-18T13:00:00.000Z',
        notes: null,
      });
  });

  it('shades what is outside the working hours of each professional and marks closed days', async () => {
    setup();
    // 2026-11-18 is a Wednesday: Lucas works 09:00-13:00 and 15:00-19:00, Mateo has no schedule.
    server.use(
      http.get(`${API}/api/schedules`, () =>
        HttpResponse.json({
          blocks: [
            { professionalId: lucas.id, dayOfWeek: 3, startMinute: 540, endMinute: 780 },
            { professionalId: lucas.id, dayOfWeek: 3, startMinute: 900, endMinute: 1140 },
          ],
        }),
      ),
    );
    renderApp(DAY);

    await screen.findByRole('button', { name: t.agenda.slotLabel('Lucas', '10:00') });
    // Before the morning block, the lunch break and after the afternoon block: only Lucas has them.
    expect(await screen.findAllByTestId('off-hours')).toHaveLength(3);
  });

  it('warns, without blocking, when booking outside the working hours', async () => {
    setup();
    server.use(
      http.get(`${API}/api/schedules`, () =>
        HttpResponse.json({
          blocks: [{ professionalId: lucas.id, dayOfWeek: 3, startMinute: 540, endMinute: 780 }],
        }),
      ),
    );
    renderApp(DAY);

    await userEvent.click(await screen.findByRole('button', { name: t.agenda.slotLabel('Lucas', '16:00') }));
    await userEvent.selectOptions(screen.getByLabelText(t.agenda.form.service), cut.id);

    expect(await screen.findByText(t.schedules.agenda.outsideHours)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: t.agenda.form.save })).toBeEnabled();
  });

  it('warns that the day is closed when it is a holiday', async () => {
    setup();
    server.use(
      http.get(`${API}/api/schedules/exceptions`, () =>
        HttpResponse.json([{ id: 'e1', date: '2026-11-18', professionalId: null, reason: 'Feriado' }]),
      ),
    );
    renderApp(DAY);

    await userEvent.click(await screen.findByRole('button', { name: t.agenda.slotLabel('Lucas', '10:00') }));

    expect(await screen.findByText(t.schedules.agenda.closedDay('Feriado'))).toBeInTheDocument();
  });

  it('shows and accepts dates as dd/mm/aaaa', async () => {
    setup([tenAm]);
    renderApp(DAY);

    const date = await screen.findByLabelText(t.agenda.dateLabel);
    expect(date).toHaveValue('18/11/2026');

    await userEvent.clear(date);
    await userEvent.type(date, '19/11/2026');
    expect(await screen.findByText(/jueves/i)).toBeInTheDocument();
  });

  it('validates required fields in Spanish', async () => {
    setup();
    renderApp(DAY);

    await userEvent.click(await screen.findByRole('button', { name: t.agenda.slotLabel('Lucas', '09:00') }));
    await userEvent.click(screen.getByRole('button', { name: t.agenda.form.save }));

    expect(await screen.findByText(t.agenda.validation.customerRequired)).toBeInTheDocument();
    expect(screen.getByText(t.agenda.validation.serviceRequired)).toBeInTheDocument();
  });

  it('only offers the services the selected professional performs', async () => {
    setup([], [lucas, { ...mateo, serviceIds: [] }]);
    renderApp(DAY);

    await userEvent.click(await screen.findByRole('button', { name: t.agenda.slotLabel('Mateo', '09:00') }));

    expect(screen.queryByRole('option', { name: 'Corte' })).not.toBeInTheDocument();
    expect(screen.getByText(t.agenda.form.noServicesForProfessional)).toBeInTheDocument();
  });

  it('shows the overlap error returned by the API', async () => {
    setup();
    server.use(http.post(`${API}/api/appointments`, () => problem(409, 'appointment_overlap')));
    renderApp(DAY);

    await userEvent.click(await screen.findByRole('button', { name: t.agenda.slotLabel('Lucas', '10:00') }));
    await userEvent.click(await screen.findByRole('button', { name: /Ana Pérez/ }));
    await userEvent.selectOptions(screen.getByLabelText(t.agenda.form.service), cut.id);
    await userEvent.click(screen.getByRole('button', { name: t.agenda.form.save }));

    expect(await screen.findByRole('alert')).toHaveTextContent(
      'Ese profesional ya tiene un turno en ese horario.',
    );
  });

  it('opens the details of an appointment and changes its status', async () => {
    setup([tenAm]);
    let sentBody: unknown;
    server.use(
      http.put(`${API}/api/appointments/${tenAm.id}/status`, async ({ request }) => {
        sentBody = await request.json();
        return HttpResponse.json({ ...tenAm, status: 'Completed' });
      }),
    );
    renderApp(DAY);

    await userEvent.click(
      await screen.findByRole('button', { name: t.agenda.appointmentLabel('Ana Pérez', '10:00') }),
    );
    expect(screen.getByText(t.agenda.status.Scheduled)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: t.agenda.actions.complete }));

    await expect.poll(() => sentBody).toEqual({ status: 'Completed' });
  });

  it('offers to reopen a cancelled appointment instead of the scheduled actions', async () => {
    setup([{ ...tenAm, status: 'Cancelled' }]);
    renderApp(DAY);

    await userEvent.click(
      await screen.findByRole('button', { name: t.agenda.appointmentLabel('Ana Pérez', '10:00') }),
    );

    expect(screen.getByRole('button', { name: t.agenda.actions.reopen })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t.agenda.actions.complete })).not.toBeInTheDocument();
  });

  it('edits an appointment keeping the customer fixed', async () => {
    setup([tenAm]);
    let sentBody: unknown;
    server.use(
      http.put(`${API}/api/appointments/${tenAm.id}`, async ({ request }) => {
        sentBody = await request.json();
        return HttpResponse.json(tenAm);
      }),
    );
    renderApp(DAY);

    await userEvent.click(
      await screen.findByRole('button', { name: t.agenda.appointmentLabel('Ana Pérez', '10:00') }),
    );
    await userEvent.click(screen.getByRole('button', { name: t.agenda.actions.edit }));
    const time = screen.getByLabelText(t.agenda.form.time);
    await userEvent.clear(time);
    await userEvent.type(time, '11:30');
    await userEvent.click(screen.getByRole('button', { name: t.agenda.form.save }));

    await expect
      .poll(() => sentBody)
      .toEqual({
        professionalId: lucas.id,
        serviceId: cut.id,
        startsAt: '2026-11-18T14:30:00.000Z',
        notes: null,
      });
  });
});
