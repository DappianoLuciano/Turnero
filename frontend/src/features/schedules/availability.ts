import type { ScheduleException, WorkingBlock } from './schedules-api';

export interface MinuteRange {
  start: number;
  end: number;
}

export interface DayAvailability {
  /** The professional has a weekly schedule: only then can the agenda tell what is "outside hours". */
  configured: boolean;
  closed: boolean;
  closedReason: string | null;
  ranges: MinuteRange[];
}

/** 0 = Sunday ... 6 = Saturday of a `YYYY-MM-DD` calendar date. */
export const dayOfWeekOf = (date: string): number => new Date(`${date}T00:00:00Z`).getUTCDay();

/** What a professional offers on a local date: weekly blocks of that weekday, unless the day is closed. */
export function dayAvailability(
  blocks: WorkingBlock[],
  exceptions: ScheduleException[],
  professionalId: string,
  date: string,
): DayAvailability {
  const mine = blocks.filter((b) => b.professionalId === professionalId);
  const closure = exceptions.find(
    (e) => e.date === date && (e.professionalId == null || e.professionalId === professionalId),
  );
  const weekday = dayOfWeekOf(date);
  return {
    configured: mine.length > 0,
    closed: closure !== undefined,
    closedReason: closure?.reason ?? null,
    ranges: mine
      .filter((b) => b.dayOfWeek === weekday)
      .map((b) => ({ start: b.startMinute, end: b.endMinute }))
      .sort((a, b) => a.start - b.start),
  };
}

/** True when [start, end) is not entirely inside one working block (or the day is closed). */
export function isOutsideHours(availability: DayAvailability, start: number, end: number): boolean {
  if (!availability.configured) return false;
  if (availability.closed) return true;
  return !availability.ranges.some((r) => start >= r.start && end <= r.end);
}

/** The parts of [from, to) that are NOT working time, to shade them on the grid. */
export function offHoursRanges(availability: DayAvailability, from: number, to: number): MinuteRange[] {
  if (!availability.configured) return [];
  if (availability.closed) return [{ start: from, end: to }];
  const off: MinuteRange[] = [];
  let cursor = from;
  for (const range of availability.ranges) {
    if (range.start > cursor) off.push({ start: cursor, end: Math.min(range.start, to) });
    cursor = Math.max(cursor, range.end);
  }
  if (cursor < to) off.push({ start: cursor, end: to });
  return off.filter((r) => r.end > r.start);
}
