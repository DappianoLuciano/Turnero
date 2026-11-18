import { useState, type FormEvent } from 'react';
import { useSession } from '@/features/auth/use-session';
import { errorMessage } from '@/shared/api/problem';
import { formatMoney, parseAmount } from '@/shared/format/money';
import { t } from '@/shared/i18n';
import { formatDate, formatMinutes, toLocal } from '@/shared/time/business-time';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Label } from '@/shared/ui/label';
import {
  paymentMethods,
  useAppointmentPayments,
  useCollectPayment,
  useReversePayment,
  type Payment,
  type PaymentMethod,
} from './payments-api';

const selectClass =
  'border-input bg-background focus-visible:ring-ring/50 h-9 w-full rounded-md border px-3 text-sm shadow-xs outline-none focus-visible:ring-[3px]';

const amountText = (amount: number) => String(amount).replace('.', ',');

interface Props {
  appointmentId: string;
  timeZone: string;
}

/** Collections of one appointment: balance, movements, a form to collect, and (owner) reversals. */
export function AppointmentPayments({ appointmentId, timeZone }: Props) {
  const session = useSession();
  const isOwner = session.status === 'authenticated' && session.user.role === 'Owner';
  const query = useAppointmentPayments(appointmentId);
  const collect = useCollectPayment();
  const reverse = useReversePayment();

  const [typedAmount, setTypedAmount] = useState<string | null>(null);
  const [method, setMethod] = useState<PaymentMethod>('Cash');
  const [amountError, setAmountError] = useState<string | null>(null);
  const [reversingId, setReversingId] = useState<string | null>(null);
  const [reason, setReason] = useState('');
  const [reasonError, setReasonError] = useState<string | null>(null);

  if (query.isPending) return <p className="text-muted-foreground text-sm">{t.app.loading}</p>;
  if (query.isError) {
    return (
      <p role="alert" className="text-destructive text-sm">
        {errorMessage(query.error)}
      </p>
    );
  }

  const summary = query.data;
  const money = (amount: number) => formatMoney(amount, summary.currency);
  const reversedIds = new Set(summary.payments.flatMap((p) => (p.reversesId ? [p.reversesId] : [])));
  const shownAmount = typedAmount ?? (summary.balance > 0 ? amountText(summary.balance) : '');

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const amount = parseAmount(shownAmount);
    if (amount === null) {
      setAmountError(t.payments.validation.amountInvalid);
      return;
    }
    setAmountError(null);
    collect.mutate({ appointmentId, amount, method }, { onSuccess: () => setTypedAmount(null) });
  };

  const confirmReverse = (event: FormEvent, payment: Payment) => {
    event.preventDefault();
    if (!reason.trim()) {
      setReasonError(t.payments.validation.reasonRequired);
      return;
    }
    setReasonError(null);
    reverse.mutate(
      { id: payment.id, reason: reason.trim() },
      {
        onSuccess: () => {
          setReversingId(null);
          setReason('');
        },
      },
    );
  };

  const when = (instant: string) => {
    const local = toLocal(instant, timeZone);
    return `${formatDate(local.date)} ${formatMinutes(local.minutes)}`;
  };

  return (
    <section aria-label={t.payments.section} className="flex flex-col gap-3 border-t pt-3">
      <h3 className="text-sm font-semibold">{t.payments.section}</h3>

      <dl className="grid grid-cols-3 gap-2 text-sm">
        <div>
          <dt className="text-muted-foreground">{t.payments.price}</dt>
          <dd>{money(summary.price)}</dd>
        </div>
        <div>
          <dt className="text-muted-foreground">{t.payments.paid}</dt>
          <dd>{money(summary.paid)}</dd>
        </div>
        <div>
          <dt className="text-muted-foreground">{t.payments.balance}</dt>
          <dd className="font-medium">
            {summary.balance > 0 ? money(summary.balance) : t.payments.fullyPaid}
          </dd>
        </div>
      </dl>

      {summary.payments.length === 0 ? (
        <p className="text-muted-foreground text-sm">{t.payments.none}</p>
      ) : (
        <ul className="flex flex-col gap-2 text-sm">
          {summary.payments.map((payment) => {
            const isReversal = payment.reversesId != null;
            const wasReversed = reversedIds.has(payment.id);
            return (
              <li key={payment.id} className="flex flex-col gap-2 rounded-md border p-2">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <span>
                    {when(payment.createdAt)} · {t.payments.methods[payment.method]}
                    {isReversal && ` · ${t.payments.reversalOf}`}
                    {wasReversed && ` · ${t.payments.reversed}`}
                  </span>
                  <span className={payment.amount < 0 ? 'text-destructive font-medium' : 'font-medium'}>
                    {money(payment.amount)}
                  </span>
                </div>
                {isReversal && payment.reason && <p className="text-muted-foreground">{payment.reason}</p>}
                {isOwner && !isReversal && !wasReversed && reversingId !== payment.id && (
                  <div>
                    <Button
                      size="sm"
                      variant="outline"
                      aria-label={`${t.payments.reverse} ${money(payment.amount)}`}
                      onClick={() => {
                        reverse.reset();
                        setReason('');
                        setReasonError(null);
                        setReversingId(payment.id);
                      }}
                    >
                      {t.payments.reverse}
                    </Button>
                  </div>
                )}
                {reversingId === payment.id && (
                  <form
                    noValidate
                    className="flex flex-col gap-2"
                    onSubmit={(event) => confirmReverse(event, payment)}
                  >
                    <Label htmlFor={`reason-${payment.id}`}>{t.payments.reasonLabel}</Label>
                    <Input
                      id={`reason-${payment.id}`}
                      value={reason}
                      maxLength={200}
                      placeholder={t.payments.reasonPlaceholder}
                      aria-invalid={!!reasonError}
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

      {!summary.acceptsPayments ? (
        <p className="text-muted-foreground text-sm">{t.payments.notPayable}</p>
      ) : (
        summary.balance > 0 && (
          <form noValidate className="grid gap-3 sm:grid-cols-[1fr_1fr_auto] sm:items-end" onSubmit={submit}>
            <div className="flex flex-col gap-2">
              <Label htmlFor={`amount-${appointmentId}`}>{t.payments.amount}</Label>
              <Input
                id={`amount-${appointmentId}`}
                inputMode="decimal"
                value={shownAmount}
                aria-invalid={!!amountError}
                onChange={(event) => setTypedAmount(event.target.value)}
              />
            </div>
            <div className="flex flex-col gap-2">
              <Label htmlFor={`method-${appointmentId}`}>{t.payments.method}</Label>
              <select
                id={`method-${appointmentId}`}
                className={selectClass}
                value={method}
                onChange={(event) => setMethod(event.target.value as PaymentMethod)}
              >
                {paymentMethods.map((m) => (
                  <option key={m} value={m}>
                    {t.payments.methods[m]}
                  </option>
                ))}
              </select>
            </div>
            <Button type="submit" disabled={collect.isPending}>
              {collect.isPending ? t.payments.collecting : t.payments.collect}
            </Button>
            {amountError && <p className="text-destructive text-sm sm:col-span-3">{amountError}</p>}
            {collect.isError && (
              <p role="alert" className="text-destructive text-sm sm:col-span-3">
                {errorMessage(collect.error)}
              </p>
            )}
          </form>
        )
      )}
    </section>
  );
}
