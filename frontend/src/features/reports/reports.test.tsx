import { screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { http, HttpResponse } from 'msw';
import { describe, expect, it } from 'vitest';
import { t } from '@/shared/i18n';
import { renderApp } from '@/test/render';
import { API, server, sessionFixture } from '@/test/server';

const business = {
  id: sessionFixture.user.businessId,
  name: 'Demo',
  slug: 'demo',
  timeZone: 'America/Argentina/Buenos_Aires',
  currency: 'ARS',
};

const report = {
  from: '2026-11-01',
  to: '2026-11-30',
  currency: 'ARS',
  collected: 5000,
  byMethod: [
    { method: 'Cash', total: 3000 },
    { method: 'Transfer', total: 2000 },
  ],
  appointmentsByStatus: [
    { status: 'Completed', count: 3 },
    { status: 'Cancelled', count: 1 },
  ],
  completedValue: 19000,
  byProfessional: [{ id: 'p1', name: 'Lucas', count: 2, value: 11000 }],
  byService: [{ id: 's1', name: 'Corte', count: 2, value: 16000 }],
};

function setup(role: 'Owner' | 'Staff' = 'Owner', payload: object = report) {
  const requests: URL[] = [];
  server.use(
    http.post(`${API}/api/auth/refresh`, () =>
      HttpResponse.json({ ...sessionFixture, user: { ...sessionFixture.user, role } }),
    ),
    http.get(`${API}/api/businesses/current`, () => HttpResponse.json(business)),
    http.get(`${API}/api/reports/summary`, ({ request }) => {
      requests.push(new URL(request.url));
      return HttpResponse.json(payload);
    }),
  );
  return requests;
}

describe('reports page', () => {
  it('downloads the range as an Excel file', async () => {
    setup();
    const exported: URL[] = [];
    server.use(
      http.get(`${API}/api/reports/summary/export`, ({ request }) => {
        exported.push(new URL(request.url));
        return new HttpResponse(new Uint8Array([80, 75, 3, 4]), {
          headers: { 'Content-Type': 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' },
        });
      }),
    );
    const created: Blob[] = [];
    const downloads: string[] = [];
    URL.createObjectURL = (blob: Blob | MediaSource) => {
      created.push(blob as Blob);
      return 'blob:test';
    };
    URL.revokeObjectURL = () => undefined;
    const click = HTMLAnchorElement.prototype.click;
    HTMLAnchorElement.prototype.click = function (this: HTMLAnchorElement) {
      downloads.push(this.download);
    };
    try {
      renderApp('/app/reports?from=2026-11-01&to=2026-11-30');
      await userEvent.click(await screen.findByRole('button', { name: t.reports.download }));

      await expect.poll(() => downloads).toEqual(['reporte-2026-11-01-2026-11-30.xlsx']);
      expect(exported[0]?.searchParams.get('from')).toBe('2026-11-01');
      expect(created).toHaveLength(1);
    } finally {
      HTMLAnchorElement.prototype.click = click;
    }
  });

  it('does not offer the download for an invalid range', async () => {
    setup();
    renderApp('/app/reports?from=2026-11-30&to=2026-11-01');

    await screen.findByText(t.reports.invalidRange);
    expect(screen.queryByRole('button', { name: t.reports.download })).not.toBeInTheDocument();
  });

  it('shows the totals and the breakdowns for the chosen range', async () => {
    const requests = setup();
    renderApp('/app/reports?from=2026-11-01&to=2026-11-30');

    expect(await screen.findByText(/5\.000,00/, { selector: 'p' })).toBeInTheDocument();
    expect(screen.getByText(/19\.000,00/, { selector: 'p' })).toBeInTheDocument();
    expect(screen.getByText(/Lucas · 2 turnos/)).toBeInTheDocument();
    expect(screen.getByText(/Corte · 2 turnos/)).toBeInTheDocument();
    expect(screen.getByText(t.agenda.status.Completed)).toBeInTheDocument();
    expect(screen.getByText(t.payments.methods.Transfer)).toBeInTheDocument();
    expect(requests[0]?.searchParams.get('from')).toBe('2026-11-01');
    expect(requests[0]?.searchParams.get('to')).toBe('2026-11-30');
    expect(screen.getByLabelText(t.reports.from)).toHaveValue('01/11/2026');
  });

  it('asks for the new range when a date is typed as dd/mm/aaaa', async () => {
    const requests = setup();
    renderApp('/app/reports?from=2026-11-01&to=2026-11-30');

    const from = await screen.findByLabelText(t.reports.from);
    await userEvent.clear(from);
    await userEvent.type(from, '15/11/2026');

    await expect.poll(() => requests.some((r) => r.searchParams.get('from') === '2026-11-15')).toBe(true);
  });

  it('does not call the API for an inverted range and explains it', async () => {
    const requests = setup();
    renderApp('/app/reports?from=2026-11-30&to=2026-11-01');

    expect(await screen.findByText(t.reports.invalidRange)).toBeInTheDocument();
    expect(requests).toHaveLength(0);
  });

  it('says so when there is no data', async () => {
    setup('Owner', {
      ...report,
      collected: 0,
      byMethod: [],
      appointmentsByStatus: [],
      completedValue: 0,
      byProfessional: [],
      byService: [],
    });
    renderApp('/app/reports?from=2026-11-01&to=2026-11-30');

    expect(await screen.findByText(t.reports.empty)).toBeInTheDocument();
  });

  it('is for the owner only: staff sees a message and no request is made', async () => {
    const requests = setup('Staff');
    renderApp('/app/reports?from=2026-11-01&to=2026-11-30');

    expect(await screen.findByText(t.reports.ownerOnly)).toBeInTheDocument();
    expect(requests).toHaveLength(0);
  });
});
