import { zodResolver } from '@hookform/resolvers/zod';
import { useForm } from 'react-hook-form';
import { z } from 'zod';
import { useSession } from '@/features/auth/use-session';
import { useBusiness, useUpdateBusinessSettings } from '@/features/business/business-api';
import { errorMessage } from '@/shared/api/problem';
import { t } from '@/shared/i18n';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Label } from '@/shared/ui/label';

const v = t.settings.validation;

const schema = z.object({
  phone: z.string().trim().max(30, v.phoneTooLong),
  noticeHours: z
    .number({ error: v.noticeRange })
    .int(v.noticeRange)
    .min(0, v.noticeRange)
    .max(720, v.noticeRange),
});

type FormValues = z.infer<typeof schema>;

/** Business settings. Only the owner can change them. */
export function SettingsPage() {
  const session = useSession();
  const business = useBusiness();
  const canEdit = session.status === 'authenticated' && session.user.role === 'Owner';

  return (
    <section className="flex max-w-xl flex-col gap-4">
      <h1 className="text-2xl font-semibold">{t.settings.title}</h1>
      {!canEdit && <p className="text-muted-foreground text-sm">{t.settings.ownerOnly}</p>}
      {business.isPending && <p className="text-muted-foreground">{t.app.loading}</p>}
      {business.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(business.error)}
        </p>
      )}
      {business.data && (
        <SettingsForm
          canEdit={canEdit}
          phone={business.data.phone ?? ''}
          noticeHours={Number(business.data.customerChangeNoticeHours ?? 12)}
        />
      )}
    </section>
  );
}

function SettingsForm({
  canEdit,
  phone,
  noticeHours,
}: {
  canEdit: boolean;
  phone: string;
  noticeHours: number;
}) {
  const update = useUpdateBusinessSettings();
  const form = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { phone, noticeHours },
  });
  const { errors } = form.formState;

  return (
    <form
      noValidate
      className="bg-card flex flex-col gap-4 rounded-xl border p-4 shadow-sm"
      onSubmit={form.handleSubmit((values) =>
        update.mutate({ phone: values.phone || null, customerChangeNoticeHours: values.noticeHours }),
      )}
    >
      <div className="flex flex-col gap-2">
        <Label htmlFor="settings-phone">{t.settings.phone}</Label>
        <Input
          id="settings-phone"
          type="tel"
          inputMode="tel"
          disabled={!canEdit}
          aria-invalid={!!errors.phone}
          {...form.register('phone')}
        />
        <p className="text-muted-foreground text-xs">{t.settings.phoneHint}</p>
        {errors.phone && <p className="text-destructive text-sm">{errors.phone.message}</p>}
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor="settings-notice">{t.settings.noticeHours}</Label>
        <Input
          id="settings-notice"
          type="number"
          inputMode="numeric"
          min={0}
          max={720}
          disabled={!canEdit}
          aria-invalid={!!errors.noticeHours}
          {...form.register('noticeHours', { valueAsNumber: true })}
        />
        <p className="text-muted-foreground text-xs">{t.settings.noticeHint}</p>
        {errors.noticeHours && <p className="text-destructive text-sm">{errors.noticeHours.message}</p>}
      </div>

      {update.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(update.error)}
        </p>
      )}
      {update.isSuccess && (
        <p role="status" className="text-sm text-emerald-700">
          {t.settings.saved}
        </p>
      )}
      {canEdit && (
        <Button type="submit" className="self-end" disabled={update.isPending}>
          {update.isPending ? t.settings.saving : t.settings.save}
        </Button>
      )}
    </form>
  );
}
