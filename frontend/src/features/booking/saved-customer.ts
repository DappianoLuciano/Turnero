/**
 * The returning customer's name and phone, kept in this device's localStorage so the next booking is one tap
 * (no account, nothing leaves the device until they book). Storage can be unavailable or throw (private mode,
 * blocked site data): every access is guarded and the page works without it.
 */
const KEY = 'turnero.booking.customer';

export interface SavedCustomer {
  name: string;
  phone: string;
}

export function loadSavedCustomer(): SavedCustomer | null {
  try {
    const raw = globalThis.localStorage?.getItem(KEY);
    if (!raw) return null;
    const value: unknown = JSON.parse(raw);
    if (
      typeof value === 'object' &&
      value !== null &&
      'name' in value &&
      'phone' in value &&
      typeof value.name === 'string' &&
      typeof value.phone === 'string' &&
      value.name.trim() !== ''
    ) {
      return { name: value.name, phone: value.phone };
    }
  } catch {
    // Unavailable or corrupted: behave as a first-time visitor.
  }
  return null;
}

export function saveCustomer(customer: SavedCustomer): void {
  try {
    globalThis.localStorage?.setItem(KEY, JSON.stringify(customer));
  } catch {
    // Storage full or blocked: the booking itself is not affected.
  }
}

export function forgetCustomer(): void {
  try {
    globalThis.localStorage?.removeItem(KEY);
  } catch {
    // Nothing to do.
  }
}
