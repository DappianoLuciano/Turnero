import { Link } from 'react-router';
import { t } from '@/shared/i18n';
import { Button } from '@/shared/ui/button';

export function NotFoundPage() {
  return (
    <main className="flex min-h-full flex-col items-center justify-center gap-4 p-6 text-center">
      <h1 className="text-2xl font-semibold">{t.notFound.title}</h1>
      <Button asChild variant="outline">
        <Link to="/app">{t.notFound.back}</Link>
      </Button>
    </main>
  );
}
