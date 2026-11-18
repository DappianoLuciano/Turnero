import { useSearchParams } from 'react-router';
import { useSession } from '@/features/auth/use-session';
import { useBusiness } from '@/features/business/business-api';
import { errorMessage } from '@/shared/api/problem';
import { formatMoney } from '@/shared/format/money';
import { t } from '@/shared/i18n';
import { addDays, isValidDate, todayIn } from '@/shared/time/business-time';
import { DateField } from '@/shared/time/DateField';
import { Button } from '@/shared/ui/button';
import { Label } from '@/shared/ui/label';
import {
  useExportSummaryReport,
  useSummaryReport,
  type ReportGroup,
  type SummaryReport,
} from './reports-api';

const MAX_DAYS = 366;

const daysBetween = (from: string, to: string) =>
  Math.round((Date.parse(`${to}T00:00:00Z`) - Date.parse(`${from}T00:00:00Z`)) / 86_400_000);

export function ReportsPage() {
  const session = useSession();
  const isOwner = session.status === 'authenticated' && session.user.role === 'Owner';
  const business = useBusiness();
  const timeZone = business.data?.timeZone ?? null;
  const [params, setParams] = useSearchParams();

  const today = timeZone ? todayIn(timeZone) : null;
  const requestedFrom = params.get('from');
  const requestedTo = params.get('to');
  const to = isValidDate(requestedTo) ? requestedTo : today;
  const from = isValidDate(requestedFrom) ? requestedFrom : to ? addDays(to, -6) : null;

  const validRange = !!from && !!to && from <= to && daysBetween(from, to) < MAX_DAYS;
  const exportReport = useExportSummaryReport();
  const report = useSummaryReport(from ?? '', to ?? '', isOwner && validRange);

  const setRange = (nextFrom: string, nextTo: string) =>
    setParams({ from: nextFrom, to: nextTo }, { replace: true });

  if (!isOwner) {
    return (
      <section className="flex flex-col gap-4">
        <h1 className="text-2xl font-semibold">{t.reports.title}</h1>
        <p className="text-muted-foreground">{t.reports.ownerOnly}</p>
      </section>
    );
  }

  return (
    <section className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold">{t.reports.title}</h1>

      {from && to && today && (
        <div className="flex flex-wrap items-end gap-3">
          <div className="flex flex-col gap-2">
            <Label htmlFor="report-from">{t.reports.from}</Label>
            <DateField
              id="report-from"
              className="w-36"
              value={from}
              onChange={(next) => isValidDate(next) && setRange(next, to)}
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="report-to">{t.reports.to}</Label>
            <DateField
              id="report-to"
              className="w-36"
              value={to}
              onChange={(next) => isValidDate(next) && setRange(from, next)}
            />
          </div>
          <Button variant="outline" onClick={() => setRange(today, today)}>
            {t.reports.presets.today}
          </Button>
          <Button variant="outline" onClick={() => setRange(addDays(today, -6), today)}>
            {t.reports.presets.last7}
          </Button>
          <Button variant="outline" onClick={() => setRange(`${today.slice(0, 8)}01`, today)}>
            {t.reports.presets.thisMonth}
          </Button>
        </div>
      )}

      {validRange && from && to && (
        <div className="flex flex-col items-start gap-2">
          <Button
            variant="outline"
            disabled={exportReport.isPending}
            onClick={() => exportReport.mutate({ from, to })}
          >
            {exportReport.isPending ? t.reports.downloading : t.reports.download}
          </Button>
          {exportReport.isError && (
            <p role="alert" className="text-destructive text-sm">
              {errorMessage(exportReport.error)}
            </p>
          )}
        </div>
      )}

      {from && to && !validRange && (
        <p role="alert" className="text-destructive text-sm">
          {t.reports.invalidRange}
        </p>
      )}
      {validRange && report.isPending && <p className="text-muted-foreground">{t.app.loading}</p>}
      {report.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(report.error)}
        </p>
      )}
      {validRange && report.data && <ReportView report={report.data} />}
    </section>
  );
}

function ReportView({ report }: { report: SummaryReport }) {
  const money = (amount: number) => formatMoney(amount, report.currency);
  const empty =
    report.collected === 0 && report.byMethod.length === 0 && report.appointmentsByStatus.length === 0;

  return (
    <div className="flex flex-col gap-4">
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="bg-card rounded-xl border p-4 shadow-sm">
          <p className="text-muted-foreground text-sm">{t.reports.collected}</p>
          <p className="text-2xl font-semibold">{money(report.collected)}</p>
          <p className="text-muted-foreground text-xs">{t.reports.collectedHint}</p>
        </div>
        <div className="bg-card rounded-xl border p-4 shadow-sm">
          <p className="text-muted-foreground text-sm">{t.reports.completedValue}</p>
          <p className="text-2xl font-semibold">{money(report.completedValue)}</p>
        </div>
      </div>

      {empty && <p className="text-muted-foreground text-sm">{t.reports.empty}</p>}

      {report.byMethod.length > 0 && (
        <Block title={t.reports.byMethod}>
          {report.byMethod.map((m) => (
            <Row key={m.method} label={t.payments.methods[m.method]} value={money(m.total)} />
          ))}
        </Block>
      )}

      {report.appointmentsByStatus.length > 0 && (
        <Block title={t.reports.byStatus}>
          {report.appointmentsByStatus.map((s) => (
            <Row key={s.status} label={t.agenda.status[s.status]} value={String(s.count)} />
          ))}
        </Block>
      )}

      <Groups title={t.reports.byProfessional} groups={report.byProfessional} money={money} />
      <Groups title={t.reports.byService} groups={report.byService} money={money} />
    </div>
  );
}

function Groups({
  title,
  groups,
  money,
}: {
  title: string;
  groups: ReportGroup[];
  money: (amount: number) => string;
}) {
  if (groups.length === 0) return null;
  return (
    <Block title={title}>
      {groups.map((g) => (
        <Row
          key={g.id}
          label={`${g.name} · ${g.count} ${t.reports.appointments.toLowerCase()}`}
          value={money(g.value)}
        />
      ))}
    </Block>
  );
}

function Block({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section aria-label={title} className="bg-card rounded-xl border p-4 shadow-sm">
      <h2 className="mb-2 text-lg font-semibold">{title}</h2>
      <ul className="flex flex-col gap-1 text-sm">{children}</ul>
    </section>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <li className="flex justify-between gap-2">
      <span>{label}</span>
      <span className="font-medium">{value}</span>
    </li>
  );
}
