import { describe, expect, it } from 'vitest';
import { t } from '@/shared/i18n';
import { whatsappLink, whatsappNumber, whenLabel } from './whatsapp';

describe('whatsappNumber', () => {
  it('keeps numbers written with a country code', () => {
    expect(whatsappNumber('+54 9 11 5555-1234')).toBe('5491155551234');
    expect(whatsappNumber('+598 99 123 456')).toBe('59899123456');
  });

  it('assumes an Argentine mobile for local numbers', () => {
    expect(whatsappNumber('011 5555-1234')).toBe('5491155551234');
    expect(whatsappNumber('1155551234')).toBe('5491155551234');
    expect(whatsappNumber('5491155551234')).toBe('5491155551234');
  });

  it('returns null when there is nothing to dial', () => {
    expect(whatsappNumber(null)).toBeNull();
    expect(whatsappNumber('')).toBeNull();
    expect(whatsappNumber('abc')).toBeNull();
    expect(whatsappNumber('123')).toBeNull();
  });
});

describe('whatsappLink', () => {
  const message = {
    customer: 'Ana Pérez',
    business: 'Barbería Demo',
    service: 'Corte',
    professional: 'Lucas',
    // 10:00 in Buenos Aires (UTC-3), a Monday.
    startsAt: '2030-01-07T13:00:00Z',
    timeZone: 'America/Argentina/Buenos_Aires',
    link: 'https://turnero.test/r/demo/turno/abc',
  };

  it('writes the message in the business time zone with the customer first name and the link', () => {
    const href = whatsappLink('+5491155551234', message)!;
    const url = new URL(href);

    expect(url.origin + url.pathname).toBe('https://wa.me/5491155551234');
    const text = url.searchParams.get('text')!;
    expect(text).toContain('Hola Ana!');
    expect(text).toContain('Barbería Demo');
    expect(text).toContain(whenLabel(message.startsAt, message.timeZone));
    expect(text).toContain('lunes 07/01/2030 a las 10:00');
    expect(text).toContain(message.link);
    expect(text).toBe(
      t.reminders.message({
        customer: 'Ana',
        business: message.business,
        service: 'Corte',
        professional: 'Lucas',
        when: 'lunes 07/01/2030 a las 10:00',
        link: message.link,
      }),
    );
  });

  it('is null without a usable phone', () => {
    expect(whatsappLink(null, message)).toBeNull();
  });
});
