import {
  BarChart3,
  BellRing,
  CalendarDays,
  Clock,
  CreditCard,
  Home,
  Scissors,
  Settings,
  Users,
  UserSquare2,
  Wallet,
  type LucideIcon,
} from 'lucide-react';
import { t } from '@/shared/i18n';

export interface NavItem {
  to: string;
  label: string;
  icon: LucideIcon;
}

/** Side navigation of the panel. Each module adds its entry here. */
export const navItems: NavItem[] = [
  { to: '/app', label: t.panel.nav.home, icon: Home },
  { to: '/app/agenda', label: t.panel.nav.agenda, icon: CalendarDays },
  { to: '/app/services', label: t.panel.nav.services, icon: Scissors },
  { to: '/app/staff', label: t.panel.nav.staff, icon: UserSquare2 },
  { to: '/app/schedules', label: t.panel.nav.schedules, icon: Clock },
  { to: '/app/customers', label: t.panel.nav.customers, icon: Users },
  { to: '/app/payments', label: t.panel.nav.payments, icon: CreditCard },
  { to: '/app/cash-register', label: t.panel.nav.cashRegister, icon: Wallet },
  { to: '/app/reports', label: t.panel.nav.reports, icon: BarChart3 },
  { to: '/app/reminders', label: t.panel.nav.reminders, icon: BellRing },
  { to: '/app/settings', label: t.panel.nav.settings, icon: Settings },
];
