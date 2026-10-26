/**
 * A tag's colour, by the slot the server gave it (TagColors in Todo.Core): the rainbow's seven, then
 * each again in two more shades (the tag-chips design). Written out in full rather than built from the
 * hue's name, because Tailwind finds a class only where it stands whole in the source.
 *
 * `filled` paints a chip that is carried or pressed; `outline` is the border of one that is not, whose
 * text stays the page's own.
 */
export interface TagColor {
  readonly filled: string;
  readonly outline: string;
}

export const tagColors: readonly TagColor[] = [
  // Light.
  {
    filled: 'bg-red-100 text-red-900 dark:bg-red-900 dark:text-red-100',
    outline: 'border-red-400 dark:border-red-600',
  },
  {
    filled: 'bg-orange-100 text-orange-900 dark:bg-orange-900 dark:text-orange-100',
    outline: 'border-orange-400 dark:border-orange-600',
  },
  {
    filled: 'bg-yellow-100 text-yellow-900 dark:bg-yellow-900 dark:text-yellow-100',
    outline: 'border-yellow-400 dark:border-yellow-600',
  },
  {
    filled: 'bg-green-100 text-green-900 dark:bg-green-900 dark:text-green-100',
    outline: 'border-green-400 dark:border-green-600',
  },
  {
    filled: 'bg-blue-100 text-blue-900 dark:bg-blue-900 dark:text-blue-100',
    outline: 'border-blue-400 dark:border-blue-600',
  },
  {
    filled: 'bg-indigo-100 text-indigo-900 dark:bg-indigo-900 dark:text-indigo-100',
    outline: 'border-indigo-400 dark:border-indigo-600',
  },
  {
    filled: 'bg-violet-100 text-violet-900 dark:bg-violet-900 dark:text-violet-100',
    outline: 'border-violet-400 dark:border-violet-600',
  },
  // Middle.
  {
    filled: 'bg-red-300 text-red-950 dark:bg-red-700 dark:text-red-50',
    outline: 'border-red-500 dark:border-red-500',
  },
  {
    filled: 'bg-orange-300 text-orange-950 dark:bg-orange-700 dark:text-orange-50',
    outline: 'border-orange-500 dark:border-orange-500',
  },
  {
    filled: 'bg-yellow-300 text-yellow-950 dark:bg-yellow-700 dark:text-yellow-50',
    outline: 'border-yellow-500 dark:border-yellow-500',
  },
  {
    filled: 'bg-green-300 text-green-950 dark:bg-green-700 dark:text-green-50',
    outline: 'border-green-500 dark:border-green-500',
  },
  {
    filled: 'bg-blue-300 text-blue-950 dark:bg-blue-700 dark:text-blue-50',
    outline: 'border-blue-500 dark:border-blue-500',
  },
  {
    filled: 'bg-indigo-300 text-indigo-950 dark:bg-indigo-700 dark:text-indigo-50',
    outline: 'border-indigo-500 dark:border-indigo-500',
  },
  {
    filled: 'bg-violet-300 text-violet-950 dark:bg-violet-700 dark:text-violet-50',
    outline: 'border-violet-500 dark:border-violet-500',
  },
  // Deep.
  {
    filled: 'bg-red-700 text-white dark:bg-red-300 dark:text-red-950',
    outline: 'border-red-700 dark:border-red-300',
  },
  {
    filled: 'bg-orange-700 text-white dark:bg-orange-300 dark:text-orange-950',
    outline: 'border-orange-700 dark:border-orange-300',
  },
  {
    filled: 'bg-yellow-700 text-white dark:bg-yellow-300 dark:text-yellow-950',
    outline: 'border-yellow-700 dark:border-yellow-300',
  },
  {
    filled: 'bg-green-700 text-white dark:bg-green-300 dark:text-green-950',
    outline: 'border-green-700 dark:border-green-300',
  },
  {
    filled: 'bg-blue-700 text-white dark:bg-blue-300 dark:text-blue-950',
    outline: 'border-blue-700 dark:border-blue-300',
  },
  {
    filled: 'bg-indigo-700 text-white dark:bg-indigo-300 dark:text-indigo-950',
    outline: 'border-indigo-700 dark:border-indigo-300',
  },
  {
    filled: 'bg-violet-700 text-white dark:bg-violet-300 dark:text-violet-950',
    outline: 'border-violet-700 dark:border-violet-300',
  },
];

/** A slot outside the palette - a newer server, say - is painted as the first rather than unstyled. */
export function tagColor(slot: number): TagColor {
  return tagColors[slot] ?? tagColors[0];
}

/**
 * A chip's colour classes. Pressed or carried, it is filled with the tag's colour; not pressed, it is
 * the colour's outline around the page's own text, so the state never rests on the colour alone -
 * the pressed one also has a tick (the tag-chips design, 2). Size and shape stay with each surface.
 */
export function tagChip(slot: number, pressed: boolean): string {
  const colour = tagColor(slot);

  return pressed
    ? `border-transparent ${colour.filled}`
    : `${colour.outline} text-gray-900 dark:text-gray-100`;
}

/** The rainbow's order, as the leaves of the `tags.hues` keys. */
export const tagHues = ['red', 'orange', 'yellow', 'green', 'blue', 'indigo', 'violet'] as const;

/** The three rounds, as the leaves of the `tags.shades` keys. */
export const tagShades = ['light', 'middle', 'deep'] as const;

/** A slot's name as two keys, so the picker can say "Blå, lys" rather than a number. */
export function tagColourKeys(slot: number): { hue: string; shade: string } {
  return {
    hue: `tags.hues.${tagHues[slot % tagHues.length]}`,
    shade: `tags.shades.${tagShades[Math.floor(slot / tagHues.length)]}`,
  };
}
