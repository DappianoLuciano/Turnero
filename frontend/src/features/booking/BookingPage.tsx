import { ArrowLeft } from 'lucide-react';
import { useState } from 'react';
import { useParams } from 'react-router';
import { ApiError, errorMessage } from '@/shared/api/problem';
import { formatMoney } from '@/shared/format/money';
import { t } from '@/shared/i18n';
import { Button } from '@/shared/ui/button';
import { ConfirmedStep } from './ConfirmedStep';
import { DetailsStep } from './DetailsStep';
import { loadSavedCustomer, type SavedCustomer } from './saved-customer';
import { TimeStep } from './TimeStep';
import {
  usePublicCatalog,
  type BookableSlot,
  type PublicBooking,
  type PublicProfessional,
  type PublicService,
} from './booking-api';

/** `null` as the chosen professional means "any available". */
type Choice = PublicProfessional | 'any';

const TOTAL_STEPS = 4;

/** Public booking site of a business (/r/:slug): no login, mobile first, one decision per screen. */
export function BookingPage() {
  const { slug = '' } = useParams();
  const catalog = usePublicCatalog(slug);

  const [service, setService] = useState<PublicService | null>(null);
  const [choice, setChoice] = useState<Choice | null>(null);
  const [slot, setSlot] = useState<BookableSlot | null>(null);
  const [confirmed, setConfirmed] = useState<PublicBooking | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  // Read once: the saved customer of this device (null the first time or when storage is unavailable).
  const [saved, setSaved] = useState<SavedCustomer | null>(() => loadSavedCustomer());

  if (catalog.isPending) {
    return (
      <Shell>
        <p className="text-muted-foreground text-center">{t.app.loading}</p>
      </Shell>
    );
  }

  if (catalog.isError) {
    const notFound = catalog.error instanceof ApiError && catalog.error.status === 404;
    return (
      <Shell>
        {notFound ? (
          <div className="flex flex-col gap-2 text-center">
            <h1 className="text-2xl font-semibold">{t.booking.notFound.title}</h1>
            <p className="text-muted-foreground">{t.booking.notFound.description}</p>
          </div>
        ) : (
          <p role="alert" className="text-destructive text-center text-sm">
            {errorMessage(catalog.error)}
          </p>
        )}
      </Shell>
    );
  }

  const { business, services, professionals } = catalog.data;
  const eligible = (s: PublicService) => professionals.filter((p) => p.serviceIds.includes(s.id));

  const reset = () => {
    setService(null);
    setChoice(null);
    setSlot(null);
    setConfirmed(null);
    setNotice(null);
  };

  const pickService = (picked: PublicService) => {
    setNotice(null);
    setService(picked);
    const options = eligible(picked);
    // With a single professional there is nothing to choose.
    setChoice(options.length === 1 ? (options[0] ?? null) : null);
  };

  const step = confirmed ? 0 : !service ? 1 : !choice ? 2 : !slot ? 3 : 4;

  const back = () => {
    setNotice(null);
    if (step === 4) setSlot(null);
    else if (step === 3) {
      // If the professional step was skipped, go back to the services.
      setChoice(null);
      if (service && eligible(service).length === 1) setService(null);
    } else if (step === 2) setService(null);
  };

  const professionalIds =
    service && choice ? (choice === 'any' ? eligible(service).map((p) => p.id) : [choice.id]) : [];
  const professionalName = (id: string) => professionals.find((p) => p.id === id)?.name ?? '';

  return (
    <Shell>
      <header className="flex flex-col gap-1 text-center">
        <h1 className="text-2xl font-semibold">{business.name}</h1>
        <p className="text-muted-foreground text-sm">{t.booking.title}</p>
        {saved && !confirmed && (
          <p className="text-sm">{t.booking.welcomeBack(saved.name.trim().split(/\s+/)[0] ?? saved.name)}</p>
        )}
      </header>

      {!confirmed && (
        <div className="flex items-center justify-between gap-2">
          {step > 1 ? (
            <Button variant="ghost" size="sm" onClick={back}>
              <ArrowLeft />
              {t.booking.back}
            </Button>
          ) : (
            <span />
          )}
          <span className="text-muted-foreground text-sm">{t.booking.step(step, TOTAL_STEPS)}</span>
        </div>
      )}

      {notice && (
        <p role="alert" className="text-destructive rounded-md border border-current p-3 text-sm">
          {notice}
        </p>
      )}

      {confirmed ? (
        <ConfirmedStep booking={confirmed} timeZone={business.timeZone} onAnother={reset} />
      ) : services.length === 0 ? (
        <p className="text-muted-foreground text-center">{t.booking.nothingToBook}</p>
      ) : step === 1 ? (
        <section aria-label={t.booking.steps.service} className="flex flex-col gap-3">
          <h2 className="text-lg font-semibold">{t.booking.steps.service}</h2>
          {services.map((s) => (
            <button
              key={s.id}
              type="button"
              className="bg-card hover:bg-muted flex items-center justify-between gap-3 rounded-xl border p-4 text-left shadow-sm"
              onClick={() => pickService(s)}
            >
              <span className="flex flex-col">
                <span className="font-medium">{s.name}</span>
                <span className="text-muted-foreground text-sm">{t.booking.minutes(s.durationMinutes)}</span>
              </span>
              <span className="font-medium">{formatMoney(s.price, s.currency)}</span>
            </button>
          ))}
        </section>
      ) : step === 2 && service ? (
        <section aria-label={t.booking.steps.professional} className="flex flex-col gap-3">
          <h2 className="text-lg font-semibold">{t.booking.steps.professional}</h2>
          <button
            type="button"
            className="bg-card hover:bg-muted flex flex-col rounded-xl border p-4 text-left shadow-sm"
            onClick={() => setChoice('any')}
          >
            <span className="font-medium">{t.booking.anyProfessional}</span>
            <span className="text-muted-foreground text-sm">{t.booking.anyProfessionalHint}</span>
          </button>
          {eligible(service).map((p) => (
            <button
              key={p.id}
              type="button"
              className="bg-card hover:bg-muted rounded-xl border p-4 text-left font-medium shadow-sm"
              onClick={() => setChoice(p)}
            >
              {p.name}
            </button>
          ))}
        </section>
      ) : step === 3 && service ? (
        <TimeStep
          slug={slug}
          timeZone={business.timeZone}
          service={service}
          professionalIds={professionalIds}
          onPick={(picked) => {
            setNotice(null);
            setSlot(picked);
          }}
        />
      ) : step === 4 && service && slot ? (
        <DetailsStep
          slug={slug}
          timeZone={business.timeZone}
          service={service}
          slot={slot}
          professionalName={professionalName(slot.professionalId)}
          saved={saved}
          onForget={() => setSaved(null)}
          onUnavailable={() => {
            setSlot(null);
            setNotice(t.booking.unavailable);
          }}
          onBooked={(booking, customer) => {
            setSaved(customer);
            setConfirmed(booking);
          }}
        />
      ) : null}
    </Shell>
  );
}

function Shell({ children }: { children: React.ReactNode }) {
  return <main className="mx-auto flex min-h-full w-full max-w-lg flex-col gap-5 p-4 pb-10">{children}</main>;
}
