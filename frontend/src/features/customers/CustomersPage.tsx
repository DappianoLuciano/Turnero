import { Archive, ArchiveRestore, Pencil, Plus, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useSession } from '@/features/auth/use-session';
import { errorMessage } from '@/shared/api/problem';
import { t } from '@/shared/i18n';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Label } from '@/shared/ui/label';
import { CustomerForm, type CustomerFormValues } from './CustomerForm';
import {
  PAGE_SIZE,
  useCreateCustomer,
  useCustomers,
  useDeleteCustomer,
  useUpdateCustomer,
  type Customer,
} from './customers-api';

type Editing = { mode: 'create' } | { mode: 'edit'; customer: Customer } | null;

const SEARCH_DEBOUNCE_MS = 300;

const toInput = (values: CustomerFormValues) => ({
  name: values.name,
  phone: values.phone || null,
  email: values.email || null,
  notes: values.notes || null,
});

export function CustomersPage() {
  const [searchText, setSearchText] = useState('');
  const [search, setSearch] = useState('');
  const [includeArchived, setIncludeArchived] = useState(false);
  const [page, setPage] = useState(1);
  const [editing, setEditing] = useState<Editing>(null);

  const customers = useCustomers({ search, includeArchived, page });
  const create = useCreateCustomer();
  const update = useUpdateCustomer();
  const remove = useDeleteCustomer();
  const session = useSession();
  const canDelete = session.status === 'authenticated' && session.user.role === 'Owner';
  const [confirmingId, setConfirmingId] = useState<string | null>(null);

  useEffect(() => {
    const timer = setTimeout(() => {
      setSearch(searchText.trim());
      setPage(1);
    }, SEARCH_DEBOUNCE_MS);
    return () => clearTimeout(timer);
  }, [searchText]);

  const close = () => {
    create.reset();
    update.reset();
    setEditing(null);
  };

  const submit = (values: CustomerFormValues) => {
    if (editing?.mode === 'edit') {
      update.mutate(
        { id: editing.customer.id, ...toInput(values), isArchived: editing.customer.isArchived },
        { onSuccess: close },
      );
    } else {
      create.mutate(toInput(values), { onSuccess: close });
    }
  };

  const toggleArchived = (customer: Customer) =>
    update.mutate({
      id: customer.id,
      name: customer.name,
      phone: customer.phone,
      email: customer.email,
      notes: customer.notes,
      isArchived: !customer.isArchived,
    });

  const data = customers.data;
  const pages = data ? Math.max(1, Math.ceil(data.total / PAGE_SIZE)) : 1;

  return (
    <section className="flex max-w-3xl flex-col gap-4">
      <div className="flex items-center justify-between gap-2">
        <h1 className="text-2xl font-semibold">{t.customers.title}</h1>
        {!editing && (
          <Button onClick={() => setEditing({ mode: 'create' })}>
            <Plus />
            {t.customers.new}
          </Button>
        )}
      </div>

      {editing && (
        <CustomerForm
          key={editing.mode === 'edit' ? editing.customer.id : 'new'}
          customer={editing.mode === 'edit' ? editing.customer : undefined}
          pending={create.isPending || update.isPending}
          error={create.error ?? (editing.mode === 'edit' ? update.error : null)}
          onSubmit={submit}
          onCancel={close}
        />
      )}

      <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex-1">
          <Label htmlFor="customer-search" className="sr-only">
            {t.customers.searchLabel}
          </Label>
          <Input
            id="customer-search"
            type="search"
            value={searchText}
            placeholder={t.customers.searchPlaceholder}
            onChange={(event) => setSearchText(event.target.value)}
          />
        </div>
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            className="size-4"
            checked={includeArchived}
            onChange={(event) => {
              setIncludeArchived(event.target.checked);
              setPage(1);
            }}
          />
          {t.customers.showArchived}
        </label>
      </div>

      {update.isError && !editing && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(update.error)}
        </p>
      )}
      {remove.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(remove.error)}
        </p>
      )}
      {customers.isPending && <p className="text-muted-foreground">{t.app.loading}</p>}
      {customers.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(customers.error)}
        </p>
      )}
      {data?.items.length === 0 && (
        <p className="text-muted-foreground">{search ? t.customers.emptySearch : t.customers.empty}</p>
      )}

      <ul className="flex flex-col gap-2">
        {data?.items.map((customer) => (
          <li
            key={customer.id}
            className="bg-card flex items-center justify-between gap-4 rounded-xl border p-4 shadow-sm"
          >
            <div className="min-w-0">
              <p className="flex items-center gap-2 font-medium">
                <span className="truncate">{customer.name}</span>
                {customer.isArchived && (
                  <span className="bg-muted text-muted-foreground rounded-full px-2 py-0.5 text-xs">
                    {t.customers.archived}
                  </span>
                )}
              </p>
              {(customer.phone || customer.email) && (
                <p className="text-muted-foreground truncate text-sm">
                  {[customer.phone, customer.email].filter(Boolean).join(' · ')}
                </p>
              )}
              {customer.notes && <p className="text-muted-foreground truncate text-sm">{customer.notes}</p>}
            </div>
            {canDelete && confirmingId === customer.id ? (
              <div
                role="group"
                aria-label={t.customers.confirmRemove(customer.name)}
                className="flex flex-col items-end gap-2"
              >
                <p className="text-sm">{t.customers.confirmRemove(customer.name)}</p>
                <div className="flex gap-2">
                  <Button variant="outline" size="sm" onClick={() => setConfirmingId(null)}>
                    {t.customers.confirmNo}
                  </Button>
                  <Button
                    variant="destructive"
                    size="sm"
                    disabled={remove.isPending}
                    onClick={() =>
                      remove.mutate(customer.id, {
                        onSuccess: () => {
                          setConfirmingId(null);
                          if (data.items.length === 1 && page > 1) setPage(page - 1);
                        },
                      })
                    }
                  >
                    {remove.isPending ? t.customers.removing : t.customers.confirmYes}
                  </Button>
                </div>
              </div>
            ) : (
              <div className="flex shrink-0">
                <Button
                  variant="ghost"
                  size="icon"
                  aria-label={t.customers.edit(customer.name)}
                  onClick={() => {
                    update.reset();
                    setEditing({ mode: 'edit', customer });
                  }}
                >
                  <Pencil />
                </Button>
                <Button
                  variant="ghost"
                  size="icon"
                  disabled={update.isPending}
                  aria-label={
                    customer.isArchived
                      ? t.customers.restore(customer.name)
                      : t.customers.archive(customer.name)
                  }
                  onClick={() => toggleArchived(customer)}
                >
                  {customer.isArchived ? <ArchiveRestore /> : <Archive />}
                </Button>
                {canDelete && (
                  <Button
                    variant="ghost"
                    size="icon"
                    aria-label={t.customers.remove(customer.name)}
                    onClick={() => {
                      remove.reset();
                      setConfirmingId(customer.id);
                    }}
                  >
                    <Trash2 />
                  </Button>
                )}
              </div>
            )}
          </li>
        ))}
      </ul>

      {data && data.total > 0 && (
        <div className="flex items-center justify-between gap-2 text-sm">
          <span className="text-muted-foreground">{t.customers.total(data.total)}</span>
          {pages > 1 && (
            <div className="flex items-center gap-2">
              <Button variant="outline" size="sm" disabled={page <= 1} onClick={() => setPage(page - 1)}>
                {t.customers.previous}
              </Button>
              <span>{t.customers.pageOf(page, pages)}</span>
              <Button variant="outline" size="sm" disabled={page >= pages} onClick={() => setPage(page + 1)}>
                {t.customers.next}
              </Button>
            </div>
          )}
        </div>
      )}
    </section>
  );
}
