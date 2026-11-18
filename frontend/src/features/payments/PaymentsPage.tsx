import { ChevronLeft, ChevronRight } from 'lucide-react';
import { useSearchParams } from 'react-router';
import { useBusiness } from '@/features/business/business-api';
import { errorMessage } from '@/shared/api/problem';
import { formatMoney } from '@/shared/format/money';
import { t } from '@/shared/i18n';
import { addDays, formatMinutes, isValidDate, toLocal, todayIn } from '@/shared/time/business-time';
import { DateField } from '@/shared/time/DateField';
import { Button } from '@/shared/ui/button';
import { Label } from '@/shared/ui/label';
import { useDailyPayments } from './payments-api';

export function PaymentsPage() {
  const business = useBusiness();
  const timeZone = business.data?.timeZone ?? null;
  const [params, setParams] = useSearchParams();
  const requested = params.get('date');
  const date = timeZone ? (isValidDate(requested) ? requested : todayIn(timeZone)) : null;

  const goTo = (next: string) => setParams({ date: next }, { replace: true });

  return (
    <section className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold">{t.payments.title}</h1>

      {date && timeZone && (
        <>
          <div className="flex flex-wrap items-center gap-2">
            <Button
              variant="outline"
              size="icon"
              aria-label={t.agenda.previousDay}
              onClick={() => goTo(addDays(date, -1))}
            >
              <ChevronLeft />
            </Button>
            <Button
              variant="outline"
              size="icon"
              aria-label={t.agenda.nextDay}
              onClick={() => goTo(addDays(date, 1))}
            >
              <ChevronRight />
            </Button>
            <Button variant="outline" onClick={() => goTo(todayIn(timeZone))}>
              {t.agenda.today}
            </Button>
            <Label htmlFor="payments-date" className="sr-only">
              {t.agenda.dateLabel}
            </Label>
            <DateField
              id="payments-date"
              className="w-36"
              value={date}
              onChange={(next) => isValidDate(next) && goTo(next)}
            />
          </div>
          <DailyReport date={date} timeZone={timeZone} />
        </>
      )}
    </section>
  );
}

function DailyReport({ date, timeZone }: { date: string; timeZone: string }) {
  const daily = useDailyPayments(date);

  if (daily.isPending) return <p className="text-muted-foreground">{t.app.loading}</p>;
  if (daily.isError) {
    return (
      <p role="alert" className="text-destructive text-sm">
        {errorMessage(daily.error)}
      </p>
    );
  }

  const { currency, total, byMethod, items } = daily.data;
  const money = (amount: number) => formatMoney(amount, currency);

  return (
    <div className="flex flex-col gap-4">
      <div className="bg-card flex flex-col gap-3 rounded-xl border p-4 shadow-sm">
        <div>
          <p className="text-muted-foreground text-sm">{t.payments.daily.total}</p>
          <p className="text-2xl font-semibold">{money(total)}</p>
        </div>
        {byMethod.length > 0 && (
          <div>
            <h2 className="text-muted-foreground mb-1 text-sm">{t.payments.daily.byMethod}</h2>
            <ul className="grid gap-1 text-sm sm:grid-cols-2">
              {byMethod.map((m) => (
                <li key={m.method} className="flex justify-between gap-2">
                  <span>{t.payments.methods[m.method]}</span>
                  <span className="font-medium">{money(m.total)}</span>
                </li>
              ))}
            </ul>
          </div>
        )}
      </div>

      {items.length === 0 ? (
        <p className="text-muted-foreground text-sm">{t.payments.daily.empty}</p>
      ) : (
        <div>
          <h2 className="mb-2 text-lg font-semibold">{t.payments.daily.movements}</h2>
          <ul className="flex flex-col gap-2 text-sm">
            {items.map(({ payment, customerName }) => {
              const local = toLocal(payment.createdAt, timeZone);
              const isReversal = payment.reversesId != null;
              return (
                <li
                  key={payment.id}
                  className="bg-card flex flex-wrap items-center justify-between gap-2 rounded-md border p-3"
                >
                  <span>
                    {formatMinutes(local.minutes)} · {customerName} · {t.payments.methods[payment.method]}
                    {isReversal && ` · ${t.payments.reversalOf}`}
                  </span>
                  <span className={payment.amount < 0 ? 'text-destructive font-medium' : 'font-medium'}>
                    {money(payment.amount)}
                  </span>
                </li>
              );
            })}
          </ul>
        </div>
      )}
    </div>
  );
}
