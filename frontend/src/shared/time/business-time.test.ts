import { describe, expect, it } from 'vitest';
import {
  addDays,
  formatDate,
  formatMinutes,
  isValidDate,
  parseDate,
  parseTime,
  todayIn,
  toLocal,
  toUtcIso,
  zoneOffsetMinutes,
} from './business-time';

const BA = 'America/Argentina/Buenos_Aires';

describe('business time', () => {
  it('reads the zone offset (Buenos Aires is UTC-3)', () => {
    expect(zoneOffsetMinutes(Date.UTC(2026, 10, 18, 12), BA)).toBe(-180);
    expect(zoneOffsetMinutes(Date.UTC(2026, 10, 18, 12), 'UTC')).toBe(0);
  });

  it('converts a UTC instant to the local date and minutes of the business', () => {
    expect(toLocal('2026-11-18T13:00:00Z', BA)).toEqual({ date: '2026-11-18', minutes: 600 });
    // 02:30Z of the 19th is still the evening of the 18th in Buenos Aires.
    expect(toLocal('2026-11-19T02:30:00Z', BA)).toEqual({ date: '2026-11-18', minutes: 23 * 60 + 30 });
  });

  it('converts local date and time to a UTC instant', () => {
    expect(toUtcIso('2026-11-18', 600, BA)).toBe('2026-11-18T13:00:00.000Z');
    expect(toUtcIso('2026-11-18', 23 * 60 + 30, BA)).toBe('2026-11-19T02:30:00.000Z');
  });

  it('respects daylight saving time of the business zone', () => {
    // New York: UTC-5 in winter, UTC-4 in summer.
    expect(toUtcIso('2026-01-15', 600, 'America/New_York')).toBe('2026-01-15T15:00:00.000Z');
    expect(toUtcIso('2026-07-15', 600, 'America/New_York')).toBe('2026-07-15T14:00:00.000Z');
  });

  it('round-trips between local and UTC', () => {
    const iso = toUtcIso('2026-03-01', 9 * 60 + 15, BA);
    expect(toLocal(iso, BA)).toEqual({ date: '2026-03-01', minutes: 9 * 60 + 15 });
  });

  it('knows today in the business zone, not in the browser zone', () => {
    // 01:00Z of the 19th is 22:00 of the 18th in Buenos Aires.
    expect(todayIn(BA, new Date('2026-11-19T01:00:00Z'))).toBe('2026-11-18');
    expect(todayIn('UTC', new Date('2026-11-19T01:00:00Z'))).toBe('2026-11-19');
  });

  it('adds days across months and years', () => {
    expect(addDays('2026-11-30', 1)).toBe('2026-12-01');
    expect(addDays('2026-01-01', -1)).toBe('2025-12-31');
    expect(addDays('2026-02-28', 1)).toBe('2026-03-01');
  });

  it('formats and parses times of day', () => {
    expect(formatMinutes(0)).toBe('00:00');
    expect(formatMinutes(9 * 60 + 5)).toBe('09:05');
    expect(parseTime('09:05')).toBe(545);
  });

  it('validates date strings', () => {
    expect(isValidDate('2026-11-18')).toBe(true);
    expect(isValidDate('18/11/2026')).toBe(false);
    expect(isValidDate('2026-13-45')).toBe(false);
    expect(isValidDate(null)).toBe(false);
  });

  it('writes dates as day/month/year', () => {
    expect(formatDate('2026-11-05')).toBe('05/11/2026');
    expect(formatDate('')).toBe('');
  });

  it('reads day/month/year and never swaps day and month', () => {
    expect(parseDate('05/11/2026')).toBe('2026-11-05');
    expect(parseDate('5/11/2026')).toBe('2026-11-05');
    expect(parseDate('5-1-2026')).toBe('2026-01-05');
    expect(parseDate('18.11.2026')).toBe('2026-11-18');
    // 11/18 would be valid as month/day: here the first number is the day, so it is not a date.
    expect(parseDate('11/18/2026')).toBeNull();
    expect(parseDate('31/02/2026')).toBeNull();
    expect(parseDate('29/02/2028')).toBe('2028-02-29');
    expect(parseDate('2026-11-18')).toBeNull();
    expect(parseDate('')).toBeNull();
  });
});
