import { locale } from '@/shared/i18n';

/** `1500` → `$ 1.500,00` for ARS in es-AR. */
export const formatMoney = (amount: number, currency: string): string =>
  new Intl.NumberFormat(locale, { style: 'currency', currency }).format(amount);

/** Parses what a person types as an amount (`1500`, `1500,50`, `1.500,50`) into a number, or `null` if invalid. */
export function parseAmount(text: string): number | null {
  const cleaned = text
    .trim()
    .replace(/\s/g, '')
    .replace(/\.(?=\d{3}(\D|$))/g, '')
    .replace(',', '.');
  if (!/^\d+(\.\d{1,2})?$/.test(cleaned)) return null;
  const value = Number(cleaned);
  return value > 0 ? value : null;
}
