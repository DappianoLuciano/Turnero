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
const lucas = { id: '00000000-0000-7000-8000-0000000000b1', name: 'Lucas', isActive: true, serviceIds: [] };
const mateo = { id: '00000000-0000-7000-8000-0000000000b2', name: 'Mateo', isActive: true, serviceIds: [] };

const monday = { professionalId: lucas.id, dayOfWeek: 1, startMinute: 540, endMinute: 1080 };

function setup(options: { blocks?: object[]; exceptions?: object[]; role?: 'Owner' | 'Staff' } = {}) {
  const { blocks = [monday], exceptions = [], role = 'Owner' } = options;
  server.use(
    http.post(`${API}/api/auth/refresh`, () =>
      HttpResponse.json({ ...sessionFixture, user: { ...sessionFixture.user, role } }),
    ),
    http.get(`${API}/api/businesses/current`, () => HttpResponse.json(business)),
    http.get(`${API}/api/professionals`, () => HttpResponse.json([lucas, mateo])),
    http.get(`${API}/api/schedules`, () => HttpResponse.json({ blocks })),
    http.get(`${API}/api/schedules/exceptions`, () => HttpResponse.json(exceptions)),
  );
}

function captureSave() {
  const sent: { professional?: string; body?: unknown } = {};
  server.use(
    http.put(`${API}/api/schedules/professionals/:id`, async ({ request, params }) => {
      sent.professional = String(params.id);
      sent.body = await request.json();
      return HttpResponse.json({ blocks: [] });
    }),
  );
  return sent;
}

describe('schedules page: weekly hours', () => {
  it('shows the saved blocks of the professional, with days off for the rest', async () => {
    setup();
    renderApp('/app/schedules');

    expect(await screen.findByLabelText(`${t.schedules.block('Lunes', 1)}: ${t.schedules.from}`)).toHaveValue(
      '09:00',
    );
    expect(screen.getByLabelText(`${t.schedules.block('Lunes', 1)}: ${t.schedules.to}`)).toHaveValue('18:00');
    expect(screen.getAllByText(t.schedules.dayOff)).toHaveLength(6);
    expect(screen.queryByText(t.schedules.notConfigured)).not.toBeInTheDocument();
  });

  it('says so when the professional has no schedule yet', async () => {
    setup({ blocks: [] });
    renderApp('/app/schedules');

    expect(await screen.findByText(t.schedules.notConfigured)).toBeInTheDocument();
  });

  it('adds a block to another day and saves the whole week', async () => {
    setup();
    const sent = captureSave();
    renderApp('/app/schedules');

    await userEvent.click(await screen.findByRole('button', { name: `${t.schedules.addBlock}: Martes` }));
    await userEvent.click(screen.getByRole('button', { name: t.schedules.save }));

    await expect
      .poll(() => sent.body)
      .toEqual({
        blocks: [
          { dayOfWeek: 1, startMinute: 540, endMinute: 1080 },
          { dayOfWeek: 2, startMinute: 540, endMinute: 1080 },
        ],
      });
    expect(sent.professional).toBe(lucas.id);
    expect(await screen.findByText(t.schedules.saved)).toBeInTheDocument();
  });

  it('copies Monday to Tuesday-Friday', async () => {
    setup();
    const sent = captureSave();
    renderApp('/app/schedules');

    await userEvent.click(await screen.findByRole('button', { name: t.schedules.copyMonday }));
    await userEvent.click(screen.getByRole('button', { name: t.schedules.save }));

    await expect
      .poll(() =>
        (sent.body as { blocks: { dayOfWeek: number }[] } | undefined)?.blocks.map((b) => b.dayOfWeek),
      )
      .toEqual([1, 2, 3, 4, 5]);
  });

  it('writes a block that ends at midnight as 00:00 and sends 24:00', async () => {
    setup({ blocks: [] });
    const sent = captureSave();
    renderApp('/app/schedules');

    await userEvent.click(await screen.findByRole('button', { name: `${t.schedules.addBlock}: Sábado` }));
    const end = screen.getByLabelText(`${t.schedules.block('Sábado', 1)}: ${t.schedules.to}`);
    await userEvent.clear(end);
    await userEvent.type(end, '00:00');
    await userEvent.click(screen.getByRole('button', { name: t.schedules.save }));

    await expect
      .poll(() => sent.body)
      .toEqual({ blocks: [{ dayOfWeek: 6, startMinute: 540, endMinute: 1440 }] });
  });

  it('refuses overlapping blocks and inverted ones before calling the API', async () => {
    setup();
    const sent = captureSave();
    renderApp('/app/schedules');

    await userEvent.click(await screen.findByRole('button', { name: `${t.schedules.addBlock}: Lunes` }));
    await userEvent.click(screen.getByRole('button', { name: t.schedules.save }));
    expect(await screen.findByText(t.schedules.validation.overlap)).toBeInTheDocument();

    const end = screen.getAllByLabelText(`${t.schedules.block('Lunes', 2)}: ${t.schedules.to}`)[0]!;
    const start = screen.getByLabelText(`${t.schedules.block('Lunes', 2)}: ${t.schedules.from}`);
    await userEvent.clear(start);
    await userEvent.type(start, '20:00');
    await userEvent.clear(end);
    await userEvent.type(end, '19:00');
    await userEvent.click(screen.getByRole('button', { name: t.schedules.save }));
    expect(await screen.findByText(t.schedules.validation.endBeforeStart)).toBeInTheDocument();

    expect(sent.body).toBeUndefined();
  });

  it('removes a block', async () => {
    setup();
    const sent = captureSave();
    renderApp('/app/schedules');

    await userEvent.click(
      await screen.findByRole('button', {
        name: `${t.schedules.removeBlock}: ${t.schedules.block('Lunes', 1)}`,
      }),
    );
    await userEvent.click(screen.getByRole('button', { name: t.schedules.save }));

    await expect.poll(() => sent.body).toEqual({ blocks: [] });
  });

  it('shows the API error when saving fails', async () => {
    setup();
    server.use(
      http.put(`${API}/api/schedules/professionals/:id`, () => problem(404, 'professional_not_found')),
    );
    renderApp('/app/schedules');

    await userEvent.click(await screen.findByRole('button', { name: t.schedules.save }));

    expect(await screen.findByText('El profesional ya no existe.')).toBeInTheDocument();
  });

  it('loads the schedule of the professional picked in the selector', async () => {
    setup({ blocks: [monday, { professionalId: mateo.id, dayOfWeek: 1, startMinute: 600, endMinute: 900 }] });
    renderApp('/app/schedules');

    await userEvent.selectOptions(await screen.findByLabelText(t.schedules.professional), mateo.id);

    expect(await screen.findByLabelText(`${t.schedules.block('Lunes', 1)}: ${t.schedules.from}`)).toHaveValue(
      '10:00',
    );
  });

  it('is read-only for staff', async () => {
    setup({ role: 'Staff' });
    renderApp('/app/schedules');

    expect(await screen.findByText(t.schedules.readOnly)).toBeInTheDocument();
    expect(screen.getByLabelText(`${t.schedules.block('Lunes', 1)}: ${t.schedules.from}`)).toBeDisabled();
    expect(screen.queryByRole('button', { name: t.schedules.save })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t.schedules.closed.add })).not.toBeInTheDocument();
  });
});

