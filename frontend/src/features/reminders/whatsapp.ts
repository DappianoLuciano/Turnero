import { locale, t } from '@/shared/i18n';
import { formatDate, formatMinutes, toLocal } from '@/shared/time/business-time';

/**
 * Number in the format of `wa.me/<number>`: digits only, with country code.
 * Numbers written with "+" are kept as they are; the rest are assumed to be Argentine mobiles (country code 54, the
 * extra 9 WhatsApp needs for mobiles, no leading zero). Returns null when there is nothing to dial.
 */
export function whatsappNumber(phone: string | null | undefined): string | null {
  if (!phone) return null;
  const digits = phone.replace(/\D/g, '');
  if (digits.length < 8) return null;
  if (phone.trim().startsWith('+')) return digits;
  const national = digits.replace(/^0+/, '');
  if (national.startsWith('549')) return national;
  if (national.startsWith('54') && national.length >= 12) return `549${national.slice(2)}`;
  return `549${national}`;
}

/** "lunes 23/11/2026 a las 10:00" in the business's time zone (never the browser's). */
export function whenLabel(startsAt: string, timeZone: string): string {
  const local = toLocal(startsAt, timeZone);
  const weekday = new Intl.DateTimeFormat(locale, { weekday: 'long', timeZone: 'UTC' }).format(
    new Date(`${local.date}T12:00:00Z`),
  );
  return t.reminders.weekdayTime(weekday, formatDate(local.date), formatMinutes(local.minutes));
}

export interface ReminderMessage {
  customer: string;
  business: string;
  service: string;
  professional: string;
  startsAt: string;
  timeZone: string;
  link: string;
}

/** `https://wa.me/<number>?text=<message>`, or null if the customer has no usable phone. */
export function whatsappLink(phone: string | null | undefined, message: ReminderMessage): string | null {
  const number = whatsappNumber(phone);
  if (!number) return null;
  const text = t.reminders.message({
    customer: message.customer.split(' ')[0] ?? message.customer,
    business: message.business,
    service: message.service,
    professional: message.professional,
    when: whenLabel(message.startsAt, message.timeZone),
    link: message.link,
  });
  return `https://wa.me/${number}?text=${encodeURIComponent(text)}`;
}
