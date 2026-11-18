import { Navigate, type RouteObject } from 'react-router';
import { BookingPlaceholderPage } from '@/features/booking/BookingPlaceholderPage';
import { LoginPage } from '@/features/auth/LoginPage';
import { RequireAuth } from '@/features/auth/RequireAuth';
import { navItems } from '@/features/panel/navigation';
import { ServicesPage } from '@/features/services/ServicesPage';
import { HomePage, PlaceholderPage } from '@/features/panel/pages';
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
          ...navItems
            .filter((item) => item.to !== '/app' && item.to !== '/app/services')
            .map((item) => ({
              path: item.to.replace('/app/', ''),
              element: <PlaceholderPage title={item.label} />,
            })),
        ],
      },
    ],
  },
  { path: '/r/:slug/*', element: <BookingPlaceholderPage /> },
  { path: '*', element: <NotFoundPage /> },
];