describe('schedules page: closed days', () => {
  const today = todayIn(timeZone);

  it('lists upcoming closed days with date as dd/mm/aaaa and who they apply to', async () => {
    setup({
      exceptions: [
        { id: 'e1', date: '2026-12-25', professionalId: null, reason: 'Navidad' },
        { id: 'e2', date: '2026-12-31', professionalId: lucas.id, reason: null },
      ],
    });
    renderApp('/app/schedules');

    expect(await screen.findByText('25/12/2026')).toBeInTheDocument();
    expect(screen.getByText(/Navidad/)).toBeInTheDocument();
    expect(screen.getByText('31/12/2026')).toBeInTheDocument();
  });

  it('closes a day for the whole business', async () => {
    setup();
    let sent: unknown;
    server.use(
      http.post(`${API}/api/schedules/exceptions`, async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json(
          { id: 'new', date: '2026-12-25', professionalId: null, reason: 'Navidad' },
          { status: 201 },
        );
      }),
    );
    renderApp('/app/schedules');

    await userEvent.type(await screen.findByLabelText(t.schedules.closed.date), '25/12/2026');
    await userEvent.type(screen.getByLabelText(t.schedules.closed.reason), 'Navidad');
    await userEvent.click(screen.getByRole('button', { name: t.schedules.closed.add }));

    await expect.poll(() => sent).toEqual({ date: '2026-12-25', professionalId: null, reason: 'Navidad' });
  });

  it('closes a day for one professional', async () => {
    setup();
    let sent: unknown;
    server.use(
      http.post(`${API}/api/schedules/exceptions`, async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json(
          { id: 'new', date: today, professionalId: lucas.id, reason: null },
          { status: 201 },
        );
      }),
    );
    renderApp('/app/schedules');

    await userEvent.type(await screen.findByLabelText(t.schedules.closed.date), '01/03/2027');
    await userEvent.selectOptions(screen.getByLabelText(t.schedules.closed.scope), lucas.id);
    await userEvent.click(screen.getByRole('button', { name: t.schedules.closed.add }));

    await expect.poll(() => sent).toEqual({ date: '2027-03-01', professionalId: lucas.id, reason: null });
  });

  it('asks for a date and shows the duplicate error from the API', async () => {
    setup();
    server.use(http.post(`${API}/api/schedules/exceptions`, () => problem(409, 'schedule_exception_exists')));
    renderApp('/app/schedules');

    await userEvent.click(await screen.findByRole('button', { name: t.schedules.closed.add }));
    expect(await screen.findByText(t.schedules.closed.dateInvalid)).toBeInTheDocument();

    await userEvent.type(screen.getByLabelText(t.schedules.closed.date), '25/12/2026');
    await userEvent.click(screen.getByRole('button', { name: t.schedules.closed.add }));
    expect(await screen.findByText('Ese día ya está cerrado.')).toBeInTheDocument();
  });

  it('reopens a closed day', async () => {
    setup({ exceptions: [{ id: 'e1', date: '2026-12-25', professionalId: null, reason: 'Navidad' }] });
    let deleted = '';
    server.use(
      http.delete(`${API}/api/schedules/exceptions/:id`, ({ params }) => {
        deleted = String(params.id);
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderApp('/app/schedules');

    await userEvent.click(
      await screen.findByRole('button', { name: `${t.schedules.closed.remove} 25/12/2026` }),
    );

    await expect.poll(() => deleted).toBe('e1');
  });
});
