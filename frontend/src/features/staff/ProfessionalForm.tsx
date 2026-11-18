import { zodResolver } from '@hookform/resolvers/zod';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import type { Service } from '@/features/services/services-api';
import { errorMessage } from '@/shared/api/problem';
import { t } from '@/shared/i18n';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Label } from '@/shared/ui/label';
import type { Professional } from './staff-api';

const v = t.staff.validation;

const schema = z.object({
  name: z.string().trim().min(1, v.nameRequired).max(120, v.nameTooLong),
  isActive: z.boolean(),
  serviceIds: z.array(z.string()),
});

export type ProfessionalFormValues = z.infer<typeof schema>;

interface Props {
  professional?: Professional;
  services: Service[];
  pending: boolean;
  error: unknown;
  onSubmit: (values: ProfessionalFormValues) => void;
  onCancel: () => void;
}

export function ProfessionalForm({ professional, services, pending, error, onSubmit, onCancel }: Props) {
  const form = useForm<ProfessionalFormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: professional?.name ?? '',
      isActive: professional?.isActive ?? true,
      serviceIds: professional?.serviceIds ?? [],
    },
  });
  const { errors } = form.formState;

  return (
    <form
      noValidate
      aria-label={professional ? t.staff.form.editTitle : t.staff.form.createTitle}
      className="bg-card flex flex-col gap-4 rounded-xl border p-4 shadow-sm"
      onSubmit={form.handleSubmit(onSubmit)}
    >
      <h2 className="text-lg font-semibold">
        {professional ? t.staff.form.editTitle : t.staff.form.createTitle}
      </h2>

      <div className="flex flex-col gap-2">
        <Label htmlFor="professional-name">{t.staff.form.name}</Label>
        <Input
          id="professional-name"
          placeholder={t.staff.form.namePlaceholder}
          aria-invalid={!!errors.name}
          {...form.register('name')}
        />
        {errors.name && <p className="text-destructive text-sm">{errors.name.message}</p>}
      </div>

      <fieldset className="flex flex-col gap-2">
        <legend className="text-sm font-medium">{t.staff.form.services}</legend>
        {services.length === 0 ? (
          <p className="text-muted-foreground text-sm">{t.staff.form.noServices}</p>
        ) : (
          <div className="grid gap-2 sm:grid-cols-2">
            {services.map((service) => (
              <label key={service.id} className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  className="size-4"
                  value={service.id}
                  {...form.register('serviceIds')}
                />
                {service.name}
              </label>
            ))}
          </div>
        )}
      </fieldset>

      {professional && (
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" className="size-4" {...form.register('isActive')} />
          {t.staff.form.active}
        </label>
      )}

      {error != null && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(error)}
        </p>
      )}

      <div className="flex justify-end gap-2">
        <Button type="button" variant="outline" onClick={onCancel}>
          {t.staff.form.cancel}
        </Button>
        <Button type="submit" disabled={pending}>
          {pending ? t.staff.form.saving : t.staff.form.save}
        </Button>
      </div>
    </form>
  );
}
