import { formatTimestamp } from './format-timestamp';

describe('formatTimestamp', () => {
  const moment = '2026-09-14T13:02:56.3347304+00:00';

  it('should write the moment the classic Danish way rather than as the wire string', () => {
    const danish = formatTimestamp(moment, 'da');

    // The whole point: what the user saw was the raw wire value.
    expect(danish).not.toContain('+00:00');
    expect(danish).not.toMatch(/\d{2}T\d{2}/);

    // "14. september 2026 kl. 15.02" - the month spelled out, kl. before the time, and a period
    // between hours and minutes. Measured against Intl for da rather than chosen.
    expect(danish).toContain('september');
    expect(danish).toContain('2026');
    expect(danish).toMatch(/kl\. \d{2}\.\d{2}/);
  });

  it('should shape the same moment differently per language', () => {
    expect(formatTimestamp(moment, 'da')).not.toBe(formatTimestamp(moment, 'en'));
  });

  /**
   * A timestamp is an instant rather than a date, so it has to be read in the reader's own zone -
   * a deadline is the opposite case and formatDeadline says so. Derived from the same Date rather
   * than written down, so the assertion holds in every zone.
   *
   * Note what it cannot see: on a runner whose zone *is* UTC the two hours coincide, and an
   * implementation that formatted the wire string's own digits would pass. Here it has teeth.
   */
  it('should show the hour of the reader, not the hour on the wire', () => {
    const local = new Date(moment);
    const hour = String(local.getHours()).padStart(2, '0');
    const minute = String(local.getMinutes()).padStart(2, '0');

    expect(formatTimestamp(moment, 'da')).toContain(`kl. ${hour}.${minute}`);
  });

  /**
   * Intl throws a RangeError on a date it cannot read, so this is a guard rather than a courtesy -
   * without it the review screen would go blank instead of losing one line.
   */
  it.each([null, undefined, '', '   ', 'i morgen'])(
    'should render nothing rather than a broken moment for %o',
    (value) => {
      expect(formatTimestamp(value, 'da')).toBe('');
      expect(formatTimestamp(value, 'en')).toBe('');
    },
  );
});
