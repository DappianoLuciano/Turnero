import { ChevronLeft, ChevronRight } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { useSearchParams } from 'react-router';
import { useSession } from '@/features/auth/use-session';
import { useBusiness } from '@/features/business/business-api';
import { errorMessage } from '@/shared/api/problem';
import { formatMoney, parseAmount } from '@/shared/format/money';
import { t } from '@/shared/i18n';
import { addDays, formatMinutes, isValidDate, toLocal, todayIn } from '@/shared/time/business-time';
import { DateField } from '@/shared/time/DateField';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Label } from '@/shared/ui/label';
import {
  cashKinds,
  useCashDay,
  useCloseCashDay,
  useRegisterCashMovement,
  useReverseCashMovement,
  type CashDay,
  type CashKind,
  type CashMovement,
} from './cash-api';

const selectClass =
  'border-input bg-background focus-visible:ring-ring/50 h-9 w-full rounded-md border px-3 text-sm shadow-xs outline-none focus-visible:ring-[3px]';

export function CashPage() {
  const business = useBusiness();
  const timeZone = business.data?.timeZone ?? null;
  const [params, setParams] = useSearchParams();
  const requested = params.get('date');
  const date = timeZone ? (isValidDate(requested) ? requested : todayIn(timeZone)) : null;
  const goTo = (next: string) => setParams({ date: next }, { replace: true });

  return (
    <section className="flex flex-col gap-4">
      <h1 className="text-2xl font-semibold">{t.cash.title}</h1>
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
            <Label htmlFor="cash-date" className="sr-only">
              {t.agenda.dateLabel}
            </Label>
            <DateField
              id="cash-date"
              className="w-36"
              value={date}
              onChange={(next) => isValidDate(next) && goTo(next)}
            />
          </div>
          <CashDayView key={date} date={date} timeZone={timeZone} today={todayIn(timeZone)} />
        </>
      )}
    </section>
  );
}

function CashDayView({ date, timeZone, today }: { date: string; timeZone: string; today: string }) {
  const query = useCashDay(date);

  if (query.isPending) return <p className="text-muted-foreground">{t.app.loading}</p>;
  if (query.isError) {
    return (
      <p role="alert" className="text-destructive text-sm">
        {errorMessage(query.error)}
      </p>
    );
  }

  const day = query.data;
  const money = (amount: number) => formatMoney(amount, day.currency);
  // Only today can receive manual movements, and only while it is not closed.
  const canMove = date === today && !day.isClosed;

  return (
    <div className="flex flex-col gap-4">
      <div className="bg-card grid gap-3 rounded-xl border p-4 shadow-sm sm:grid-cols-3">
        <div>
          <p className="text-muted-foreground text-sm">{t.cash.cashPayments}</p>
          <p className="text-lg font-medium">{money(day.cashPayments)}</p>
        </div>
        <div>
          <p className="text-muted-foreground text-sm">{t.cash.manualNet}</p>
          <p className="text-lg font-medium">{money(day.manualNet)}</p>
        </div>
        <div>
          <p className="text-muted-foreground text-sm">{t.cash.expected}</p>
          <p className="text-2xl font-semibold">{money(day.expected)}</p>
        </div>
      </div>

      {canMove && <MovementForm />}
      <Movements day={day} timeZone={timeZone} canReverse={canMove} />
      {day.closing ? <ClosingSummary day={day} /> : date <= today && <ClosingForm date={date} />}
    </div>
  );
}

