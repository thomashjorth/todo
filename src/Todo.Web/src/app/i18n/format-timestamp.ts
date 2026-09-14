/**
 * Formats a timestamp in one language. A timestamp is an instant rather than a date, which is the
 * whole difference from `formatDeadline` next door: a deadline is taken apart by hand so it cannot
 * slide a day, while an instant has to be read in the reader's own zone. 13:02 UTC is 15:02 in
 * Copenhagen, and an instant late enough in the day belongs to the next one locally.
 *
 * `dateStyle: 'long'` with `timeStyle: 'short'` is what Danish spells the classic way - measured
 * rather than chosen: `14. september 2026 kl. 15.02`, with the month written out, `kl.` in front
 * and a period between the hours and the minutes. English gets its own shape from the same call.
 *
 * Guarded rather than trusted, because `Intl` throws a `RangeError` on a date it cannot read - so
 * without this the review screen would go blank instead of losing one line. `formatDeadline`
 * answers an empty string for the same reason.
 */
export function formatTimestamp(value: string | null | undefined, lang: string): string {
  const moment = new Date(value ?? '');

  if (Number.isNaN(moment.getTime())) {
    return '';
  }

  return new Intl.DateTimeFormat(lang, { dateStyle: 'long', timeStyle: 'short' }).format(moment);
}
