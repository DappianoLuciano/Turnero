import { Plus, Trash2 } from 'lucide-react';
import { useState, type FormEvent } from 'react';
import { Link } from 'react-router';
import { useSession } from '@/features/auth/use-session';
import { useBusiness } from '@/features/business/business-api';
import { useProfessionals, type Professional } from '@/features/staff/staff-api';
import { errorMessage } from '@/shared/api/problem';
import { t } from '@/shared/i18n';
import { addDays, formatDate, formatMinutes, parseTime, todayIn } from '@/shared/time/business-time';
import { DateField } from '@/shared/time/DateField';
import { Button } from '@/shared/ui/button';
import { Input } from '@/shared/ui/input';
import { Label } from '@/shared/ui/label';
import {
  useCreateScheduleException,
  useDeleteScheduleException,
  useReplaceSchedule,
  useScheduleExceptions,
  useSchedules,
  type WorkingBlock,
} from './schedules-api';

/** Display order: Monday first. Values are `dayOfWeek` (0 = Sunday). */
const WEEK_ORDER = [1, 2, 3, 4, 5, 6, 0];
const MINUTES_PER_DAY = 24 * 60;
const CLOSED_WINDOW_DAYS = 365;

const selectClass =
  'border-input bg-background focus-visible:ring-ring/50 h-9 w-full rounded-md border px-3 text-sm shadow-xs outline-none focus-visible:ring-[3px]';

interface DraftBlock {
  start: string;
  end: string;
}

/** Editable weekly schedule: for each `dayOfWeek`, its blocks as `HH:mm` text. */
type Draft = Record<number, DraftBlock[]>;

/** The input type="time" cannot show 24:00, so a block that ends at midnight is written 00:00. */
const endToText = (minute: number) => formatMinutes(minute === MINUTES_PER_DAY ? 0 : minute);
const endFromText = (text: string) => (parseTime(text) === 0 ? MINUTES_PER_DAY : parseTime(text));

function toDraft(blocks: WorkingBlock[]): Draft {
  const draft: Draft = { 0: [], 1: [], 2: [], 3: [], 4: [], 5: [], 6: [] };
  for (const b of blocks) {
    draft[b.dayOfWeek]?.push({ start: formatMinutes(b.startMinute), end: endToText(b.endMinute) });
  }
  return draft;
}

const TIME = /^([01]\d|2[0-3]):[0-5]\d$/;

/** Returns the blocks ready to send, or the validation message. */
function toBlocks(
  draft: Draft,
): { blocks: { dayOfWeek: number; startMinute: number; endMinute: number }[] } | { error: string } {
  const v = t.schedules.validation;
  const blocks: { dayOfWeek: number; startMinute: number; endMinute: number }[] = [];
  for (const day of WEEK_ORDER) {
    const dayBlocks: { startMinute: number; endMinute: number }[] = [];
    for (const b of draft[day] ?? []) {
      if (!TIME.test(b.start) || !TIME.test(b.end)) return { error: v.timeInvalid };
      const startMinute = parseTime(b.start);
      const endMinute = endFromText(b.end);
      if (endMinute <= startMinute) return { error: v.endBeforeStart };
      dayBlocks.push({ startMinute, endMinute });
    }
    dayBlocks.sort((a, b) => a.startMinute - b.startMinute);
    if (dayBlocks.some((b, i) => i > 0 && b.startMinute < (dayBlocks[i - 1]?.endMinute ?? 0))) {
      return { error: v.overlap };
    }
    blocks.push(...dayBlocks.map((b) => ({ dayOfWeek: day, ...b })));
  }
  return { blocks };
}

export function SchedulesPage() {
  const session = useSession();
  const canEdit = session.status === 'authenticated' && session.user.role === 'Owner';
  const professionals = useProfessionals();
  const schedules = useSchedules();
  const [selected, setSelected] = useState<string | null>(null);

  const active = (professionals.data ?? []).filter((p) => p.isActive);
  const current = active.find((p) => p.id === selected) ?? active[0];

  return (
    <section className="flex flex-col gap-6">
      <h1 className="text-2xl font-semibold">{t.schedules.title}</h1>

      {(professionals.isPending || schedules.isPending) && (
        <p className="text-muted-foreground">{t.app.loading}</p>
      )}
      {(professionals.error ?? schedules.error) != null && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(professionals.error ?? schedules.error)}
        </p>
      )}

      {professionals.isSuccess && active.length === 0 && (
        <div className="flex flex-col items-start gap-2">
          <p className="text-muted-foreground">{t.schedules.noProfessionals}</p>
          <Button asChild variant="outline">
            <Link to="/app/staff">{t.schedules.goToStaff}</Link>
          </Button>
        </div>
      )}

      {current && schedules.isSuccess && (
        <div className="flex flex-col gap-4">
          <div className="flex max-w-xs flex-col gap-2">
            <Label htmlFor="schedule-professional">{t.schedules.professional}</Label>
            <select
              id="schedule-professional"
              className={selectClass}
              value={current.id}
              onChange={(event) => setSelected(event.target.value)}
            >
              {active.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </div>
          <WeeklyEditor
            key={current.id}
            professional={current}
            blocks={schedules.data.filter((b) => b.professionalId === current.id)}
            canEdit={canEdit}
          />
        </div>
      )}

      {professionals.isSuccess && <ClosedDays professionals={active} canEdit={canEdit} />}
    </section>
  );
}

