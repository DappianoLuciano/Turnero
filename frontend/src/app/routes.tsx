import { Navigate, type RouteObject } from 'react-router';
import { BookingPage } from '@/features/booking/BookingPage';
import { LoginPage } from '@/features/auth/LoginPage';
import { RequireAuth } from '@/features/auth/RequireAuth';
import { CustomersPage } from '@/features/customers/CustomersPage';
import { navItems } from '@/features/panel/navigation';
import { AgendaPage } from '@/features/agenda/AgendaPage';
import { CashPage } from '@/features/cash/CashPage';
import { SchedulesPage } from '@/features/schedules/SchedulesPage';
import { ReportsPage } from '@/features/reports/ReportsPage';
import { PaymentsPage } from '@/features/payments/PaymentsPage';
import { StaffPage } from '@/features/staff/StaffPage';
import { ServicesPage } from '@/features/services/ServicesPage';
import { HomePage } from '@/features/home/HomePage';
import { PlaceholderPage } from '@/features/panel/pages';
import { PanelLayout } from '@/features/panel/PanelLayout';
import { NotFoundPage } from './NotFoundPage';

/**
 * Two route zones:
 * - /app/*     business panel (requires login)
 * - /r/:slug/* public booking site of a business
 */
export const routes: RouteObject[] = [
  { path: '/', element: <Navigate to="/app" replace /> },
  { path: '/app/login', element: <LoginPage /> },
  {
    path: '/app',
    element: <RequireAuth />,
    children: [
      {
        element: <PanelLayout />,
        children: [
          { index: true, element: <HomePage /> },
          { path: 'services', element: <ServicesPage /> },
          { path: 'customers', element: <CustomersPage /> },
          { path: 'staff', element: <StaffPage /> },
          { path: 'agenda', element: <AgendaPage /> },
          { path: 'payments', element: <PaymentsPage /> },
          { path: 'cash-register', element: <CashPage /> },
          { path: 'reports', element: <ReportsPage /> },
          { path: 'schedules', element: <SchedulesPage /> },
          ...navItems
            .filter(
              (item) =>
                ![
                  '/app',
                  '/app/services',
                  '/app/customers',
                  '/app/staff',
                  '/app/agenda',
                  '/app/payments',
                  '/app/cash-register',
                  '/app/reports',
                  '/app/schedules',
                ].includes(item.to),
            )
            .map((item) => ({
              path: item.to.replace('/app/', ''),
              element: <PlaceholderPage title={item.label} />,
            })),
        ],
      },
    ],
  },
  { path: '/r/:slug/*', element: <BookingPage /> },
  { path: '*', element: <NotFoundPage /> },
];
