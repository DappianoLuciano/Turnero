import type { Professional } from '@/features/staff/staff-api';
import { t } from '@/shared/i18n';
import { formatMinutes, toLocal } from '@/shared/time/business-time';
import { offHoursRanges, type DayAvailability } from '@/features/schedules/availability';
import type { Appointment } from './agenda-api';

const HOUR_PX = 64;
const SLOT_MINUTES = 30;
const MIN_START_HOUR = 8;
const MIN_END_HOUR = 21;
const MINUTES_PER_DAY = 24 * 60;

interface Placed {
  appointment: Appointment;
  start: number;
  end: number;
}

const blockStyle: Record<Appointment['status'], string> = {
  Scheduled: 'bg-primary/10 border-primary text-foreground',
  Completed: 'bg-emerald-100 border-emerald-600 text-emerald-950 dark:bg-emerald-950 dark:text-emerald-50',
  Cancelled: 'bg-muted border-border text-muted-foreground line-through opacity-60',
  NoShow: 'bg-muted border-border text-muted-foreground line-through opacity-60',
};

interface Props {
  professionals: Professional[];
  appointments: Appointment[];
  timeZone: string;
  selectedId: string | null;
  /** Working hours / closed day of each column, to shade what is outside them. */
  availabilityOf: (professionalId: string) => DayAvailability;
  onSlotClick: (professionalId: string, minutes: number) => void;
  onSelect: (appointment: Appointment) => void;
}

/** One day: a column per professional, one row per hour, appointments placed by their local time. */
export function AgendaGrid({
  professionals,
  appointments,
  timeZone,
  selectedId,
  availabilityOf,
  onSlotClick,
  onSelect,
}: Props) {
  const placed: Placed[] = appointments.map((appointment) => {
    const start = toLocal(appointment.startsAt, timeZone).minutes;
    const length =
      (new Date(appointment.endsAt).getTime() - new Date(appointment.startsAt).getTime()) / 60000;
    return { appointment, start, end: Math.min(start + length, MINUTES_PER_DAY) };
  });

  const working = professionals.flatMap((p) => availabilityOf(p.id).ranges);
  const startHour = Math.min(
    MIN_START_HOUR,
    ...placed.map((p) => Math.floor(p.start / 60)),
    ...working.map((r) => Math.floor(r.start / 60)),
  );
  const endHour = Math.min(
    24,
    Math.max(
      MIN_END_HOUR,
      ...placed.map((p) => Math.ceil(p.end / 60)),
      ...working.map((r) => Math.ceil(r.end / 60)),
    ),
  );
  const height = (endHour - startHour) * HOUR_PX;
  const hours = Array.from({ length: endHour - startHour }, (_, i) => startHour + i);
  const slots = Array.from(
    { length: ((endHour - startHour) * 60) / SLOT_MINUTES },
    (_, i) => startHour * 60 + i * SLOT_MINUTES,
  );
  const px = (minutes: number) => ((minutes - startHour * 60) / 60) * HOUR_PX;

  return (
    <div className="overflow-x-auto rounded-xl border">
      <div className="flex min-w-fit">
        <div className="bg-card sticky left-0 z-20 w-14 shrink-0 border-r">
          <div className="h-10 border-b" />
          <div className="relative" style={{ height }}>
            {hours.map((hour) => (
              <span
                key={hour}
                className="text-muted-foreground absolute right-2 -translate-y-1/2 text-xs"
                style={{ top: px(hour * 60) }}
              >
                {formatMinutes(hour * 60)}
              </span>
            ))}
          </div>
        </div>

        {professionals.map((professional) => {
          const availability = availabilityOf(professional.id);
          const off = offHoursRanges(availability, startHour * 60, endHour * 60);
          return (
            <div key={professional.id} className="w-44 min-w-44 flex-1 border-r last:border-r-0">
              <div className="bg-card flex h-10 items-center justify-center border-b px-2 text-sm font-medium">
                <span className="truncate">{professional.name}</span>
              </div>
              <div className="relative" style={{ height }}>
                {off.map((range) => (
                  <div
                    key={range.start}
                    aria-hidden
                    data-testid="off-hours"
                    className="bg-muted/70 pointer-events-none absolute inset-x-0"
                    style={{ top: px(range.start), height: px(range.end) - px(range.start) }}
                  />
                ))}
                {availability.closed && (
                  <span className="text-muted-foreground pointer-events-none absolute inset-x-0 top-2 z-10 text-center text-xs">
                    {t.schedules.agenda.closedLabel}
                  </span>
                )}
                {hours.map((hour) => (
                  <div
                    key={hour}
                    className="border-border absolute inset-x-0 border-t"
                    style={{ top: px(hour * 60) }}
                  />
                ))}
                {slots.map((minutes) => (
                  <button
                    key={minutes}
                    type="button"
                    aria-label={t.agenda.slotLabel(professional.name, formatMinutes(minutes))}
                    className="hover:bg-primary/5 focus-visible:bg-primary/10 absolute inset-x-0 cursor-pointer"
                    style={{ top: px(minutes), height: (SLOT_MINUTES / 60) * HOUR_PX }}
                    onClick={() => onSlotClick(professional.id, minutes)}
                  />
                ))}
                {placed
                  .filter((p) => p.appointment.professionalId === professional.id)
                  .map(({ appointment, start, end }) => (
                    <button
                      key={appointment.id}
                      type="button"
                      aria-label={t.agenda.appointmentLabel(appointment.customerName, formatMinutes(start))}
                      aria-pressed={appointment.id === selectedId}
                      className={`absolute inset-x-1 z-10 overflow-hidden rounded-md border-l-4 px-2 py-1 text-left text-xs shadow-sm ${blockStyle[appointment.status]} ${appointment.id === selectedId ? 'ring-ring ring-2' : ''}`}
                      style={{ top: px(start), height: Math.max(((end - start) / 60) * HOUR_PX - 2, 24) }}
                      onClick={() => onSelect(appointment)}
                    >
                      <span className="block truncate font-medium">{appointment.customerName}</span>
                      <span className="block truncate">
                        {formatMinutes(start)} · {appointment.serviceName}
                      </span>
                    </button>
                  ))}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
