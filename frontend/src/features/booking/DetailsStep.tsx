import { useState, type FormEvent } from 'react';
import { ApiError, errorMessage } from '@/shared/api/problem';
import { formatMoney } from '@/shared/format/money';
import { t } from '@/shared/i18n';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Label } from '@/shared/ui/label';
import { WhenText } from './WhenText';
import { useCreateBooking, type BookableSlot, type PublicBooking, type PublicService } from './booking-api';
import { forgetCustomer, saveCustomer, type SavedCustomer } from './saved-customer';

/** Same rule as the server: optional `+`, 6 to 15 digits, ignoring spaces, dashes, dots and parentheses. */
const isValidPhone = (phone: string) => /^\+?\d{6,15}$/.test(phone.replace(/[\s\-.()]/g, ''));

interface Props {
  slug: string;
  timeZone: string;
  service: PublicService;
  slot: BookableSlot;
  professionalName: string;
  saved: SavedCustomer | null;
  onForget: () => void;
  onUnavailable: () => void;
  onBooked: (booking: PublicBooking, customer: SavedCustomer) => void;
}

/** Last step: who is booking. A returning customer finds their data already filled in (kept on this device). */
export function DetailsStep({
  slug,
  timeZone,
  service,
  slot,
  professionalName,
  saved,
  onForget,
  onUnavailable,
  onBooked,
}: Props) {
  const create = useCreateBooking(slug);
  const [name, setName] = useState(saved?.name ?? '');
  const [phone, setPhone] = useState(saved?.phone ?? '');
  const [errors, setErrors] = useState<{ name?: string; phone?: string }>({});

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const next = {
      ...(name.trim() ? {} : { name: t.booking.details.nameRequired }),
      ...(isValidPhone(phone) ? {} : { phone: t.booking.details.phoneInvalid }),
    };
    setErrors(next);
    if (Object.keys(next).length > 0) return;

    const customer = { name: name.trim(), phone: phone.trim() };
    create.mutate(
      {
        professionalId: slot.professionalId,
        serviceId: service.id,
        startsAt: slot.startsAt,
        customerName: customer.name,
        customerPhone: customer.phone,
      },
      {
        onSuccess: (booking) => {
          saveCustomer(customer);
          onBooked(booking, customer);
        },
        onError: (error) => {
          if (error instanceof ApiError && error.code === 'slot_unavailable') onUnavailable();
        },
      },
    );
  };

  const stillHere =
    create.isError && !(create.error instanceof ApiError && create.error.code === 'slot_unavailable');

  return (
    <section aria-label={t.booking.steps.details} className="flex flex-col gap-4">
      <h2 className="text-lg font-semibold">{t.booking.steps.details}</h2>

      <div className="bg-card flex flex-col gap-1 rounded-xl border p-4 text-sm shadow-sm">
        <span className="text-muted-foreground">{t.booking.details.summary}</span>
        <span className="font-medium">{service.name}</span>
        <span>{professionalName}</span>
        <WhenText startsAt={slot.startsAt} timeZone={timeZone} />
        <span className="font-medium">{formatMoney(service.price, service.currency)}</span>
      </div>

      <form noValidate className="flex flex-col gap-4" onSubmit={submit}>
        <div className="flex flex-col gap-2">
          <Label htmlFor="booking-name">{t.booking.details.name}</Label>
          <Input
            id="booking-name"
            autoComplete="name"
            maxLength={120}
            value={name}
            aria-invalid={!!errors.name}
            onChange={(event) => setName(event.target.value)}
          />
          {errors.name && <p className="text-destructive text-sm">{errors.name}</p>}
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="booking-phone">{t.booking.details.phone}</Label>
          <Input
            id="booking-phone"
            type="tel"
            inputMode="tel"
            autoComplete="tel"
            maxLength={30}
            value={phone}
            aria-invalid={!!errors.phone}
            onChange={(event) => setPhone(event.target.value)}
          />
          <p className="text-muted-foreground text-xs">{t.booking.details.phoneHint}</p>
          {errors.phone && <p className="text-destructive text-sm">{errors.phone}</p>}
        </div>

        {stillHere && (
          <p role="alert" className="text-destructive text-sm">
            {errorMessage(create.error)}
          </p>
        )}

        <Button type="submit" size="lg" disabled={create.isPending}>
          {create.isPending ? t.booking.details.confirming : t.booking.details.confirm}
        </Button>
      </form>

      {saved && (
        <div className="text-muted-foreground flex flex-col items-start gap-1 text-xs">
          <p>{t.booking.details.remembered}</p>
          <button
            type="button"
            className="underline"
            onClick={() => {
              forgetCustomer();
              setName('');
              setPhone('');
              onForget();
            }}
          >
            {t.booking.details.forget}
          </button>
        </div>
      )}
    </section>
  );
}
