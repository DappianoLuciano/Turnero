import { useDeferredValue, useState } from 'react';
import { useCustomers } from '@/features/customers/customers-api';
import { t } from '@/shared/i18n';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Label } from '@/shared/ui/label';

interface Props {
  selected: { id: string; name: string } | null;
  error?: string;
  onSelect: (customer: { id: string; name: string } | null) => void;
}

/** Searches active customers by name, phone or email and lets the user pick one. */
export function CustomerPicker({ selected, error, onSelect }: Props) {
  const [search, setSearch] = useState('');
  const deferred = useDeferredValue(search.trim());
  const customers = useCustomers({ search: deferred, includeArchived: false, page: 1 });

  if (selected) {
    return (
      <div className="flex flex-col gap-2">
        <span className="text-sm font-medium">{t.agenda.form.customer}</span>
        <div className="flex items-center justify-between gap-2 rounded-md border px-3 py-2 text-sm">
          <span className="truncate">{selected.name}</span>
          <Button type="button" variant="ghost" size="sm" onClick={() => onSelect(null)}>
            {t.agenda.form.customerChange}
          </Button>
        </div>
      </div>
    );
  }

  const items = customers.data?.items ?? [];
  return (
    <div className="flex flex-col gap-2">
      <Label htmlFor="appointment-customer-search">{t.agenda.form.customerSearch}</Label>
      <Input
        id="appointment-customer-search"
        type="search"
        value={search}
        placeholder={t.agenda.form.customerSearchPlaceholder}
        aria-invalid={!!error}
        onChange={(event) => setSearch(event.target.value)}
      />
      {customers.isSuccess && items.length === 0 && (
        <p className="text-muted-foreground text-sm">{t.agenda.form.customerNone}</p>
      )}
      {items.length > 0 && (
        <ul className="max-h-48 overflow-y-auto rounded-md border">
          {items.map((customer) => (
            <li key={customer.id}>
              <button
                type="button"
                className="hover:bg-muted flex w-full flex-col items-start px-3 py-2 text-left text-sm"
                onClick={() => onSelect({ id: customer.id, name: customer.name })}
              >
                <span className="font-medium">{customer.name}</span>
                {customer.phone && <span className="text-muted-foreground">{customer.phone}</span>}
              </button>
            </li>
          ))}
        </ul>
      )}
      {error && <p className="text-destructive text-sm">{error}</p>}
    </div>
  );
}
