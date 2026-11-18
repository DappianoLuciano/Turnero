import { zodResolver } from '@hookform/resolvers/zod';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { errorMessage } from '@/shared/api/problem';
import { t } from '@/shared/i18n';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Label } from '@/shared/ui/label';
import type { Customer } from './customers-api';

const v = t.customers.validation;

const phonePattern = /^\+?[0-9]{6,15}$/;

const schema = z.object({
  name: z.string().trim().min(1, v.nameRequired).max(120, v.nameTooLong),
  phone: z
    .string()
    .trim()
    .refine((p) => p === '' || phonePattern.test(p.replace(/[\s\-.()]/g, '')), v.phoneInvalid),
  email: z
    .string()
    .trim()
    .refine((e) => e === '' || z.email().safeParse(e).success, v.emailInvalid)
    .refine((e) => e.length <= 254, v.emailInvalid),
  notes: z.string().trim().max(1000, v.notesTooLong),
});

export type CustomerFormValues = z.infer<typeof schema>;

interface Props {
  customer?: Customer;
  pending: boolean;
  error: unknown;
  onSubmit: (values: CustomerFormValues) => void;
  onCancel: () => void;
}

export function CustomerForm({ customer, pending, error, onSubmit, onCancel }: Props) {
  const form = useForm<CustomerFormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: customer?.name ?? '',
      phone: customer?.phone ?? '',
      email: customer?.email ?? '',
      notes: customer?.notes ?? '',
    },
  });
  const { errors } = form.formState;

  return (
    <form
      noValidate
      aria-label={customer ? t.customers.form.editTitle : t.customers.form.createTitle}
      className="bg-card flex flex-col gap-4 rounded-xl border p-4 shadow-sm"
      onSubmit={form.handleSubmit(onSubmit)}
    >
      <h2 className="text-lg font-semibold">
        {customer ? t.customers.form.editTitle : t.customers.form.createTitle}
      </h2>

      <div className="flex flex-col gap-2">
        <Label htmlFor="customer-name">{t.customers.form.name}</Label>
        <Input
          id="customer-name"
          placeholder={t.customers.form.namePlaceholder}
          aria-invalid={!!errors.name}
          {...form.register('name')}
        />
        {errors.name && <p className="text-destructive text-sm">{errors.name.message}</p>}
      </div>

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="flex flex-col gap-2">
          <Label htmlFor="customer-phone">{t.customers.form.phone}</Label>
          <Input
            id="customer-phone"
            type="tel"
            inputMode="tel"
            placeholder={t.customers.form.phonePlaceholder}
            aria-invalid={!!errors.phone}
            {...form.register('phone')}
          />
          {errors.phone && <p className="text-destructive text-sm">{errors.phone.message}</p>}
        </div>
        <div className="flex flex-col gap-2">
          <Label htmlFor="customer-email">{t.customers.form.email}</Label>
          <Input id="customer-email" type="email" aria-invalid={!!errors.email} {...form.register('email')} />
          {errors.email && <p className="text-destructive text-sm">{errors.email.message}</p>}
        </div>
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor="customer-notes">{t.customers.form.notes}</Label>
        <textarea
          id="customer-notes"
          rows={3}
          aria-invalid={!!errors.notes}
          className="border-input bg-background focus-visible:ring-ring/50 min-h-16 w-full rounded-md border px-3 py-2 text-sm shadow-xs outline-none focus-visible:ring-[3px]"
          {...form.register('notes')}
        />
        {errors.notes && <p className="text-destructive text-sm">{errors.notes.message}</p>}
      </div>

      {error != null && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(error)}
        </p>
      )}

      <div className="flex justify-end gap-2">
        <Button type="button" variant="outline" onClick={onCancel}>
          {t.customers.form.cancel}
        </Button>
        <Button type="submit" disabled={pending}>
          {pending ? t.customers.form.saving : t.customers.form.save}
        </Button>
      </div>
    </form>
  );
}
