import { formatDeadline } from './format-deadline';

describe('formatDeadline', () => {
  it('should keep the day it was given while shaping it per language', () => {
    const danish = formatDeadline('2026-08-13', 'da');
    const english = formatDeadline('2026-08-13', 'en');

    expect(danish).toMatch(/\b13\b/);
    expect(english).toMatch(/\b13\b/);
    expect(danish).toContain('2026');
    expect(english).toContain('2026');
    expect(danish).not.toBe(english);
  });

  // A deadline read as UTC midnight slides a day west of Greenwich, and at New Year a day is a year.
  it.each([
    { deadline: '2025-12-31', day: '31', year: '2025', otherYear: '2026' },
    { deadline: '2026-01-01', day: '1', year: '2026', otherYear: '2025' },
  ])('should keep $deadline on its own day and year', ({ deadline, day, year, otherYear }) => {
    for (const lang of ['da', 'en']) {
      const formatted = formatDeadline(deadline, lang);

      expect(formatted).toMatch(new RegExp(`\\b${day}\\b`));
      expect(formatted).toContain(year);
      expect(formatted).not.toContain(otherYear);
    }
  });

  it.each([null, undefined, '', '   ', 'i morgen'])(
    'should render nothing rather than a broken date for %o',
    (deadline) => {
      expect(formatDeadline(deadline, 'da')).toBe('');
      expect(formatDeadline(deadline, 'en')).toBe('');
    },
  );
});
