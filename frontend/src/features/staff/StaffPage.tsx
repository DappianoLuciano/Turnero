import { Pencil, Plus, Trash2 } from 'lucide-react';
import { useState } from 'react';
import { useSession } from '@/features/auth/use-session';
import { useServices } from '@/features/services/services-api';
import { errorMessage } from '@/shared/api/problem';
import { t } from '@/shared/i18n';
import { Button } from '@/shared/ui/button';
import { ProfessionalForm, type ProfessionalFormValues } from './ProfessionalForm';
import {
  useCreateProfessional,
  useDeleteProfessional,
  useProfessionals,
  useUpdateProfessional,
  type Professional,
} from './staff-api';

type Editing = { mode: 'create' } | { mode: 'edit'; professional: Professional } | null;

export function StaffPage() {
  const session = useSession();
  const canEdit = session.status === 'authenticated' && session.user.role === 'Owner';
  const professionals = useProfessionals();
  const services = useServices();
  const create = useCreateProfessional();
  const update = useUpdateProfessional();
  const remove = useDeleteProfessional();
  const [confirmingId, setConfirmingId] = useState<string | null>(null);
  const [editing, setEditing] = useState<Editing>(null);

  const serviceNames = new Map((services.data ?? []).map((s) => [s.id, s.name]));

  const close = () => {
    create.reset();
    update.reset();
    setEditing(null);
  };

  const submit = (values: ProfessionalFormValues) => {
    const input = { name: values.name, serviceIds: values.serviceIds };
    if (editing?.mode === 'edit') {
      update.mutate(
        { id: editing.professional.id, ...input, isActive: values.isActive },
        { onSuccess: close },
      );
    } else {
      create.mutate(input, { onSuccess: close });
    }
  };

  return (
    <section className="flex max-w-3xl flex-col gap-4">
      <div className="flex items-center justify-between gap-2">
        <h1 className="text-2xl font-semibold">{t.staff.title}</h1>
        {canEdit && !editing && services.isSuccess && (
          <Button onClick={() => setEditing({ mode: 'create' })}>
            <Plus />
            {t.staff.new}
          </Button>
        )}
      </div>

      {editing && (
        <ProfessionalForm
          key={editing.mode === 'edit' ? editing.professional.id : 'new'}
          professional={editing.mode === 'edit' ? editing.professional : undefined}
          services={services.data ?? []}
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
      {professionals.isPending && <p className="text-muted-foreground">{t.app.loading}</p>}
      {professionals.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(professionals.error)}
        </p>
      )}
      {professionals.data?.length === 0 && (
        <p className="text-muted-foreground">{canEdit ? t.staff.emptyOwner : t.staff.empty}</p>
      )}

      <ul className="flex flex-col gap-2">
        {professionals.data?.map((professional) => {
          const names = professional.serviceIds.map((id) => serviceNames.get(id)).filter(Boolean);
          return (
            <li
              key={professional.id}
              className="bg-card flex items-center justify-between gap-4 rounded-xl border p-4 shadow-sm"
            >
              <div className="min-w-0">
                <p className="flex items-center gap-2 font-medium">
                  <span className="truncate">{professional.name}</span>
                  {!professional.isActive && (
                    <span className="bg-muted text-muted-foreground rounded-full px-2 py-0.5 text-xs">
                      {t.staff.inactive}
                    </span>
                  )}
                </p>
                <p className="text-muted-foreground text-sm">
                  {names.length > 0 ? names.join(', ') : t.staff.noServices}
                </p>
              </div>
              {canEdit && confirmingId === professional.id ? (
                <div
                  role="group"
                  aria-label={t.staff.confirmRemove(professional.name)}
                  className="flex flex-col items-end gap-2"
                >
                  <p className="text-sm">{t.staff.confirmRemove(professional.name)}</p>
                  <div className="flex gap-2">
                    <Button variant="outline" size="sm" onClick={() => setConfirmingId(null)}>
                      {t.staff.confirmNo}
                    </Button>
                    <Button
                      variant="destructive"
                      size="sm"
                      disabled={remove.isPending}
                      onClick={() =>
                        remove.mutate(professional.id, { onSuccess: () => setConfirmingId(null) })
                      }
                    >
                      {remove.isPending ? t.staff.removing : t.staff.confirmYes}
                    </Button>
                  </div>
                </div>
              ) : (
                canEdit && (
                  <div className="flex shrink-0">
                    <Button
                      variant="ghost"
                      size="icon"
                      aria-label={t.staff.edit(professional.name)}
                      onClick={() => {
                        update.reset();
                        setEditing({ mode: 'edit', professional });
                      }}
                    >
                      <Pencil />
                    </Button>
                    <Button
                      variant="ghost"
                      size="icon"
                      aria-label={t.staff.remove(professional.name)}
                      onClick={() => {
                        remove.reset();
                        setConfirmingId(professional.id);
                      }}
                    >
                      <Trash2 />
                    </Button>
                  </div>
                )
              )}
            </li>
          );
        })}
      </ul>
    </section>
  );
}
