/** Alt alene bærer navigationen og listens kontroller; Alt+Shift bærer panelets felter. */
export type ShortcutModifier = 'alt' | 'alt-shift';

/**
 * The registry key: layer plus key, so Alt+O and Alt+Shift+O are two entries rather than one.
 *
 * Lower-cased, because Alt+Shift+D reports `event.key === 'D'` while Alt+D reports 'd', and the
 * registration and the lookup have to meet.
 */
export function shortcutKey(modifier: ShortcutModifier, key: string): string {
  return `${modifier}+${key.toLowerCase()}`;
}

/**
 * What `aria-keyshortcuts` says. Derived from the same two fields as the key above, so the label a
 * screen reader announces cannot drift from the combination that actually works.
 */
export function shortcutLabel(modifier: ShortcutModifier, key: string): string {
  const letter = key.toUpperCase();

  return modifier === 'alt-shift' ? `Alt+Shift+${letter}` : `Alt+${letter}`;
}
