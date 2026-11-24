import { Check, MessageCircle } from 'lucide-react';
import { useState } from 'react';
import { useBusiness } from '@/features/business/business-api';
import { errorMessage } from '@/shared/api/problem';
import { t } from '@/shared/i18n';
import { addDays, formatMinutes, todayIn, toLocal } from '@/shared/time/business-time';
import { Button } from '@/shared/ui/button';
import { useMarkReminderSent, useReminders } from './reminders-api';
import { whatsappLink } from './whatsapp';

/**
 * Reminders for a day: one WhatsApp message per appointment, written and addressed by the panel. There is no
 * messaging provider: the business presses "send", WhatsApp opens with the text ready, and the appointment is marked.
 */
export function RemindersPage() {
  const business = useBusiness();
  if (business.isPending) {
    return <p className="text-muted-foreground">{t.app.loading}</p>;
  }
  if (business.isError) {
    return (
      <p role="alert" className="text-destructive text-sm">
        {errorMessage(business.error)}
      </p>
    );
  }
  return <Reminders name={business.data.name} slug={business.data.slug} timeZone={business.data.timeZone} />;
}

function Reminders({ name, slug, timeZone }: { name: string; slug: string; timeZone: string }) {
  const today = todayIn(timeZone);
  const [date, setDate] = useState(() => addDays(today, 1));
  const reminders = useReminders(date);
  const markSent = useMarkReminderSent(date);

  const list = reminders.data ?? [];
  const sentCount = list.filter((r) => r.reminderSentAt).length;

  return (
    <section className="flex max-w-3xl flex-col gap-4">
      <div className="flex flex-col gap-1">
        <h1 className="text-2xl font-semibold">{t.reminders.title}</h1>
        <p className="text-muted-foreground text-sm">{t.reminders.description}</p>
      </div>

      <div className="flex gap-2">
        {[
          { day: today, label: t.reminders.today },
          { day: addDays(today, 1), label: t.reminders.tomorrow },
        ].map(({ day, label }) => (
          <Button
            key={day}
            variant={day === date ? 'default' : 'outline'}
            aria-pressed={day === date}
            onClick={() => setDate(day)}
          >
            {label}
          </Button>
        ))}
      </div>

      {reminders.isPending && <p className="text-muted-foreground">{t.app.loading}</p>}
      {reminders.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(reminders.error)}
        </p>
      )}
      {reminders.data?.length === 0 && <p className="text-muted-foreground">{t.reminders.empty}</p>}
      {list.length > 0 && (
        <p className="text-muted-foreground text-sm">{t.reminders.summary(sentCount, list.length)}</p>
      )}

      <ul className="flex flex-col gap-2">
        {list.map((r) => {
          const href = whatsappLink(r.customerPhone, {
            customer: r.customerName,
            business: name,
            service: r.serviceName,
            professional: r.professionalName,
            startsAt: r.startsAt,
            timeZone,
            link: `${globalThis.location.origin}/r/${slug}/turno/${r.manageToken}`,
          });
          return (
            <li
              key={r.appointmentId}
              className="bg-card flex items-center justify-between gap-3 rounded-xl border p-4 shadow-sm"
            >
              <div className="min-w-0">
                <p className="truncate font-medium">
                  {formatMinutes(toLocal(r.startsAt, timeZone).minutes)} · {r.customerName}
                </p>
                <p className="text-muted-foreground truncate text-sm">
                  {r.serviceName} · {r.professionalName}
                </p>
              </div>
              <div className="flex shrink-0 flex-col items-end gap-1">
                {r.reminderSentAt && (
                  <span className="flex items-center gap-1 text-xs text-emerald-700">
                    <Check className="size-3" aria-hidden />
                    {t.reminders.sent}
                  </span>
                )}
                {href ? (
                  <Button asChild size="sm" variant={r.reminderSentAt ? 'outline' : 'default'}>
                    <a
                      href={href}
                      target="_blank"
                      rel="noreferrer"
                      aria-label={t.reminders.sendTo(r.customerName)}
                      onClick={() => markSent.mutate(r.appointmentId)}
                    >
                      <MessageCircle />
                      {r.reminderSentAt ? t.reminders.resend : t.reminders.send}
                    </a>
                  </Button>
                ) : (
                  <span className="text-muted-foreground text-sm">
                    {r.customerPhone ? t.reminders.invalidPhone : t.reminders.noPhone}
                  </span>
                )}
              </div>
            </li>
          );
        })}
      </ul>
    </section>
  );
}
