import { api, refreshSession, unwrap } from '@/shared/api/client';
import { sessionStore } from './session-store';

export interface LoginInput {
  email: string;
  password: string;
}

export async function login(input: LoginInput) {
  const session = await unwrap(api.POST('/api/auth/login', { body: input }));
  sessionStore.setSession(session);
  return session;
}

export async function logout() {
  try {
    await api.POST('/api/auth/logout');
  } finally {
    sessionStore.clear();
  }
}

/** On app start: try to restore the session from the refresh cookie. */
export function restoreSession(): Promise<boolean> {
  return refreshSession();
}
