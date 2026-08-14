import { pluralKey } from './plural-key';

describe('pluralKey', () => {
  it('should use the plural form for none', () => {
    expect(pluralKey(0, 'retro.skipped')).toBe('retro.skipped.other');
  });

  it('should use the singular form for exactly one', () => {
    expect(pluralKey(1, 'retro.skipped')).toBe('retro.skipped.one');
  });

  it('should use the plural form for more than one', () => {
    expect(pluralKey(2, 'retro.skipped')).toBe('retro.skipped.other');
  });
});
