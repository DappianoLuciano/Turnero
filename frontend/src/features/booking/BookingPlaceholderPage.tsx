import { useParams } from 'react-router';
import { t } from '@/shared/i18n';

/**
 * Public booking site for a business (/r/:slug). Placeholder: the tenant will be resolved by slug
 * (or subdomain) on the API side — see docs/decisions/0012.
 */
export function BookingPlaceholderPage() {
  const { slug = '' } = useParams();
  return (
    <main className="mx-auto flex min-h-full max-w-md flex-col justify-center gap-3 p-6 text-center">
      <h1 className="text-2xl font-semibold">{t.booking.title}</h1>
      <p className="text-muted-foreground">{t.booking.placeholder(slug)}</p>
    </main>
  );
}
