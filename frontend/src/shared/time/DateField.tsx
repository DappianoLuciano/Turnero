import { useState } from 'react';
import { t } from '@/shared/i18n';
import { Input } from '@/shared/ui/input';
import { formatDate, parseDate } from './business-time';

interface Props {
  id: string;
  /** `YYYY-MM-DD`, or empty when there is no valid date. */
  value: string;
  /** Called with `YYYY-MM-DD`, or `''` while the text is not a valid date. */
  onChange: (value: string) => void;
  invalid?: boolean;
  className?: string;
}

/**
 * Date input that always reads and shows `dd/mm/aaaa`. The native `<input type="date">` follows the
 * browser's locale (mm/dd/yyyy in many), so it is not used.
 */
export function DateField({ id, value, onChange, invalid, className }: Props) {
  // While the user types, show their text; once they leave, show the canonical format again.
  const [draft, setDraft] = useState<string | null>(null);

  return (
    <Input
      id={id}
      className={className}
      inputMode="numeric"
      autoComplete="off"
      placeholder={t.common.datePlaceholder}
      aria-invalid={invalid}
      value={draft ?? formatDate(value)}
      onChange={(event) => {
        setDraft(event.target.value);
        onChange(parseDate(event.target.value) ?? '');
      }}
      onBlur={() => setDraft(null)}
    />
  );
}