function WeeklyEditor({
  professional,
  blocks,
  canEdit,
}: {
  professional: Professional;
  blocks: WorkingBlock[];
  canEdit: boolean;
}) {
  const replace = useReplaceSchedule();
  const [draft, setDraft] = useState<Draft>(() => toDraft(blocks));
  const [error, setError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  const change = (next: Draft) => {
    setDraft(next);
    setSaved(false);
    replace.reset();
  };

  const updateBlock = (day: number, index: number, patch: Partial<DraftBlock>) =>
    change({ ...draft, [day]: (draft[day] ?? []).map((b, i) => (i === index ? { ...b, ...patch } : b)) });

  const addBlock = (day: number) =>
    change({ ...draft, [day]: [...(draft[day] ?? []), { start: '09:00', end: '18:00' }] });

  const removeBlock = (day: number, index: number) =>
    change({ ...draft, [day]: (draft[day] ?? []).filter((_, i) => i !== index) });

  const copyMonday = () => {
    const monday = draft[1] ?? [];
    change({ ...draft, 2: [...monday], 3: [...monday], 4: [...monday], 5: [...monday] });
  };

  const submit = (event: FormEvent) => {
    event.preventDefault();
    const result = toBlocks(draft);
    if ('error' in result) return setError(result.error);
    setError(null);
    replace.mutate(
      { professionalId: professional.id, blocks: result.blocks },
      { onSuccess: () => setSaved(true) },
    );
  };

  return (
    <form
      noValidate
      aria-label={t.schedules.weekly}
      className="bg-card flex flex-col gap-4 rounded-xl border p-4 shadow-sm"
      onSubmit={submit}
    >
      <h2 className="text-lg font-semibold">{t.schedules.weekly}</h2>
      {blocks.length === 0 && <p className="text-muted-foreground text-sm">{t.schedules.notConfigured}</p>}

      <div className="flex flex-col gap-3">
        {WEEK_ORDER.map((day) => {
          const dayName = t.schedules.days[WEEK_ORDER.indexOf(day)] ?? '';
          const dayBlocks = draft[day] ?? [];
          return (
            <div
              key={day}
              className="grid gap-2 border-b pb-3 last:border-b-0 last:pb-0 sm:grid-cols-[8rem_1fr] sm:items-start"
            >
              <span className="text-sm font-medium">{dayName}</span>
              <div className="flex flex-col gap-2">
                {dayBlocks.length === 0 && (
                  <span className="text-muted-foreground text-sm">{t.schedules.dayOff}</span>
                )}
                {dayBlocks.map((block, index) => (
                  <div key={index} className="flex flex-wrap items-center gap-2">
                    <Input
                      type="time"
                      className="w-32"
                      aria-label={`${t.schedules.block(dayName, index + 1)}: ${t.schedules.from}`}
                      value={block.start}
                      disabled={!canEdit}
                      onChange={(event) => updateBlock(day, index, { start: event.target.value })}
                    />
                    <span aria-hidden>–</span>
                    <Input
                      type="time"
                      className="w-32"
                      aria-label={`${t.schedules.block(dayName, index + 1)}: ${t.schedules.to}`}
                      value={block.end}
                      disabled={!canEdit}
                      onChange={(event) => updateBlock(day, index, { end: event.target.value })}
                    />
                    {canEdit && (
                      <Button
                        type="button"
                        variant="ghost"
                        size="icon"
                        aria-label={`${t.schedules.removeBlock}: ${t.schedules.block(dayName, index + 1)}`}
                        onClick={() => removeBlock(day, index)}
                      >
                        <Trash2 />
                      </Button>
                    )}
                  </div>
                ))}
                {canEdit && (
                  <div>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      aria-label={`${t.schedules.addBlock}: ${dayName}`}
                      onClick={() => addBlock(day)}
                    >
                      <Plus />
                      {t.schedules.addBlock}
                    </Button>
                  </div>
                )}
              </div>
            </div>
          );
        })}
      </div>

      {error && <p className="text-destructive text-sm">{error}</p>}
      {replace.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(replace.error)}
        </p>
      )}
      {saved && (
        <p role="status" className="text-sm">
          {t.schedules.saved}
        </p>
      )}

      {canEdit ? (
        <div className="flex flex-wrap gap-2">
          <Button type="submit" disabled={replace.isPending}>
            {replace.isPending ? t.schedules.saving : t.schedules.save}
          </Button>
          <Button type="button" variant="outline" onClick={copyMonday}>
            {t.schedules.copyMonday}
          </Button>
        </div>
      ) : (
        <p className="text-muted-foreground text-sm">{t.schedules.readOnly}</p>
      )}
    </form>
  );
}

