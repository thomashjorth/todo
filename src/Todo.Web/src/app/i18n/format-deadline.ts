const isoDate = /^(\d{4})-(\d{2})-(\d{2})$/;

/**
 * Formats a date-only deadline in one language. The string is taken apart by hand because
 * `new Date('2026-08-13')` is UTC midnight, which reads as the 12th west of Greenwich;
 * `new Date(y, m - 1, d)` is local midnight and cannot be read as anything else.
 */
export function formatDeadline(value: string | null | undefined, lang: string): string {
  const parts = isoDate.exec(value ?? '');
  if (!parts) {
    return '';
  }

  const [, year, month, day] = parts.map(Number);

  return new Intl.DateTimeFormat(lang, {
    day: 'numeric',
    month: 'short',
    year: 'numeric',
  }).format(new Date(year, month - 1, day));
}
