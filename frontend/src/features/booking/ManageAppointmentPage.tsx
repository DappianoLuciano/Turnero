import { CheckCircle2 } from 'lucide-react';
import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { ApiError, errorMessage } from '@/shared/api/problem';
import { formatMoney } from '@/shared/format/money';
import { t } from '@/shared/i18n';
import { addDays, todayIn } from '@/shared/time/business-time';
import { Button } from '@/shared/ui/button';
import { WhenText } from './WhenText';
import { DayTimePicker } from './TimeStep';
import {
  useAppointmentSlots,
  useCancelAppointment,
  usePublicAppointment,
  useRescheduleAppointment,
} from './manage-api';
import { usePublicCatalog } from './booking-api';

type Mode = 'view' | 'pick' | 'confirmMove' | 'confirmCancel';

/**
 * Public page of one appointment (/r/:slug/turno/:token): the customer sees it and, while the business's notice
 * window allows it, moves or cancels it. The token in the link is the only credential.
 */
export function ManageAppointmentPage() {
  const { slug = '', token = '' } = useParams();
  const appointment = usePublicAppointment(slug, token);
  const catalog = usePublicCatalog(slug);
  const reschedule = useRescheduleAppointment(slug, token);
  const cancel = useCancelAppointment(slug, token);

  const timeZone = catalog.data?.business.timeZone ?? 'America/Argentina/Buenos_Aires';
  const [mode, setMode] = useState<Mode>('view');
  const [date, setDate] = useState(() => addDays(todayIn(timeZone), 1));
  const [target, setTarget] = useState<string | null>(null);
  const slots = useAppointmentSlots(slug, token, date, mode === 'pick');
  const [done, setDone] = useState<'moved' | 'cancelled' | null>(null);

  if (appointment.isPending || catalog.isPending) {
    return (
      <Shell>
        <p className="text-muted-foreground text-center">{t.app.loading}</p>
      </Shell>
    );
  }

  if (appointment.isError || catalog.isError) {
    const error = appointment.error ?? catalog.error;
    const notFound = error instanceof ApiError && error.status === 404;
    const closed = error instanceof ApiError && error.status === 402;
    return (
      <Shell>
        {notFound || closed ? (
          <div className="flex flex-col gap-2 text-center">
            <h1 className="text-2xl font-semibold">
              {closed ? t.booking.closed.title : t.booking.manage.notFound.title}
            </h1>
            <p className="text-muted-foreground">
              {closed ? t.booking.closed.description : t.booking.manage.notFound.description}
            </p>
          </div>
        ) : (
          <p role="alert" className="text-destructive text-center text-sm">
            {errorMessage(error)}
          </p>
        )}
      </Shell>
    );
  }

  const a = appointment.data;
  const timeZoneOfBusiness = catalog.data.business.timeZone;
  const back = () => {
    reschedule.reset();
    cancel.reset();
    setTarget(null);
    setMode('view');
  };

  const confirmMove = () =>
    target &&
    reschedule.mutate(target, {
      onSuccess: () => {
        setDone('moved');
        back();
      },
      onError: () => setMode('pick'),
    });
  const confirmCancel = () =>
    cancel.mutate(undefined, {
      onSuccess: () => {
        setDone('cancelled');
        back();
      },
    });

  return (
    <Shell>
      <header className="flex flex-col gap-1">
        <p className="text-muted-foreground text-sm font-semibold">{a.businessName}</p>
        <h1 className="text-2xl font-semibold">{t.booking.manage.title}</h1>
      </header>

      {done && (
        <p
          role="status"
          className="flex items-center gap-2 rounded-md bg-emerald-50 px-4 py-3 text-sm text-emerald-900"
        >
          <CheckCircle2 className="size-5 shrink-0" aria-hidden />
          {done === 'moved' ? t.booking.manage.moved : t.booking.manage.cancelled}
        </p>
      )}

      <dl className="bg-card grid w-full grid-cols-[auto_1fr] gap-x-4 gap-y-2 rounded-xl border p-4 text-sm shadow-sm">
        <dt className="text-muted-foreground">{t.booking.confirmed.when}</dt>
        <dd>
          <WhenText startsAt={a.startsAt} timeZone={timeZoneOfBusiness} />
        </dd>
        <dt className="text-muted-foreground">{t.booking.confirmed.what}</dt>
        <dd>{a.serviceName}</dd>
        <dt className="text-muted-foreground">{t.booking.confirmed.who}</dt>
        <dd>{a.professionalName}</dd>
        <dt className="text-muted-foreground">{t.booking.confirmed.price}</dt>
        <dd>{formatMoney(a.price, a.currency)}</dd>
        <dt className="text-muted-foreground">{t.booking.manage.state}</dt>
        <dd>{t.agenda.status[a.status]}</dd>
      </dl>

      {mode === 'pick' && (
        <>
          {reschedule.isError && (
            <p role="alert" className="text-destructive text-sm">
              {errorMessage(reschedule.error)}
            </p>
          )}
          <DayTimePicker
            title={t.booking.manage.pickTime}
            timeZone={timeZoneOfBusiness}
            date={date}
            onDateChange={setDate}
            isPending={slots.isPending}
            error={slots.error}
            isClosed={slots.data?.isClosed ?? false}
            slots={slots.data?.slots ?? []}
            onPick={(slot) => {
              reschedule.reset();
              setTarget(slot.startsAt);
              setMode('confirmMove');
            }}
          />
          <Button variant="outline" onClick={back}>
            {t.booking.back}
          </Button>
        </>
      )}

      {mode === 'confirmMove' && target && (
        <div role="group" aria-label={t.booking.manage.confirmMoveTitle} className="flex flex-col gap-3">
          <p className="font-medium">
            {t.booking.manage.confirmMoveBefore} <WhenText startsAt={target} timeZone={timeZoneOfBusiness} />
            {t.booking.manage.confirmMoveAfter}
          </p>
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => setMode('pick')}>
              {t.booking.back}
            </Button>
            <Button disabled={reschedule.isPending} onClick={confirmMove}>
              {reschedule.isPending ? t.booking.manage.moving : t.booking.manage.confirmMoveYes}
            </Button>
          </div>
        </div>
      )}

      {mode === 'confirmCancel' && (
        <div role="group" aria-label={t.booking.manage.confirmCancelTitle} className="flex flex-col gap-3">
          <p className="font-medium">{t.booking.manage.confirmCancel}</p>
          {cancel.isError && (
            <p role="alert" className="text-destructive text-sm">
              {errorMessage(cancel.error)}
            </p>
          )}
          <div className="flex gap-2">
            <Button variant="outline" onClick={back}>
              {t.booking.manage.keep}
            </Button>
            <Button variant="destructive" disabled={cancel.isPending} onClick={confirmCancel}>
              {cancel.isPending ? t.booking.manage.cancelling : t.booking.manage.confirmCancelYes}
            </Button>
          </div>
        </div>
      )}

      {mode === 'view' && a.status === 'Scheduled' && a.canChange && (
        <div className="flex flex-col gap-2 sm:flex-row">
          <Button size="lg" onClick={() => setMode('pick')}>
            {t.booking.manage.change}
          </Button>
          <Button size="lg" variant="outline" onClick={() => setMode('confirmCancel')}>
            {t.booking.manage.cancel}
          </Button>
        </div>
      )}

      {mode === 'view' && a.status === 'Scheduled' && !a.canChange && (
        <p className="text-muted-foreground text-sm">
          {t.booking.manage.tooLate(a.noticeHours)}{' '}
          {a.businessPhone ? t.booking.manage.contact(a.businessPhone) : t.booking.manage.contactBusiness}
        </p>
      )}

      {mode === 'view' && a.status !== 'Scheduled' && (
        <Button asChild variant="outline">
          <Link to={`/r/${slug}`}>{t.booking.manage.bookAnother}</Link>
        </Button>
      )}
    </Shell>
  );
}

function Shell({ children }: { children: React.ReactNode }) {
  return <main className="mx-auto flex min-h-full w-full max-w-lg flex-col gap-5 p-4 pb-10">{children}</main>;
}
