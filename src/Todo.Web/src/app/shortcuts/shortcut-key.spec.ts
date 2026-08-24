import { shortcutKey, shortcutLabel } from './shortcut-key';

describe('shortcutKey', () => {
  // De to lag er hele grunden til at nøglen ikke længere er bogstavet alene: Alt+O er
  // navigationen til opgavelisten, og Alt+Shift+O er panelets opgavestiller-felt.
  it('should keep the two layers apart for the same letter', () => {
    expect(shortcutKey('alt', 'o')).not.toBe(shortcutKey('alt-shift', 'o'));
  });

  // Alt+Shift+D rapporterer 'D' fra tastaturet, Alt+D rapporterer 'd'. Nøglen skal være den
  // samme, uanset hvilken vej den kom.
  it('should fold the case, because Alt+Shift reports an upper-case key', () => {
    expect(shortcutKey('alt-shift', 'D')).toBe(shortcutKey('alt-shift', 'd'));
  });

  it('should read the label off the same two fields the key is built from', () => {
    expect(shortcutLabel('alt', 'k')).toBe('Alt+K');
    expect(shortcutLabel('alt-shift', 'd')).toBe('Alt+Shift+D');
  });
});
