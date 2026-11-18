import { zodResolver } from '@hookform/resolvers/zod';
import { useMemo, useState } from 'react';
import { Controller, useForm, useWatch } from 'react-hook-form';
import { z } from 'zod';
import type { Service } from '@/features/services/services-api';
import type { Professional } from '@/features/staff/staff-api';
import { errorMessage } from '@/shared/api/problem';
import { t } from '@/shared/i18n';
import { isValidDate, parseTime } from '@/shared/time/business-time';
import { DateField } from '@/shared/time/DateField';
import { isOutsideHours, type DayAvailability } from '@/features/schedules/availability';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Label } from '@/shared/ui/label';
import { CustomerPicker } from './CustomerPicker';

const v = t.agenda.validation;
const TIME_PATTERN = /^([01]\d|2[0-3]):[0-5]\d$/;

function buildSchema(requireCustomer: boolean) {
  return z.object({
    customerId: requireCustomer ? z.string().min(1, v.customerRequired) : z.string(),
    professionalId: z.string().min(1, v.professionalRequired),
    serviceId: z.string().min(1, v.serviceRequired),
    date: z.string().refine(isValidDate, v.dateInvalid),
    time: z.string().regex(/^([01]\d|2[0-3]):[0-5]\d$/, v.timeInvalid),
    notes: z.string().trim().max(500, v.notesTooLong),
  });
}

export type AppointmentFormValues = z.infer<ReturnType<typeof buildSchema>>;

export interface AppointmentFormInitial {
  customer: { id: string; name: string } | null;
  professionalId: string;
  serviceId: string;
  date: string;
  time: string;
  notes: string;
}

interface Props {
  mode: 'create' | 'edit';
  initial: AppointmentFormInitial;
  professionals: Professional[];
  services: Service[];
  /** Working hours and closed days of a professional on a local date. */
  availabilityOn: (professionalId: string, date: string) => DayAvailability;
  pending: boolean;
  error: unknown;
  onSubmit: (values: AppointmentFormValues) => void;
  onCancel: () => void;
}

const selectClass =
  'border-input bg-background focus-visible:ring-ring/50 h-9 w-full rounded-md border px-3 text-sm shadow-xs outline-none focus-visible:ring-[3px]';

