import { Pencil, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useSession } from '@/features/auth/use-session';
import { errorMessage } from '@/shared/api/problem';
import { locale, t } from '@/shared/i18n';
import { Button } from '@/shared/ui/button';
import { ServiceForm, type ServiceFormValues } from './ServiceForm';
import {
  useCreateService,
  useDeleteService,
  useServices,
  useUpdateService,
  type Service,
} from './services-api';

const formatPrice = (service: Service) =>
  new Intl.NumberFormat(locale, { style: 'currency', currency: service.currency }).format(service.price);

type Editing = { mode: 'create' } | { mode: 'edit'; service: Service } | null;

export function ServicesPage() {
  const session = useSession();
  const canEdit = session.status === 'authenticated' && session.user.role === 'Owner';
  const services = useServices();
  const create = useCreateService();
  const update = useUpdateService();
  const remove = useDeleteService();
  const [confirmingId, setConfirmingId] = useState<string | null>(null);
  const [editing, setEditing] = useState<Editing>(null);

  const close = () => {
    create.reset();
    update.reset();
    setEditing(null);
  };

  const submit = (values: ServiceFormValues) => {
    const input = {
      name: values.name,
      description: values.description || null,
      durationMinutes: values.durationMinutes,
      price: values.price,
    };
    if (editing?.mode === 'edit') {
      update.mutate({ id: editing.service.id, ...input, isActive: values.isActive }, { onSuccess: close });
    } else {
      create.mutate(input, { onSuccess: close });
    }
  };

  return (
    <section className="flex max-w-3xl flex-col gap-4">
      <div className="flex items-center justify-between gap-2">
        <h1 className="text-2xl font-semibold">{t.services.title}</h1>
        {canEdit && !editing && (
          <Button onClick={() => setEditing({ mode: 'create' })}>
            <Plus />
            {t.services.new}
          </Button>
        )}
      </div>

      {editing && (
        <ServiceForm
          key={editing.mode === 'edit' ? editing.service.id : 'new'}
          service={editing.mode === 'edit' ? editing.service : undefined}
          pending={create.isPending || update.isPending}
          error={create.error ?? update.error}
          onSubmit={submit}
          onCancel={close}
        />
      )}

      {remove.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(remove.error)}
        </p>
      )}
      {services.isPending && <p className="text-muted-foreground">{t.app.loading}</p>}
      {services.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(services.error)}
        </p>
      )}
      {services.data?.length === 0 && (
        <p className="text-muted-foreground">{canEdit ? t.services.emptyOwner : t.services.empty}</p>
      )}

      <ul className="flex flex-col gap-2">
        {services.data?.map((service) => (
          <li
            key={service.id}
            className="bg-card flex items-center justify-between gap-4 rounded-xl border p-4 shadow-sm"
          >
            <div className="min-w-0">
              <p className="flex items-center gap-2 font-medium">
                <span className="truncate">{service.name}</span>
                {!service.isActive && (
                  <span className="bg-muted text-muted-foreground rounded-full px-2 py-0.5 text-xs">
                    {t.services.inactive}
                  </span>
                )}
              </p>
              {service.description && (
                <p className="text-muted-foreground truncate text-sm">{service.description}</p>
              )}
              <p className="text-muted-foreground text-sm">
                {t.services.duration(service.durationMinutes)} · {formatPrice(service)}
              </p>
            </div>
            {canEdit && confirmingId === service.id ? (
              <div
                role="group"
                aria-label={t.services.confirmRemove(service.name)}
                className="flex flex-col items-end gap-2"
              >
                <p className="text-sm">{t.services.confirmRemove(service.name)}</p>
                <div className="flex gap-2">
                  <Button variant="outline" size="sm" onClick={() => setConfirmingId(null)}>
                    {t.services.confirmNo}
                  </Button>
                  <Button
                    variant="destructive"
                    size="sm"
                    disabled={remove.isPending}
                    onClick={() =>
                      remove.mutate(service.id, {
                        onSuccess: () => setConfirmingId(null),
                      })
                    }
                  >
                    {remove.isPending ? t.services.removing : t.services.confirmYes}
                  </Button>
                </div>
              </div>
            ) : (
              canEdit && (
                <div className="flex shrink-0">
                  <Button
                    variant="ghost"
                    size="icon"
                    aria-label={t.services.edit(service.name)}
                    onClick={() => setEditing({ mode: 'edit', service })}
                  >
                    <Pencil />
                  </Button>
                  <Button
                    variant="ghost"
                    size="icon"
                    aria-label={t.services.remove(service.name)}
                    onClick={() => {
                      remove.reset();
                      setConfirmingId(service.id);
                    }}
                  >
                    <Trash2 />
                  </Button>
                </div>
              )
            )}
          </li>
        ))}
      </ul>
    </section>
  );
}
