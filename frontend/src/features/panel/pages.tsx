import { t } from '@/shared/i18n';

export function PlaceholderPage({ title }: { title: string }) {
  return (
    <section className="flex flex-col gap-2">
      <h1 className="text-2xl font-semibold">{title}</h1>
      <p className="text-muted-foreground">{t.panel.placeholder}</p>
    </section>
  );
}