function ClosedDays({ professionals, canEdit }: { professionals: Professional[]; canEdit: boolean }) {
  const business = useBusiness();
  const timeZone = business.data?.timeZone ?? null;
  const today = timeZone ? todayIn(timeZone) : null;
  const exceptions = useScheduleExceptions(today, today ? addDays(today, CLOSED_WINDOW_DAYS) : null);
  const create = useCreateScheduleException();
  const remove = useDeleteScheduleException();

  const [date, setDate] = useState('');
  const [scope, setScope] = useState('');
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);

  const nameOf = (id: string | null | undefined) =>
    id ? (professionals.find((p) => p.id === id)?.name ?? '') : t.schedules.closed.everyone;

  const submit = (event: FormEvent) => {
    event.preventDefault();
    if (!date) return setError(t.schedules.closed.dateInvalid);
    setError(null);
    create.mutate(
      { date, professionalId: scope || null, reason: reason.trim() || null },
      {
        onSuccess: () => {
          setDate('');
          setReason('');
        },
      },
    );
  };

  return (
    <div className="flex flex-col gap-3">
      <div>
        <h2 className="text-lg font-semibold">{t.schedules.closed.title}</h2>
        <p className="text-muted-foreground text-sm">{t.schedules.closed.description}</p>
      </div>

      {canEdit && (
        <form
          noValidate
          aria-label={t.schedules.closed.title}
          className="bg-card grid gap-3 rounded-xl border p-4 shadow-sm sm:grid-cols-[auto_1fr_2fr_auto] sm:items-end"
          onSubmit={submit}
        >
          <div className="flex flex-col gap-2">
            <Label htmlFor="closed-date">{t.schedules.closed.date}</Label>
            <DateField id="closed-date" className="w-36" value={date} invalid={!!error} onChange={setDate} />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="closed-scope">{t.schedules.closed.scope}</Label>
            <select
              id="closed-scope"
              className={selectClass}
              value={scope}
              onChange={(event) => setScope(event.target.value)}
            >
              <option value="">{t.schedules.closed.everyone}</option>
              {professionals.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.name}
                </option>
              ))}
            </select>
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="closed-reason">{t.schedules.closed.reason}</Label>
            <Input
              id="closed-reason"
              value={reason}
              maxLength={120}
              placeholder={t.schedules.closed.reasonPlaceholder}
              onChange={(event) => setReason(event.target.value)}
            />
          </div>
          <Button type="submit" disabled={create.isPending}>
            {create.isPending ? t.schedules.closed.adding : t.schedules.closed.add}
          </Button>
          {error && <p className="text-destructive text-sm sm:col-span-4">{error}</p>}
          {create.isError && (
            <p role="alert" className="text-destructive text-sm sm:col-span-4">
              {errorMessage(create.error)}
            </p>
          )}
        </form>
      )}

      {exceptions.isSuccess && exceptions.data.length === 0 && (
        <p className="text-muted-foreground text-sm">{t.schedules.closed.none}</p>
      )}
      {remove.isError && (
        <p role="alert" className="text-destructive text-sm">
          {errorMessage(remove.error)}
        </p>
      )}
      <ul className="flex flex-col gap-2 text-sm">
        {(exceptions.data ?? []).map((e) => (
          <li
            key={e.id}
            className="bg-card flex flex-wrap items-center justify-between gap-2 rounded-md border p-3"
          >
            <span>
              <span className="font-medium">{formatDate(e.date)}</span> · {nameOf(e.professionalId)}
              {e.reason && ` · ${e.reason}`}
            </span>
            {canEdit && (
              <Button
                size="sm"
                variant="outline"
                aria-label={`${t.schedules.closed.remove} ${formatDate(e.date)}`}
                disabled={remove.isPending}
                onClick={() => remove.mutate(e.id)}
              >
                {t.schedules.closed.remove}
              </Button>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}
