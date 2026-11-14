import type { components } from '@/shared/api/schema';

export type SessionUser = components['schemas']['SessionUser'];
export type SessionResponse = components['schemas']['SessionResponse'];

export type SessionState =
  | { status: 'unknown' }
  | { status: 'anonymous' }
  | { status: 'authenticated'; accessToken: string; expiresAt: string; user: SessionUser };

type Listener = () => void;

/**
 * Session kept in memory only: the access token is never written to localStorage.
 * The refresh token lives in an httpOnly cookie the JS cannot read (ADR 0006).
 */
let state: SessionState = { status: 'unknown' };
const listeners = new Set<Listener>();

export const sessionStore = {
  get: (): SessionState => state,
  subscribe(listener: Listener) {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },
  setSession(session: SessionResponse) {
    state = {
      status: 'authenticated',
      accessToken: session.accessToken,
      expiresAt: session.accessTokenExpiresAt,
      user: session.user,
    };
    listeners.forEach((l) => l());
  },
  clear() {
    state = { status: 'anonymous' };
    listeners.forEach((l) => l());
  },
  accessToken(): string | null {
    return state.status === 'authenticated' ? state.accessToken : null;
  },
  /** Test helper. */
  reset() {
    state = { status: 'unknown' };
    listeners.forEach((l) => l());
  },
};
