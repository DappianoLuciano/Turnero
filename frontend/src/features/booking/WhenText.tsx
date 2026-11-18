import { locale } from '@/shared/i18n';
import { formatDate, formatMinutes, toLocal } from '@/shared/time/business-time';

const weekday = (date: string) =>
  new Intl.DateTimeFormat(locale, { weekday: 'long', timeZone: 'UTC' }).format(new Date(`${date}T12:00:00Z`));

/** "lunes 23/11/2026 · 10:00" in the business's time zone (never the browser's). */
export function WhenText({ startsAt, timeZone }: { startsAt: string; timeZone: string }) {
  const local = toLocal(startsAt, timeZone);
  return (
    <span>
      <span className="capitalize">{weekday(local.date)}</span> {formatDate(local.date)} ·{' '}
      {formatMinutes(local.minutes)}
    </span>
  );
}
