import { Lock } from 'lucide-react';
import { useBusiness } from '@/features/business/business-api';
import { product } from '@/shared/config/product';
import { t } from '@/shared/i18n';

/** Shown instead of the panel when the trial ended and the business has not paid. Data is kept, nothing is lost. */
export function SubscriptionExpiredPage() {
  const business = useBusiness();
  const slug = business.data?.slug;

  return (
    <section className="mx-auto flex max-w-md flex-col items-center gap-4 py-12 text-center">
      <Lock className="text-muted-foreground size-10" aria-hidden />
      <h1 className="text-2xl font-semibold">{t.subscription.expired.title}</h1>
      <p className="text-muted-foreground">{t.subscription.expired.description}</p>
      <p className="text-sm">{t.subscription.expired.dataSafe}</p>
      {slug && <p className="bg-muted rounded-md px-3 py-2 text-sm">{t.subscription.expired.yourId(slug)}</p>}
      {product.supportEmail ? (
        <a className="text-primary text-sm font-medium underline" href={`mailto:${product.supportEmail}`}>
          {t.subscription.expired.contact(product.supportEmail)}
        </a>
      ) : (
        <p className="text-muted-foreground text-sm">{t.subscription.expired.contactUs}</p>
      )}
    </section>
  );
}
