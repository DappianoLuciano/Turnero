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

interface PickerProps {
  /** Heading of the section. */
  title: string;
  timeZone: string;
  date: string;
  onDateChange: (date: string) => void;
  isPending: boolean;
  error: unknown;
  isClosed: boolean;
  slots: { startsAt: string; minutes: number }[];
  onPick: (slot: { startsAt: string; minutes: number }) => void;
}

/** Day chips for the next two weeks (or any other date) and the free times of the chosen day. */
export function DayTimePicker({
  title,
  timeZone,
  date,
  onDateChange,
  isPending,
  error,
  isClosed,
  slots,
  onPick,
}: PickerProps) {
  const today = todayIn(timeZone);
  const days = Array.from({ length: DAYS_SHOWN }, (_, i) => addDays(today, i));

  return (
    <section aria-label={title} className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold">{title}</h2>

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
            onClick={() => onDateChange(day)}
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
          onChange={(next) => isValidDate(next) && onDateChange(next)}
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

interface Props {
  slug: string;
  timeZone: string;
  service: PublicService;
  professionalIds: string[];
  onPick: (slot: BookableSlot) => void;
}

/** Booking step: free times of the chosen service for one or several professionals. */
export function TimeStep({ slug, timeZone, service, professionalIds, onPick }: Props) {
  const [date, setDate] = useState(() => todayIn(timeZone));
  const { isPending, error, slots, isClosed } = useDaySlots(slug, professionalIds, service.id, date);

  return (
    <DayTimePicker
      title={t.booking.steps.time}
      timeZone={timeZone}
      date={date}
      onDateChange={setDate}
      isPending={isPending}
      error={error}
      isClosed={isClosed}
      slots={slots}
      onPick={(picked) => {
        const slot = slots.find((s) => s.startsAt === picked.startsAt);
        if (slot) onPick(slot);
      }}
    />
  );
}