export function AppointmentForm({
  mode,
  initial,
  professionals,
  services,
  availabilityOn,
  pending,
  error,
  onSubmit,
  onCancel,
}: Props) {
  const schema = useMemo(() => buildSchema(mode === 'create'), [mode]);
  const form = useForm<AppointmentFormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      customerId: initial.customer?.id ?? '',
      professionalId: initial.professionalId,
      serviceId: initial.serviceId,
      date: initial.date,
      time: initial.time,
      notes: initial.notes,
    },
  });
  const [customer, setCustomer] = useState(initial.customer);
  const { errors } = form.formState;

  const professionalId = useWatch({ control: form.control, name: 'professionalId' });
  // Inactive professionals/services stay selectable only when the appointment already uses them.
  const professionalOptions = professionals.filter((p) => p.isActive || p.id === initial.professionalId);
  const allowedServiceIds = new Set(professionals.find((p) => p.id === professionalId)?.serviceIds ?? []);
  const serviceOptions = services.filter(
    (s) => allowedServiceIds.has(s.id) && (s.isActive || s.id === initial.serviceId),
  );

  const [watchedDate, watchedTime, watchedServiceId] = useWatch({
    control: form.control,
    name: ['date', 'time', 'serviceId'],
  });
  const warning = outsideHoursWarning();

  function outsideHoursWarning(): string | null {
    if (!isValidDate(watchedDate) || !TIME_PATTERN.test(watchedTime) || !professionalId) return null;
    const availability = availabilityOn(professionalId, watchedDate);
    const duration = services.find((s) => s.id === watchedServiceId)?.durationMinutes ?? 0;
    const start = parseTime(watchedTime);
    if (availability.closed) return t.schedules.agenda.closedDay(availability.closedReason);
    return isOutsideHours(availability, start, start + Math.max(duration, 1))
      ? t.schedules.agenda.outsideHours
      : null;
  }

  const title = mode === 'create' ? t.agenda.form.createTitle : t.agenda.form.editTitle;

  return (
    <form
      noValidate
      aria-label={title}
      className="bg-card flex flex-col gap-4 rounded-xl border p-4 shadow-sm"
      onSubmit={form.handleSubmit(onSubmit)}
    >
      <h2 className="text-lg font-semibold">{title}</h2>

      {mode === 'create' ? (
        <CustomerPicker
          selected={customer}
          error={errors.customerId?.message}
          onSelect={(picked) => {
            setCustomer(picked);
            form.setValue('customerId', picked?.id ?? '', { shouldValidate: form.formState.isSubmitted });
          }}
        />
      ) : (
        <div className="flex flex-col gap-1">
          <span className="text-sm font-medium">{t.agenda.form.customer}</span>
          <span className="text-sm">{initial.customer?.name}</span>
        </div>
      )}

      <div className="grid gap-4 sm:grid-cols-2">
        <div className="flex flex-col gap-2">
          <Label htmlFor="appointment-professional">{t.agenda.form.professional}</Label>
          <select
            id="appointment-professional"
            className={selectClass}
            aria-invalid={!!errors.professionalId}
            {...form.register('professionalId', { onChange: () => form.setValue('serviceId', '') })}
          >
            {professionalOptions.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
          {errors.professionalId && (
            <p className="text-destructive text-sm">{errors.professionalId.message}</p>
          )}
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="appointment-service">{t.agenda.form.service}</Label>
          <select
            id="appointment-service"
            className={selectClass}
            aria-invalid={!!errors.serviceId}
            {...form.register('serviceId')}
          >
            <option value="">{t.agenda.form.servicePlaceholder}</option>
            {serviceOptions.map((s) => (
              <option key={s.id} value={s.id}>
                {s.name}
              </option>
            ))}
          </select>
          {serviceOptions.length === 0 && professionalId && (
            <p className="text-muted-foreground text-sm">{t.agenda.form.noServicesForProfessional}</p>
          )}
          {errors.serviceId && <p className="text-destructive text-sm">{errors.serviceId.message}</p>}
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="appointment-date">{t.agenda.form.date}</Label>
          <Controller
            control={form.control}
            name="date"
            render={({ field }) => (
              <DateField
                id="appointment-date"
                value={field.value}
                invalid={!!errors.date}
                onChange={field.onChange}
              />
            )}
          />
          {errors.date && <p className="text-destructive text-sm">{errors.date.message}</p>}
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor="appointment-time">{t.agenda.form.time}</Label>
          <Input
            id="appointment-time"
            type="time"
            step={300}
            aria-invalid={!!errors.time}
            {...form.register('time')}
          />
          {errors.time && <p className="text-destructive text-sm">{errors.time.message}</p>}
        </div>
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor="appointment-notes">{t.agenda.form.notes}</Label>
        <Input id="appointment-notes" aria-invalid={!!errors.notes} {...form.register('notes')} />
        {errors.notes && <p className="text-destructive text-sm">{errors.notes.message}</p>}
      </div>

      {warning && (
        <p
          role="status"
          className="rounded-md border border-amber-500 bg-amber-50 p-2 text-sm text-amber-950 dark:bg-amber-950 dark:text-amber-50"
        >
          {warning}
        </p>
      )}

      {error != null && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(error)}
        </p>
      )}

      <div className="flex justify-end gap-2">
        <Button type="button" variant="outline" onClick={onCancel}>
          {t.agenda.form.cancel}
        </Button>
        <Button type="submit" disabled={pending}>
          {pending ? t.agenda.form.saving : t.agenda.form.save}
        </Button>
      </div>
    </form>
  );
}