function MovementForm() {
  const register = useRegisterCashMovement();
  const [kind, setKind] = useState<CashKind>('Income');
  const [amount, setAmount] = useState('');
  const [concept, setConcept] = useState('');
  const [error, setError] = useState<string | null>(null);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const parsed = parseAmount(amount);
    if (parsed === null) return setError(t.cash.validation.amountInvalid);
    if (!concept.trim()) return setError(t.cash.validation.conceptRequired);
    setError(null);
    register.mutate(
      { kind, amount: parsed, concept: concept.trim() },
      {
        onSuccess: () => {
          setAmount('');
          setConcept('');
        },
      },
    );
  };

  return (
    <form
      noValidate
      aria-label={t.cash.form.title}
      className="bg-card grid gap-3 rounded-xl border p-4 shadow-sm sm:grid-cols-[auto_1fr_2fr_auto] sm:items-end"
      onSubmit={submit}
    >
      <div className="flex flex-col gap-2">
        <Label htmlFor="cash-kind">{t.cash.form.kind}</Label>
        <select
          id="cash-kind"
          className={selectClass}
          value={kind}
          onChange={(event) => setKind(event.target.value as CashKind)}
        >
          {cashKinds.map((k) => (
            <option key={k} value={k}>
              {t.cash.kinds[k]}
            </option>
          ))}
        </select>
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="cash-amount">{t.cash.form.amount}</Label>
        <Input
          id="cash-amount"
          inputMode="decimal"
          value={amount}
          onChange={(event) => setAmount(event.target.value)}
        />
      </div>
      <div className="flex flex-col gap-2">
        <Label htmlFor="cash-concept">{t.cash.form.concept}</Label>
        <Input
          id="cash-concept"
          value={concept}
          maxLength={120}
          placeholder={t.cash.form.conceptPlaceholder}
          onChange={(event) => setConcept(event.target.value)}
        />
      </div>
      <Button type="submit" disabled={register.isPending}>
        {register.isPending ? t.cash.form.submitting : t.cash.form.submit}
      </Button>
      {error && <p className="text-destructive text-sm sm:col-span-4">{error}</p>}
      {register.isError && (
        <p role="alert" className="text-destructive text-sm sm:col-span-4">
          {errorMessage(register.error)}
        </p>
      )}
    </form>
  );
}

