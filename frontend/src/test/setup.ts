import '@testing-library/jest-dom/vitest';
import { cleanup, configure } from '@testing-library/react';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { sessionStore } from '@/features/auth/session-store';
import { server } from './server';

// Many test files run in parallel: give async queries more room than the 1s default.
configure({ asyncUtilTimeout: 4000 });

beforeAll(() => server.listen({ onUnhandledFrame: 'error' }));
afterEach(() => {
  cleanup();
  server.resetHandlers();
  sessionStore.reset();
});
afterAll(() => server.close());
