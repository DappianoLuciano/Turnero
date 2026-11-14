import { useEffect } from 'react';
import { Navigate, Outlet, useLocation } from 'react-router';
import { t } from '@/shared/i18n';
import { restoreSession } from './auth-api';
import { useSession } from './use-session';

/** Guards /app/*: restores the session from the refresh cookie once, then redirects if anonymous. */
export function RequireAuth() {
  const session = useSession();
  const location = useLocation();

  useEffect(() => {
    if (session.status === 'unknown') {
      void restoreSession();
    }
  }, [session.status]);

  if (session.status === 'unknown') {
    return (
      <div role="status" className="text-muted-foreground flex h-full items-center justify-center text-sm">
        {t.app.loading}
      </div>
    );
  }

  if (session.status === 'anonymous') {
    return <Navigate to="/app/login" replace state={{ from: location.pathname }} />;
  }

  return <Outlet />;
}
