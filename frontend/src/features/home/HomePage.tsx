import { Link } from 'react-router';
import { useAppointments, type Appointment } from '@/features/agenda/agenda-api';
import { useSession } from '@/features/auth/use-session';
import { useBusiness } from '@/features/business/business-api';
import { useCashDay } from '@/features/cash/cash-api';
import { navItems } from '@/features/panel/navigation';
import { BookingLinkCard } from './BookingLinkCard';
import { useDailyPayments } from '@/features/payments/payments-api';
import { errorMessage } from '@/shared/api/problem';
import { formatMoney } from '@/shared/format/money';
import { t } from '@/shared/i18n';
import { formatMinutes, toLocal, todayIn } from '@/shared/time/business-time';
import { Button } from '@/shared/ui/button';

const QUICK_LINKS = ['/app/agenda', '/app/customers', '/app/payments', '/app/cash-register'];
const UPCOMING_LIMIT = 5;

/** Dashboard: what happens today, at a glance. */
export function HomePage() {
  const session = useSession();
  const name = session.status === 'authenticated' ? session.user.fullName : '';
  const business = useBusiness();

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold">{t.panel.home.greeting(name)}</h1>
        <p className="text-muted-foreground">{t.panel.home.description}</p>
      </div>

      {business.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(business.error)}
        </p>
      )}
      {business.data && <Today timeZone={business.data.timeZone} currency={business.data.currency} />}

      {business.data && <BookingLinkCard slug={business.data.slug} />}

      <div className="flex flex-col gap-2">
        <h2 className="text-lg font-semibold">{t.panel.home.quickLinks}</h2>
        <div className="flex flex-wrap gap-2">
          {navItems
            .filter((item) => QUICK_LINKS.includes(item.to))
            .map(({ to, label, icon: Icon }) => (
              <Button key={to} asChild variant="outline">
                <Link to={to}>
                  <Icon />
                  {label}
                </Link>
              </Button>
            ))}
        </div>
      </div>
    </section>
  );
}

function Today({ timeZone, currency }: { timeZone: string; currency: string }) {
  const today = todayIn(timeZone);
  const appointments = useAppointments(today);
  const payments = useDailyPayments(today);
  const cash = useCashDay(today);

  const failure = appointments.error ?? payments.error ?? cash.error;
  const list = appointments.data ?? [];
  const nowMinutes = toLocal(new Date(), timeZone).minutes;
  const pending = list.filter((a) => a.status === 'Scheduled');
  const upcoming = pending
    .filter((a) => toLocal(a.startsAt, timeZone).minutes >= nowMinutes)
    .slice(0, UPCOMING_LIMIT);
  const money = (amount: number) => formatMoney(amount, currency);

  return (
    <div className="flex flex-col gap-4">
      {failure != null && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(failure)}
        </p>
      )}

      <div className="grid gap-3 sm:grid-cols-3">
        <Card
          label={t.panel.home.appointmentsToday}
          value={appointments.isSuccess ? String(list.length) : '…'}
          hint={appointments.isSuccess ? t.panel.home.pending(pending.length) : undefined}
        />
        <Card
          label={t.panel.home.collectedToday}
          value={payments.isSuccess ? money(payments.data.total) : '…'}
        />
        <Card label={t.panel.home.cashExpected} value={cash.isSuccess ? money(cash.data.expected) : '…'} />
      </div>

      <div>
        <h2 className="mb-2 text-lg font-semibold">{t.panel.home.upcoming}</h2>
        {appointments.isSuccess && upcoming.length === 0 && (
          <p className="text-muted-foreground text-sm">{t.panel.home.noUpcoming}</p>
        )}
        <ul className="flex flex-col gap-2 text-sm">
          {upcoming.map((a) => (
            <UpcomingRow key={a.id} appointment={a} timeZone={timeZone} date={today} />
          ))}
        </ul>
      </div>
    </div>
  );
}

function Card({ label, value, hint }: { label: string; value: string; hint?: string | undefined }) {
  return (
    <div className="bg-card rounded-xl border p-4 shadow-sm">
      <p className="text-muted-foreground text-sm">{label}</p>
      <p className="text-2xl font-semibold">{value}</p>
      {hint && <p className="text-muted-foreground text-xs">{hint}</p>}
    </div>
  );
}

function UpcomingRow({
  appointment,
  timeZone,
  date,
}: {
  appointment: Appointment;
  timeZone: string;
  date: string;
}) {
  const time = formatMinutes(toLocal(appointment.startsAt, timeZone).minutes);
  return (
    <li>
      <Link
        to={`/app/agenda?date=${date}`}
        className="bg-card hover:bg-muted flex flex-wrap items-center justify-between gap-2 rounded-md border p-3"
      >
        <span>
          <span className="font-medium">{time}</span> · {appointment.customerName}
        </span>
        <span className="text-muted-foreground">
          {appointment.serviceName} · {appointment.professionalName}
        </span>
      </Link>
    </li>
  );
}
