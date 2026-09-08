import { shortcutKey, shortcutLabel } from './shortcut-key';

describe('shortcutKey', () => {
  // The two layers are the whole reason the key is no longer the letter on its own: Alt+O is the
  // navigation to the task list, and Alt+Shift+O is the panel's requester field.
  it('should keep the two layers apart for the same letter', () => {
    expect(shortcutKey('alt', 'o')).not.toBe(shortcutKey('alt-shift', 'o'));
  });

  // Alt+Shift+D reports 'D' from the keyboard, Alt+D reports 'd'. The key has to be the same,
  // whichever way it arrived.
  it('should fold the case, because Alt+Shift reports an upper-case key', () => {
    expect(shortcutKey('alt-shift', 'D')).toBe(shortcutKey('alt-shift', 'd'));
  });

  it('should read the label off the same two fields the key is built from', () => {
    expect(shortcutLabel('alt', 'k')).toBe('Alt+K');
    expect(shortcutLabel('alt-shift', 'd')).toBe('Alt+Shift+D');
  });
});
