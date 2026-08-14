import { resolveLanguage } from './resolve-language';

describe('resolveLanguage', () => {
  it('should prefer a stored choice over the system language', () => {
    expect(resolveLanguage('en', 'da-DK')).toBe('en');
    expect(resolveLanguage('da', 'en-GB')).toBe('da');
  });

  it.each(['da-DK', 'DA', 'da'])('should read %s as Danish', (system) => {
    expect(resolveLanguage(null, system)).toBe('da');
  });

  it.each(['en-GB', 'de-DE', ''])('should fall back to English for %s', (system) => {
    expect(resolveLanguage(null, system)).toBe('en');
  });

  it('should ignore a stored language it cannot show', () => {
    expect(resolveLanguage('klingon', 'en-GB')).toBe('en');
  });

  // An app you cannot read is worse than one in a foreign language you can.
  it('should fall back to English rather than Danish for an unknown system language', () => {
    expect(resolveLanguage('klingon', 'fr-FR')).toBe('en');
  });
});
