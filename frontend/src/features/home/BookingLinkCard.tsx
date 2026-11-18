import { useState } from 'react';
import { t } from '@/shared/i18n';
import { Button } from '@/shared/ui/button';

/** The public booking link of the business, ready to share with customers. */
export function BookingLinkCard({ slug }: { slug: string }) {
  const [copied, setCopied] = useState(false);
  const path = `/r/${slug}`;
  const url = `${globalThis.location?.origin ?? ''}${path}`;
  const text = t.panel.home.bookingLink;

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(url);
      setCopied(true);
    } catch {
      // Clipboard blocked: the link is selectable on screen anyway.
      setCopied(false);
    }
  };

  return (
    <section aria-label={text.title} className="bg-card flex flex-col gap-2 rounded-xl border p-4 shadow-sm">
      <h2 className="text-lg font-semibold">{text.title}</h2>
      <p className="text-muted-foreground text-sm">{text.description}</p>
      <p className="bg-muted rounded-md px-3 py-2 text-sm break-all select-all">{url}</p>
      <div className="flex flex-wrap items-center gap-2">
        <Button variant="outline" onClick={copy}>
          {text.copy}
        </Button>
        <Button asChild variant="ghost">
          <a href={path} target="_blank" rel="noreferrer">
            {text.open}
          </a>
        </Button>
        {copied && (
          <span role="status" className="text-sm">
            {text.copied}
          </span>
        )}
      </div>
    </section>
  );
}
