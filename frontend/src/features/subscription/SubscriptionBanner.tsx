import { t } from '@/shared/i18n';
import { useSubscription } from './subscription-api';

/** Days before the end of a paid period at which we start reminding the owner. */
const RENEWAL_REMINDER_DAYS = 5;

/** Thin notice on top of the panel: remaining trial days, or a paid period about to end. */
export function SubscriptionBanner() {
  const { data } = useSubscription();
  if (!data) return null;

  const days = Number(data.daysLeft);
  const message =
    data.state === 'Trial'
      ? t.subscription.banner.trial(days)
      : data.state === 'Active' && days <= RENEWAL_REMINDER_DAYS
        ? t.subscription.banner.renewal(days)
        : null;
  if (!message) return null;

  return (
    <p role="status" className="bg-secondary text-secondary-foreground mb-4 rounded-md px-4 py-2 text-sm">
      {message}
    </p>
  );
}
