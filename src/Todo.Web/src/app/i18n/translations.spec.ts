import da from '../../../public/i18n/da.json';
import en from '../../../public/i18n/en.json';

// Dotted paths, so a key that is an object in one file and a string in the other is caught too.
function keysOf(value: unknown, prefix = ''): string[] {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) {
    return [prefix];
  }

  return Object.entries(value)
    .flatMap(([key, child]) => keysOf(child, prefix ? `${prefix}.${key}` : key))
    .sort();
}

describe('translation files', () => {
  const danish = keysOf(da);
  const english = keysOf(en);

  it('should translate something at all', () => {
    expect(danish.length).toBeGreaterThan(0);
  });

  it('should have no key that only Danish has', () => {
    expect(danish.filter((key) => !english.includes(key))).toEqual([]);
  });

  it('should have no key that only English has', () => {
    expect(english.filter((key) => !danish.includes(key))).toEqual([]);
  });
});
