import { useState } from 'react';
import { errorMessage } from '@/shared/api/problem';
import { locale, t } from '@/shared/i18n';
import { addDays, formatDate, formatMinutes, isValidDate, todayIn } from '@/shared/time/business-time';
import { DateField } from '@/shared/time/DateField';
import { Label } from '@/shared/ui/label';
import { useDaySlots, type BookableSlot, type PublicService } from './booking-api';

const DAYS_SHOWN = 14;

const weekday = (date: string) =>
  new Intl.DateTimeFormat(locale, { weekday: 'short', timeZone: 'UTC' }).format(
    new Date(`${date}T12:00:00Z`),
  );

interface Props {
  slug: string;
  timeZone: string;
  service: PublicService;
  professionalIds: string[];
  onPick: (slot: BookableSlot) => void;
}

/** Day chips for the next two weeks (or any other date) and the free times of the chosen day. */
export function TimeStep({ slug, timeZone, service, professionalIds, onPick }: Props) {
  const today = todayIn(timeZone);
  const [date, setDate] = useState(today);
  const days = Array.from({ length: DAYS_SHOWN }, (_, i) => addDays(today, i));
  const { isPending, error, slots, isClosed } = useDaySlots(slug, professionalIds, service.id, date);

  return (
    <section aria-label={t.booking.steps.time} className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold">{t.booking.steps.time}</h2>

      <div className="-mx-4 flex gap-2 overflow-x-auto px-4 pb-1">
        {days.map((day) => (
          <button
            key={day}
            type="button"
            aria-pressed={day === date}
            aria-label={t.booking.time.dayLabel(weekday(day), formatDate(day))}
            className={`flex min-w-16 shrink-0 flex-col items-center rounded-xl border px-3 py-2 text-sm ${
              day === date ? 'bg-primary text-primary-foreground border-primary' : 'bg-card hover:bg-muted'
            }`}
            onClick={() => setDate(day)}
          >
            <span className="capitalize">{weekday(day)}</span>
            <span className="font-medium">{formatDate(day).slice(0, 5)}</span>
          </button>
        ))}
      </div>

      <div className="flex items-center gap-2">
        <Label htmlFor="booking-other-date" className="shrink-0">
          {t.booking.time.otherDate}
        </Label>
        <DateField
          id="booking-other-date"
          className="w-36"
          value={date}
          onChange={(next) => isValidDate(next) && setDate(next)}
        />
      </div>

      {isPending && <p className="text-muted-foreground text-sm">{t.booking.time.loading}</p>}
      {error != null && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(error)}
        </p>
      )}
      {!isPending && error == null && isClosed && (
        <p className="text-muted-foreground text-sm">{t.booking.time.closed}</p>
      )}
      {!isPending && error == null && !isClosed && slots.length === 0 && (
        <p className="text-muted-foreground text-sm">{t.booking.time.none}</p>
      )}

      <div className="grid grid-cols-3 gap-2 sm:grid-cols-4">
        {slots.map((slot) => (
          <button
            key={slot.startsAt}
            type="button"
            aria-label={t.booking.time.slotLabel(formatMinutes(slot.minutes))}
            className="bg-card hover:bg-primary hover:text-primary-foreground rounded-lg border py-3 text-sm font-medium shadow-sm"
            onClick={() => onPick(slot)}
          >
            {formatMinutes(slot.minutes)}
          </button>
        ))}
      </div>
    </section>
  );
}
