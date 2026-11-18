import { LogOut, Menu, X } from 'lucide-react';
import { useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router';
import { logout } from '@/features/auth/auth-api';
import { useSession } from '@/features/auth/use-session';
import { t } from '@/shared/i18n';
import { cn } from '@/shared/lib/utils';
import { Button } from '@/shared/ui/button';
import { navItems } from './navigation';

/** Panel shell. Mobile first: top bar + drawer; from md up, a fixed sidebar. */
export function PanelLayout() {
  const [open, setOpen] = useState(false);
  const navigate = useNavigate();
  const session = useSession();

  const handleLogout = async () => {
    await logout();
    navigate('/app/login', { replace: true });
  };

  return (
    <div className="flex h-full">
      {open && (
        <div
          className="fixed inset-0 z-30 bg-black/40 md:hidden"
          aria-hidden
          onClick={() => setOpen(false)}
        />
      )}

      <aside
        className={cn(
          'bg-background fixed inset-y-0 left-0 z-40 flex w-64 flex-col border-r transition-transform md:static md:translate-x-0',
          open ? 'translate-x-0' : '-translate-x-full',
        )}
      >
        <div className="flex h-14 items-center justify-between px-4">
          <span className="font-semibold">{t.app.name}</span>
          <Button
            variant="ghost"
            size="icon"
            className="md:hidden"
            aria-label={t.panel.closeMenu}
            onClick={() => setOpen(false)}
          >
            <X />
          </Button>
        </div>
        <nav className="flex flex-1 flex-col gap-1 overflow-y-auto px-2 py-2">
          {navItems.map(({ to, label, icon: Icon }) => (
            <NavLink
              key={to}
              to={to}
              end={to === '/app'}
              onClick={() => setOpen(false)}
              className={({ isActive }) =>
                cn(
                  'flex items-center gap-3 rounded-md px-3 py-2.5 text-sm font-medium',
                  isActive
                    ? 'bg-secondary text-secondary-foreground'
                    : 'text-muted-foreground hover:bg-muted',
                )
              }
            >
              <Icon className="size-4" />
              {label}
            </NavLink>
          ))}
        </nav>
        <div className="border-t p-2">
          {session.status === 'authenticated' && (
            <div className="px-3 py-2">
              <p className="truncate text-sm font-medium">{session.user.fullName}</p>
              <p className="text-muted-foreground truncate text-xs">{session.user.email}</p>
            </div>
          )}
          <Button variant="ghost" className="w-full justify-start gap-3" onClick={handleLogout}>
            <LogOut className="size-4" />
            {t.auth.logout}
          </Button>
        </div>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex h-14 items-center gap-2 border-b px-2 md:hidden">
          <Button variant="ghost" size="icon" aria-label={t.panel.openMenu} onClick={() => setOpen(true)}>
            <Menu />
          </Button>
          <span className="font-semibold">{t.app.name}</span>
        </header>
        <main className="flex-1 overflow-y-auto p-4 md:p-8">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
