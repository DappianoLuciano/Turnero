import { zodResolver } from '@hookform/resolvers/zod';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { errorMessage } from '@/shared/api/problem';
import { t } from '@/shared/i18n';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Label } from '@/shared/ui/label';
import type { Service } from './services-api';

const v = t.services.validation;

const schema = z.object({
  name: z.string().trim().min(1, v.nameRequired).max(120, v.nameTooLong),
  description: z.string().trim().max(500, v.descriptionTooLong),
  durationMinutes: z
    .number({ error: v.durationRange })
    .int(v.durationRange)
    .min(5, v.durationRange)
    .max(480, v.durationRange),
  price: z
    .number({ error: v.priceInvalid })
    .min(0, v.priceInvalid)
    .max(9_999_999, v.priceInvalid)
    .refine((n) => Math.abs(n * 100 - Math.round(n * 100)) < 1e-6, v.priceInvalid),
  isActive: z.boolean(),
});

export type ServiceFormValues = z.infer<typeof schema>;

interface Props {
  service?: Service;
  pending: boolean;
  error: unknown;
  onSubmit: (values: ServiceFormValues) => void;
  onCancel: () => void;
}

export function ServiceForm({ service, pending, error, onSubmit, onCancel }: Props) {
  const form = useForm<ServiceFormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: service?.name ?? '',
      description: service?.description ?? '',
      durationMinutes: service?.durationMinutes ?? 30,
      price: service?.price ?? 0,
      isActive: service?.isActive ?? true,
    },
  });
  const { errors } = form.formState;

  return (
    <form
      noValidate
      aria-label={service ? t.services.form.editTitle : t.services.form.createTitle}
      className="bg-card flex flex-col gap-4 rounded-xl border p-4 shadow-sm"
      onSubmit={form.handleSubmit(onSubmit)}
    >
      <h2 className="text-lg font-semibold">
        {service ? t.services.form.editTitle : t.services.form.createTitle}
      </h2>

      <div className="flex flex-col gap-2">
        <Label htmlFor="service-name">{t.services.form.name}</Label>
        <Input
          id="service-name"
          placeholder={t.services.form.namePlaceholder}
          aria-invalid={!!errors.name}
          {...form.register('name')}
        />
        {errors.name && <p className="text-destructive text-sm">{errors.name.message}</p>}
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor="service-description">{t.services.form.description}</Label>
        <Input
          id="service-description"
          aria-invalid={!!errors.description}
          {...form.register('description')}
        />
        {errors.description && <p className="text-destructive text-sm">{errors.description.message}</p>}
      </div>

      <div className="grid grid-cols-2 gap-4">
        <div className="flex flex-col gap-2">
          <Label htmlFor="service-duration">{t.services.form.duration}</Label>
          <Input
            id="service-duration"
            type="number"
            inputMode="numeric"
            aria-invalid={!!errors.durationMinutes}
            {...form.register('durationMinutes', { valueAsNumber: true })}
          />
          {errors.durationMinutes && (
            <p className="text-destructive text-sm">{errors.durationMinutes.message}</p>
          )}
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="service-price">{t.services.form.price}</Label>
          <Input
            id="service-price"
            type="number"
            inputMode="decimal"
            step="0.01"
            aria-invalid={!!errors.price}
            {...form.register('price', { valueAsNumber: true })}
          />
          {errors.price && <p className="text-destructive text-sm">{errors.price.message}</p>}
        </div>
      </div>

      {service && (
        <label className="flex items-center gap-2 text-sm">
          <input type="checkbox" className="size-4" {...form.register('isActive')} />
          {t.services.form.active}
        </label>
      )}

      {error != null && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(error)}
        </p>
      )}

      <div className="flex justify-end gap-2">
        <Button type="button" variant="outline" onClick={onCancel}>
          {t.services.form.cancel}
        </Button>
        <Button type="submit" disabled={pending}>
          {pending ? t.services.form.saving : t.services.form.save}
        </Button>
      </div>
    </form>
  );
}