function Movements({ day, timeZone, canReverse }: { day: CashDay; timeZone: string; canReverse: boolean }) {
  const session = useSession();
  const isOwner = session.status === 'authenticated' && session.user.role === 'Owner';
  const reverse = useReverseCashMovement();
  const [reversingId, setReversingId] = useState<string | null>(null);
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<string | null>(null);
  const reversedIds = new Set(day.movements.flatMap((m) => (m.reversesId ? [m.reversesId] : [])));

  const confirm = (event: FormEvent, movement: CashMovement) => {
    event.preventDefault();
    if (!reason.trim()) return setReasonError(t.payments.validation.reasonRequired);
    setReasonError(null);
    reverse.mutate(
      { id: movement.id, reason: reason.trim() },
      {
        onSuccess: () => {
          setReversingId(null);
          setReason('');
        },
      },
    );
  };

  return (
    <div>
      <h2 className="mb-2 text-lg font-semibold">{t.cash.movements}</h2>
      {day.movements.length === 0 ? (
        <p className="text-muted-foreground text-sm">{t.cash.none}</p>
      ) : (
        <ul className="flex flex-col gap-2 text-sm">
          {day.movements.map((m) => {
            const isReversal = m.reversesId != null;
            const wasReversed = reversedIds.has(m.id);
            const time = formatMinutes(toLocal(m.createdAt, timeZone).minutes);
            return (
              <li key={m.id} className="bg-card flex flex-col gap-2 rounded-md border p-3">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span>
                    {time} · {t.cash.kinds[m.kind]} · {m.concept}
                    {isReversal && ` · ${t.payments.reversalOf}`}
                    {wasReversed && ` · ${t.payments.reversed}`}
                  </span>
                  <span className={m.amount < 0 ? 'text-destructive font-medium' : 'font-medium'}>
                    {formatMoney(m.amount, m.currency)}
                  </span>
                </div>
                {isReversal && m.reason && <p className="text-muted-foreground">{m.reason}</p>}
                {isOwner && canReverse && !isReversal && !wasReversed && reversingId !== m.id && (
                  <div>
                    <Button
                      size="sm"
                      variant="outline"
                      aria-label={`${t.payments.reverse} ${m.concept}`}
                      onClick={() => {
                        reverse.reset();
                        setReason('');
                        setReasonError(null);
                        setReversingId(m.id);
                      }}
                    >
                      {t.payments.reverse}
                    </Button>
                  </div>
                )}
                {reversingId === m.id && (
                  <form noValidate className="flex flex-col gap-2" onSubmit={(event) => confirm(event, m)}>
                    <Label htmlFor={`cash-reason-${m.id}`}>{t.payments.reasonLabel}</Label>
                    <Input
                      id={`cash-reason-${m.id}`}
                      value={reason}
                      maxLength={200}
                      placeholder={t.payments.reasonPlaceholder}
                      onChange={(event) => setReason(event.target.value)}
                    />
                    {reasonError && <p className="text-destructive text-sm">{reasonError}</p>}
                    {reverse.isError && (
                      <p role="alert" className="text-destructive text-sm">
                        {errorMessage(reverse.error)}
                      </p>
                    )}
                    <div className="flex gap-2">
                      <Button type="submit" size="sm" variant="destructive" disabled={reverse.isPending}>
                        {t.payments.confirmReverse}
                      </Button>
                      <Button type="button" size="sm" variant="ghost" onClick={() => setReversingId(null)}>
                        {t.payments.cancelReverse}
                      </Button>
                    </div>
                  </form>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </div>
  );
}

function ClosingForm({ date }: { date: string }) {
  const close = useCloseCashDay();
  const [counted, setCounted] = useState('');
  const [note, setNote] = useState('');
  const [error, setError] = useState<string | null>(null);

  const submit = (event: FormEvent) => {
    event.preventDefault();
    // Zero is a valid count (empty drawer); parseAmount rejects it, so handle it explicitly.
    const parsed = counted.trim() === '0' ? 0 : parseAmount(counted);
    if (parsed === null) return setError(t.cash.validation.countedInvalid);
    setError(null);
    close.mutate({ date, countedAmount: parsed, note: note.trim() || null });
  };

  return (
    <form
      noValidate
      aria-label={t.cash.closing.title}
      className="bg-card flex flex-col gap-3 rounded-xl border p-4 shadow-sm"
      onSubmit={submit}
    >
      <h2 className="text-lg font-semibold">{t.cash.closing.title}</h2>
      <div className="grid gap-3 sm:grid-cols-2">
        <div className="flex flex-col gap-2">
          <Label htmlFor="cash-counted">{t.cash.closing.counted}</Label>
          <Input
            id="cash-counted"
            inputMode="decimal"
            value={counted}
            aria-invalid={!!error}
            onChange={(event) => setCounted(event.target.value)}
          />
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="cash-note">{t.cash.closing.note}</Label>
          <Input
            id="cash-note"
            value={note}
            maxLength={300}
            onChange={(event) => setNote(event.target.value)}
          />
        </div>
      </div>
      {error && <p className="text-destructive text-sm">{error}</p>}
      {close.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(close.error)}
        </p>
      )}
      <div>
        <Button type="submit" disabled={close.isPending}>
          {close.isPending ? t.cash.closing.submitting : t.cash.closing.submit}
        </Button>
      </div>
    </form>
  );
}

function ClosingSummary({ day }: { day: CashDay }) {
  const closing = day.closing;
  if (!closing) return null;
  const money = (amount: number) => formatMoney(amount, closing.currency);
  const verdict =
    closing.difference === 0
      ? t.cash.closing.exact
      : closing.difference > 0
        ? t.cash.closing.over
        : t.cash.closing.short;

  return (
    <section
      aria-label={t.cash.closing.closedTitle}
      className="bg-card flex flex-col gap-2 rounded-xl border p-4 shadow-sm"
    >
      <h2 className="text-lg font-semibold">{t.cash.closing.closedTitle}</h2>
      <dl className="grid gap-2 text-sm sm:grid-cols-3">
        <div>
          <dt className="text-muted-foreground">{t.cash.closing.expectedAtClose}</dt>
          <dd>{money(closing.expected)}</dd>
        </div>
        <div>
          <dt className="text-muted-foreground">{t.cash.closing.countedAtClose}</dt>
          <dd>{money(closing.counted)}</dd>
        </div>
        <div>
          <dt className="text-muted-foreground">{t.cash.closing.difference}</dt>
          <dd className={closing.difference === 0 ? 'font-medium' : 'text-destructive font-medium'}>
            {money(closing.difference)}
          </dd>
        </div>
      </dl>
      <p className="text-sm">{verdict}</p>
      {closing.note && <p className="text-muted-foreground text-sm">{closing.note}</p>}
      <p className="text-muted-foreground text-xs">{t.cash.closing.closedHint}</p>
    </section>
  );
}
