/**
 * Conversions between UTC instants and the local wall-clock time of a business (IANA time zone).
 * The browser's own zone is never used: the agenda always shows the business's time.
 * Dates are `YYYY-MM-DD` strings; times of day are minutes since local midnight.
 */

const formatters = new Map<string, Intl.DateTimeFormat>();

function formatter(timeZone: string): Intl.DateTimeFormat {
  let cached = formatters.get(timeZone);
  if (!cached) {
    cached = new Intl.DateTimeFormat('en-US', {
      timeZone,
      hourCycle: 'h23',
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
    });
    formatters.set(timeZone, cached);
  }
  return cached;
}

interface LocalFields {
  year: number;
  month: number;
  day: number;
  hour: number;
  minute: number;
  second: number;
}

function localFields(utcMs: number, timeZone: string): LocalFields {
  const parts = formatter(timeZone).formatToParts(new Date(utcMs));
  const get = (type: Intl.DateTimeFormatPartTypes) => Number(parts.find((p) => p.type === type)?.value);
  return {
    year: get('year'),
    month: get('month'),
    day: get('day'),
    hour: get('hour'),
    minute: get('minute'),
    second: get('second'),
  };
}

const pad = (n: number) => String(n).padStart(2, '0');

/** Offset of the zone from UTC at the given instant, in minutes (Buenos Aires: -180). */
export function zoneOffsetMinutes(utcMs: number, timeZone: string): number {
  const f = localFields(utcMs, timeZone);
  const asIfUtc = Date.UTC(f.year, f.month - 1, f.day, f.hour, f.minute, f.second);
  return Math.round((asIfUtc - Math.floor(utcMs / 1000) * 1000) / 60000);
}

/** Local calendar date and minutes since local midnight of a UTC instant. */
export function toLocal(instant: string | Date, timeZone: string): { date: string; minutes: number } {
  const f = localFields(new Date(instant).getTime(), timeZone);
  return { date: `${f.year}-${pad(f.month)}-${pad(f.day)}`, minutes: f.hour * 60 + f.minute };
}

/** Today's local date in the business zone. */
export function todayIn(timeZone: string, now: Date = new Date()): string {
  return toLocal(now, timeZone).date;
}

/** The UTC instant (ISO 8601) of a local date and time of day in the business zone. */
export function toUtcIso(date: string, minutes: number, timeZone: string): string {
  const [year, month, day] = date.split('-').map(Number) as [number, number, number];
  const wallClockAsUtc = Date.UTC(year, month - 1, day, 0, minutes);
  // The offset depends on the instant itself: refine once around the guess.
  let utc = wallClockAsUtc - zoneOffsetMinutes(wallClockAsUtc, timeZone) * 60000;
  utc = wallClockAsUtc - zoneOffsetMinutes(utc, timeZone) * 60000;
  return new Date(utc).toISOString();
}

export function addDays(date: string, days: number): string {
  const [year, month, day] = date.split('-').map(Number) as [number, number, number];
  const moved = new Date(Date.UTC(year, month - 1, day + days));
  return `${moved.getUTCFullYear()}-${pad(moved.getUTCMonth() + 1)}-${pad(moved.getUTCDate())}`;
}

export const isValidDate = (value: string | null | undefined): value is string =>
  !!value && /^\d{4}-\d{2}-\d{2}$/.test(value) && !Number.isNaN(Date.parse(`${value}T00:00:00Z`));

/** `YYYY-MM-DD` → `dd/mm/aaaa` (Argentine format). Anything that is not a valid date gives an empty string. */
export function formatDate(value: string | null | undefined): string {
  if (!isValidDate(value)) return '';
  const [year, month, day] = value.split('-') as [string, string, string];
  return `${day}/${month}/${year}`;
}

/**
 * `dd/mm/aaaa` (also `d/m/aaaa`, with `-` or `.` as separators) → `YYYY-MM-DD`, or `null` if it is not a real date.
 * Month and day are never swapped: the first number is always the day.
 */
export function parseDate(text: string): string | null {
  const match = /^(\d{1,2})[/\-.](\d{1,2})[/\-.](\d{4})$/.exec(text.trim());
  if (!match) return null;
  const [, day, month, year] = match as unknown as [string, string, string, string];
  const iso = `${year}-${pad(Number(month))}-${pad(Number(day))}`;
  // Reject overflow such as 31/02: the normalized date must be the same one.
  const check = new Date(`${iso}T00:00:00Z`);
  return !Number.isNaN(check.getTime()) && check.toISOString().startsWith(iso) ? iso : null;
}

/** `HH:mm` of minutes since midnight. */
export const formatMinutes = (minutes: number): string =>
  `${pad(Math.floor(minutes / 60))}:${pad(minutes % 60)}`;

/** Minutes since midnight of an `HH:mm` string. */
export function parseTime(value: string): number {
  const [hours, minutes] = value.split(':').map(Number) as [number, number];
  return hours * 60 + minutes;
}
