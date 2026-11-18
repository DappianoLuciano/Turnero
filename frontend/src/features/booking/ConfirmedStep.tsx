import { CheckCircle2 } from 'lucide-react';
import { formatMoney } from '@/shared/format/money';
import { t } from '@/shared/i18n';
import { Button } from '@/shared/ui/button';
import { WhenText } from './WhenText';
import type { PublicBooking } from './booking-api';

interface Props {
  booking: PublicBooking;
  timeZone: string;
  onAnother: () => void;
}

export function ConfirmedStep({ booking, timeZone, onAnother }: Props) {
  return (
    <section aria-label={t.booking.confirmed.title} className="flex flex-col items-center gap-4 text-center">
      <CheckCircle2 className="size-14 text-emerald-600" aria-hidden />
      <h2 className="text-xl font-semibold">{t.booking.confirmed.title}</h2>

      <dl className="bg-card grid w-full grid-cols-[auto_1fr] gap-x-4 gap-y-2 rounded-xl border p-4 text-left text-sm shadow-sm">
        <dt className="text-muted-foreground">{t.booking.confirmed.when}</dt>
        <dd>
          <WhenText startsAt={booking.startsAt} timeZone={timeZone} />
        </dd>
        <dt className="text-muted-foreground">{t.booking.confirmed.what}</dt>
        <dd>{booking.serviceName}</dd>
        <dt className="text-muted-foreground">{t.booking.confirmed.who}</dt>
        <dd>{booking.professionalName}</dd>
        <dt className="text-muted-foreground">{t.booking.confirmed.price}</dt>
        <dd>{formatMoney(booking.price, booking.currency)}</dd>
      </dl>

      <p className="text-muted-foreground text-sm">{t.booking.confirmed.payAtPlace}</p>
      <Button variant="outline" onClick={onAnother}>
        {t.booking.confirmed.another}
      </Button>
    </section>
  );
}
