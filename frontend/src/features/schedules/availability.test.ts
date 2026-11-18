import { describe, expect, it } from 'vitest';
import { dayAvailability, dayOfWeekOf, isOutsideHours, offHoursRanges } from './availability';
import type { ScheduleException, WorkingBlock } from './schedules-api';

const LUCAS = 'lucas';
const MATEO = 'mateo';

// 2026-11-18 is a Wednesday (3).
const WEDNESDAY = '2026-11-18';
const blocks: WorkingBlock[] = [
  { professionalId: LUCAS, dayOfWeek: 3, startMinute: 9 * 60, endMinute: 13 * 60 },
  { professionalId: LUCAS, dayOfWeek: 3, startMinute: 15 * 60, endMinute: 19 * 60 },
  { professionalId: LUCAS, dayOfWeek: 4, startMinute: 9 * 60, endMinute: 12 * 60 },
];
const exception = (date: string, professionalId: string | null): ScheduleException => ({
  id: 'e1',
  date,
  professionalId,
  reason: 'Feriado',
});

describe('availability rules', () => {
  it('knows the weekday of a calendar date', () => {
    expect(dayOfWeekOf(WEDNESDAY)).toBe(3);
    expect(dayOfWeekOf('2026-11-22')).toBe(0);
  });

  it('takes the blocks of that weekday only, sorted', () => {
    const availability = dayAvailability(blocks, [], LUCAS, WEDNESDAY);

    expect(availability.configured).toBe(true);
    expect(availability.closed).toBe(false);
    expect(availability.ranges).toEqual([
      { start: 540, end: 780 },
      { start: 900, end: 1140 },
    ]);
  });

  it('a professional without any schedule is not configured, so nothing is outside hours', () => {
    const availability = dayAvailability(blocks, [], MATEO, WEDNESDAY);

    expect(availability.configured).toBe(false);
    expect(isOutsideHours(availability, 3 * 60, 4 * 60)).toBe(false);
    expect(offHoursRanges(availability, 480, 1260)).toEqual([]);
  });

  it('a closed day (of the business or of the professional) is outside hours all day', () => {
    for (const closure of [exception(WEDNESDAY, null), exception(WEDNESDAY, LUCAS)]) {
      const availability = dayAvailability(blocks, [closure], LUCAS, WEDNESDAY);
      expect(availability.closed).toBe(true);
      expect(availability.closedReason).toBe('Feriado');
      expect(isOutsideHours(availability, 10 * 60, 11 * 60)).toBe(true);
      expect(offHoursRanges(availability, 480, 1260)).toEqual([{ start: 480, end: 1260 }]);
    }
    // Another professional's day off or another date does not apply.
    expect(dayAvailability(blocks, [exception(WEDNESDAY, MATEO)], LUCAS, WEDNESDAY).closed).toBe(false);
    expect(dayAvailability(blocks, [exception('2026-11-19', null)], LUCAS, WEDNESDAY).closed).toBe(false);
  });

  it('an appointment is inside hours only if it fits entirely in one block', () => {
    const availability = dayAvailability(blocks, [], LUCAS, WEDNESDAY);

    expect(isOutsideHours(availability, 9 * 60, 9 * 60 + 30)).toBe(false);
    expect(isOutsideHours(availability, 12 * 60 + 30, 13 * 60)).toBe(false);
    // Crosses the end of the morning block.
    expect(isOutsideHours(availability, 12 * 60 + 45, 13 * 60 + 15)).toBe(true);
    // In the lunch break.
    expect(isOutsideHours(availability, 14 * 60, 14 * 60 + 30)).toBe(true);
    // A day of the week without blocks while others have them.
    expect(isOutsideHours(dayAvailability(blocks, [], LUCAS, '2026-11-20'), 10 * 60, 11 * 60)).toBe(true);
  });

  it('shades the gaps between blocks and around them', () => {
    const availability = dayAvailability(blocks, [], LUCAS, WEDNESDAY);

    expect(offHoursRanges(availability, 8 * 60, 21 * 60)).toEqual([
      { start: 480, end: 540 },
      { start: 780, end: 900 },
      { start: 1140, end: 1260 },
    ]);
  });
});
