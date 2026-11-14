import { useSyncExternalStore } from 'react';
import { sessionStore, type SessionState } from './session-store';

export function useSession(): SessionState {
  return useSyncExternalStore(sessionStore.subscribe, sessionStore.get, sessionStore.get);
}
