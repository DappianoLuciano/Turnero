import { AppointmentPayments } from '@/features/payments/AppointmentPayments';
import { errorMessage } from '@/shared/api/problem';
import { formatMoney } from '@/shared/format/money';
import { t } from '@/shared/i18n';
import { formatMinutes, toLocal } from '@/shared/time/business-time';
import { Button } from '@/shared/ui/button';
import { useChangeAppointmentStatus, type Appointment, type AppointmentStatus } from './agenda-api';

interface Props {
  appointment: Appointment;
  timeZone: string;
  onEdit: () => void;
  onClose: () => void;
}

export function AppointmentDetails({ appointment, timeZone, onEdit, onClose }: Props) {
  const changeStatus = useChangeAppointmentStatus();
  const start = toLocal(appointment.startsAt, timeZone).minutes;
  const end = toLocal(appointment.endsAt, timeZone).minutes;
  const price = formatMoney(appointment.price, appointment.currency);

  const setStatus = (status: AppointmentStatus) => changeStatus.mutate({ id: appointment.id, status });
  const scheduled = appointment.status === 'Scheduled';

  return (
    <section
      aria-label={t.agenda.appointmentLabel(appointment.customerName, formatMinutes(start))}
      className="bg-card flex flex-col gap-3 rounded-xl border p-4 shadow-sm"
    >
      <div className="flex items-start justify-between gap-2">
        <h2 className="text-lg font-semibold">{appointment.customerName}</h2>
        <span className="bg-muted rounded-full px-2 py-0.5 text-xs">
          {t.agenda.status[appointment.status]}
        </span>
      </div>

      <dl className="grid gap-x-4 gap-y-1 text-sm sm:grid-cols-[auto_1fr]">
        <dt className="text-muted-foreground">{t.agenda.details.service}</dt>
        <dd>{appointment.serviceName}</dd>
        <dt className="text-muted-foreground">{t.agenda.details.professional}</dt>
        <dd>{appointment.professionalName}</dd>
        <dt className="text-muted-foreground">{t.agenda.details.time}</dt>
        <dd>
          {formatMinutes(start)} – {formatMinutes(end)}
        </dd>
        <dt className="text-muted-foreground">{t.agenda.details.price}</dt>
        <dd>{price}</dd>
        {appointment.notes && (
          <>
            <dt className="text-muted-foreground">{t.agenda.details.notes}</dt>
            <dd>{appointment.notes}</dd>
          </>
        )}
      </dl>

      <AppointmentPayments appointmentId={appointment.id} timeZone={timeZone} />

      {changeStatus.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(changeStatus.error)}
        </p>
      )}

      <div className="flex flex-wrap justify-end gap-2">
        {scheduled ? (
          <>
            <Button size="sm" disabled={changeStatus.isPending} onClick={() => setStatus('Completed')}>
              {t.agenda.actions.complete}
            </Button>
            <Button
              size="sm"
              variant="outline"
              disabled={changeStatus.isPending}
              onClick={() => setStatus('NoShow')}
            >
              {t.agenda.actions.noShow}
            </Button>
            <Button size="sm" variant="outline" onClick={onEdit}>
              {t.agenda.actions.edit}
            </Button>
            <Button
              size="sm"
              variant="destructive"
              disabled={changeStatus.isPending}
              onClick={() => setStatus('Cancelled')}
            >
              {t.agenda.actions.cancel}
            </Button>
          </>
        ) : (
          <Button
            size="sm"
            variant="outline"
            disabled={changeStatus.isPending}
            onClick={() => setStatus('Scheduled')}
          >
            {t.agenda.actions.reopen}
          </Button>
        )}
        <Button size="sm" variant="ghost" onClick={onClose}>
          {t.agenda.details.close}
        </Button>
      </div>
    </section>
  );
}
