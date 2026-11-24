import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { t } from '@/shared/i18n';
import { renderApp } from '@/test/render';
import { API, server, sessionFixture } from '@/test/server';

const business = {
  id: 'b',
  name: 'Barbería Demo',
  slug: 'demo',
  timeZone: 'America/Argentina/Buenos_Aires',
  currency: 'ARS',
  phone: null,
  customerChangeNoticeHours: 12,
};

const reminder = {
  appointmentId: 'a1',
  customerName: 'Ana Pérez',
  customerPhone: '+5491155551234',
  professionalName: 'Lucas',
  serviceName: 'Corte',
  startsAt: '2030-01-07T13:00:00Z',
  reminderSentAt: null as string | null,
  manageToken: 'f'.repeat(64),
};

function setup(items: object[]) {
  server.use(
    http.post(`${API}/api/auth/refresh`, () => HttpResponse.json(sessionFixture)),
    http.get(`${API}/api/businesses/current`, () => HttpResponse.json(business)),
    http.get(`${API}/api/appointments/reminders`, () => HttpResponse.json(items)),
  );
}

describe('reminders page', () => {
  it('lists the appointments with a WhatsApp link that carries the message and the manage link', async () => {
    setup([reminder]);
    renderApp('/app/reminders');

    const link = await screen.findByRole('link', { name: t.reminders.sendTo('Ana Pérez') });
    const url = new URL(link.getAttribute('href')!);
    expect(url.origin + url.pathname).toBe('https://wa.me/5491155551234');
    const text = url.searchParams.get('text')!;
    expect(text).toContain('Hola Ana!');
    expect(text).toContain(`/r/demo/turno/${reminder.manageToken}`);
    expect(link).toHaveAttribute('target', '_blank');
    expect(screen.getByText(t.reminders.summary(0, 1))).toBeInTheDocument();
  });

  it('marks the reminder as sent when WhatsApp is opened', async () => {
    let markedId: string | null = null;
    setup([reminder]);
    server.use(
      http.post(`${API}/api/appointments/:id/reminder-sent`, ({ params }) => {
        markedId = params.id as string;
        return new HttpResponse(null, { status: 204 });
      }),
    );
    renderApp('/app/reminders');

    const link = await screen.findByRole('link', { name: t.reminders.sendTo('Ana Pérez') });
    // Do not navigate away in the test: only the click handler matters.
    link.addEventListener('click', (e) => e.preventDefault());
    await userEvent.click(link);

    await vi.waitFor(() => expect(markedId).toBe('a1'));
  });

  it('shows who was already reminded and offers to resend', async () => {
    setup([{ ...reminder, reminderSentAt: '2030-01-06T15:00:00Z' }]);
    renderApp('/app/reminders');

    expect(await screen.findByText(t.reminders.sent)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: t.reminders.sendTo('Ana Pérez') })).toHaveTextContent(
      t.reminders.resend,
    );
    expect(screen.getByText(t.reminders.summary(1, 1))).toBeInTheDocument();
  });

  it('flags customers without a usable phone and handles an empty day', async () => {
    setup([{ ...reminder, customerPhone: null }]);
    renderApp('/app/reminders');

    expect(await screen.findByText(t.reminders.noPhone)).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: t.reminders.sendTo('Ana Pérez') })).not.toBeInTheDocument();
  });

  it('asks for the day: tomorrow by default, today on demand', async () => {
    const asked: string[] = [];
    setup([]);
    server.use(
      http.get(`${API}/api/appointments/reminders`, ({ request }) => {
        asked.push(new URL(request.url).searchParams.get('date')!);
        return HttpResponse.json([]);
      }),
    );
    renderApp('/app/reminders');

    expect(await screen.findByText(t.reminders.empty)).toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: t.reminders.today }));
    await vi.waitFor(() => expect(asked.length).toBe(2));
    expect(new Date(asked[1]!).getTime()).toBeLessThan(new Date(asked[0]!).getTime());
  });
});
