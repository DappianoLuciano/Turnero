import { useSession } from '@/features/auth/use-session';
import { t } from '@/shared/i18n';

export function HomePage() {
  const session = useSession();
  const name = session.status === 'authenticated' ? session.user.fullName : '';
  return (
    <section className="flex flex-col gap-2">
      <h1 className="text-2xl font-semibold">{t.panel.home.greeting(name)}</h1>
      <p className="text-muted-foreground">{t.panel.home.description}</p>
    </section>
  );
}

export function PlaceholderPage({ title }: { title: string }) {
  return (
    <section className="flex flex-col gap-2">
      <h1 className="text-2xl font-semibold">{title}</h1>
      <p className="text-muted-foreground">{t.panel.placeholder}</p>
    </section>
  );
}
