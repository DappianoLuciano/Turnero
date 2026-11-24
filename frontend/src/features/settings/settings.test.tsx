import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
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

const loginAs = (role: 'Owner' | 'Staff') =>
  server.use(
    http.post(`${API}/api/auth/refresh`, () =>
      HttpResponse.json({ ...sessionFixture, user: { ...sessionFixture.user, role } }),
    ),
    http.get(`${API}/api/businesses/current`, () => HttpResponse.json(business)),
  );

describe('settings page', () => {
  it('saves the phone and the notice hours', async () => {
    let sent: unknown;
    loginAs('Owner');
    server.use(
      http.put(`${API}/api/businesses/current/settings`, async ({ request }) => {
        sent = await request.json();
        return HttpResponse.json({ ...business, phone: '+5491100009999', customerChangeNoticeHours: 24 });
      }),
    );
    renderApp('/app/settings');

    const phone = await screen.findByLabelText(t.settings.phone);
    await userEvent.type(phone, '+5491100009999');
    const hours = screen.getByLabelText(t.settings.noticeHours);
    await userEvent.clear(hours);
    await userEvent.type(hours, '24');
    await userEvent.click(screen.getByRole('button', { name: t.settings.save }));

    expect(await screen.findByRole('status')).toHaveTextContent(t.settings.saved);
    expect(sent).toEqual({ phone: '+5491100009999', customerChangeNoticeHours: 24 });
  });

  it('validates the notice hours', async () => {
    loginAs('Owner');
    renderApp('/app/settings');

    const hours = await screen.findByLabelText(t.settings.noticeHours);
    await userEvent.clear(hours);
    await userEvent.type(hours, '900');
    await userEvent.click(screen.getByRole('button', { name: t.settings.save }));

    expect(await screen.findByText(t.settings.validation.noticeRange)).toBeInTheDocument();
  });

  it('is read-only for non-owners', async () => {
    loginAs('Staff');
    renderApp('/app/settings');

    expect(await screen.findByLabelText(t.settings.phone)).toBeDisabled();
    expect(screen.getByText(t.settings.ownerOnly)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: t.settings.save })).not.toBeInTheDocument();
  });
});
