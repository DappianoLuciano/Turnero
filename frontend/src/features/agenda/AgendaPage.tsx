import { ChevronLeft, ChevronRight, Plus } from 'lucide-react';
import { useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { useBusiness } from '@/features/business/business-api';
import { useServices } from '@/features/services/services-api';
import { dayAvailability } from '@/features/schedules/availability';
import { useScheduleExceptions, useSchedules } from '@/features/schedules/schedules-api';
import { useProfessionals } from '@/features/staff/staff-api';
import { errorMessage } from '@/shared/api/problem';
import { locale, t } from '@/shared/i18n';
import {
  addDays,
  isValidDate,
  parseTime,
  toLocal,
  todayIn,
  toUtcIso,
  formatMinutes,
} from '@/shared/time/business-time';
import { DateField } from '@/shared/time/DateField';
import { Button } from '@/shared/ui/button';
import { Label } from '@/shared/ui/label';
import { useAppointments, useCreateAppointment, useUpdateAppointment } from './agenda-api';
import { AgendaGrid } from './AgendaGrid';
import { AppointmentDetails } from './AppointmentDetails';
import { AppointmentForm, type AppointmentFormInitial, type AppointmentFormValues } from './AppointmentForm';

type Panel =
  | { kind: 'create'; professionalId: string; minutes: number }
  | { kind: 'edit'; appointmentId: string }
  | null;

const dayLabel = (date: string) =>
  new Intl.DateTimeFormat(locale, { weekday: 'long', day: 'numeric', month: 'long', timeZone: 'UTC' }).format(
    new Date(`${date}T12:00:00Z`),
  );

export function AgendaPage() {
  const business = useBusiness();
  const timeZone = business.data?.timeZone ?? null;
  const [params, setParams] = useSearchParams();
  const requested = params.get('date');
  const date = timeZone ? (isValidDate(requested) ? requested : todayIn(timeZone)) : null;

  const appointments = useAppointments(date);
  const professionals = useProfessionals();
  const services = useServices();
  const schedules = useSchedules();
  // Closed days around the viewed day, so the form can warn about nearby dates too.
  const exceptions = useScheduleExceptions(date ? addDays(date, -30) : null, date ? addDays(date, 60) : null);
  const availabilityOn = (professionalId: string, on: string) =>
    dayAvailability(schedules.data ?? [], exceptions.data ?? [], professionalId, on);
  const create = useCreateAppointment();
  const update = useUpdateAppointment();
  const [panel, setPanel] = useState<Panel>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);

  const goTo = (next: string) => {
    setParams({ date: next }, { replace: true });
    setSelectedId(null);
  };

  const closePanel = () => {
    create.reset();
    update.reset();
    setPanel(null);
  };

  const dayAppointments = appointments.data ?? [];
  const selected = dayAppointments.find((a) => a.id === selectedId) ?? null;
  const editing =
    panel?.kind === 'edit' ? dayAppointments.find((a) => a.id === panel.appointmentId) : undefined;

  const columns = (professionals.data ?? []).filter(
    (p) => p.isActive || dayAppointments.some((a) => a.professionalId === p.id),
  );
  const hasActiveProfessionals = (professionals.data ?? []).some((p) => p.isActive);

  const openCreate = (professionalId: string, minutes: number) => {
    create.reset();
    setSelectedId(null);
    setPanel({ kind: 'create', professionalId, minutes });
  };

  const initialFor = (): AppointmentFormInitial | null => {
    if (!date) return null;
    if (panel?.kind === 'create') {
      return {
        customer: null,
        professionalId: panel.professionalId,
        serviceId: '',
        date,
        time: formatMinutes(panel.minutes),
        notes: '',
      };
    }
    if (editing && timeZone) {
      const local = toLocal(editing.startsAt, timeZone);
      return {
        customer: { id: editing.customerId, name: editing.customerName },
        professionalId: editing.professionalId,
        serviceId: editing.serviceId,
        date: local.date,
        time: formatMinutes(local.minutes),
        notes: editing.notes ?? '',
      };
    }
    return null;
  };

  const submit = (values: AppointmentFormValues) => {
    if (!timeZone) return;
    const input = {
      professionalId: values.professionalId,
      serviceId: values.serviceId,
      startsAt: toUtcIso(values.date, parseTime(values.time), timeZone),
      notes: values.notes || null,
    };
    const done = () => {
      closePanel();
      goTo(values.date);
    };
    if (panel?.kind === 'edit') {
      update.mutate({ id: panel.appointmentId, ...input }, { onSuccess: done });
    } else {
      create.mutate({ customerId: values.customerId, ...input }, { onSuccess: done });
    }
  };

  const initial = initialFor();
  const loading = business.isPending || professionals.isPending || services.isPending;
  const failure = business.error ?? professionals.error ?? services.error ?? appointments.error;

  return (
    <section className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h1 className="text-2xl font-semibold">{t.agenda.title}</h1>
        {hasActiveProfessionals && date && !panel && (
          <Button onClick={() => openCreate(columns.find((p) => p.isActive)?.id ?? '', 9 * 60)}>
            <Plus />
            {t.agenda.new}
          </Button>
        )}
      </div>

      {date && (
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
          <Button variant="outline" onClick={() => timeZone && goTo(todayIn(timeZone))}>
            {t.agenda.today}
          </Button>
          <Label htmlFor="agenda-date" className="sr-only">
            {t.agenda.dateLabel}
          </Label>
          <DateField
            id="agenda-date"
            className="w-36"
            value={date}
            onChange={(next) => isValidDate(next) && goTo(next)}
          />
          <p className="text-muted-foreground text-sm capitalize">{dayLabel(date)}</p>
        </div>
      )}

      {loading && <p className="text-muted-foreground">{t.app.loading}</p>}
      {failure != null && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(failure)}
        </p>
      )}

      {!loading && !hasActiveProfessionals && professionals.isSuccess && (
        <div className="flex flex-col items-start gap-2">
          <p className="text-muted-foreground">{t.agenda.noProfessionals}</p>
          <Button asChild variant="outline">
            <Link to="/app/staff">{t.agenda.goToStaff}</Link>
          </Button>
        </div>
      )}

      {panel && initial && (
        <AppointmentForm
          key={panel.kind === 'edit' ? panel.appointmentId : `new-${panel.professionalId}-${panel.minutes}`}
          mode={panel.kind}
          initial={initial}
          professionals={professionals.data ?? []}
          services={services.data ?? []}
          availabilityOn={availabilityOn}
          pending={create.isPending || update.isPending}
          error={panel.kind === 'edit' ? update.error : create.error}
          onSubmit={submit}
          onCancel={closePanel}
        />
      )}

      {selected && !panel && timeZone && (
        <AppointmentDetails
          key={selected.id}
          appointment={selected}
          timeZone={timeZone}
          onEdit={() => {
            update.reset();
            setPanel({ kind: 'edit', appointmentId: selected.id });
          }}
          onClose={() => setSelectedId(null)}
        />
      )}

      {timeZone && columns.length > 0 && (
        <>
          <AgendaGrid
            professionals={columns}
            appointments={dayAppointments}
            timeZone={timeZone}
            selectedId={selectedId}
            availabilityOf={(professionalId) => availabilityOn(professionalId, date ?? '')}
            onSlotClick={openCreate}
            onSelect={(appointment) => {
              closePanel();
              setSelectedId(appointment.id);
            }}
          />
          {appointments.isSuccess && dayAppointments.length === 0 && (
            <p className="text-muted-foreground text-sm">{t.agenda.empty}</p>
          )}
        </>
      )}
    </section>
  );
}
