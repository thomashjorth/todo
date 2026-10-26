import { tagChip, tagColor, tagColourKeys, tagColors } from './tag-colors';

describe('Tag colours', () => {
  // TagColors.Count on the server: a slot it gives must have a colour here.
  it('should have a colour for each of the 21 slots, no two alike', () => {
    expect(tagColors.length).toBe(21);
    expect(new Set(tagColors.map((c) => c.filled)).size).toBe(21);
  });

  // The rainbow first: the user's first seven tags are red, orange, yellow, green, blue, indigo,
  // violet, and the shades come after.
  it('should go through the rainbow before the shades', () => {
    const hues = tagColors.slice(0, 7).map((c) => /bg-(\w+)-/.exec(c.filled)![1]);

    expect(hues).toEqual(['red', 'orange', 'yellow', 'green', 'blue', 'indigo', 'violet']);
    expect(tagColors[7].filled).toContain('bg-red-300');
  });

  // The name must follow the palette: slot 11 is the middle round's blue, so a name read from the
  // wrong end of either list would say another colour than the one painted.
  it('should name a slot by its hue and its shade', () => {
    expect(tagColourKeys(11)).toEqual({ hue: 'tags.hues.blue', shade: 'tags.shades.middle' });
    expect(tagColors[11].filled).toContain('bg-blue-300');
  });

  it('should paint a slot outside the palette as the first', () => {
    expect(tagColor(99)).toBe(tagColors[0]);
  });

  it('should fill a pressed chip and outline one that is not', () => {
    expect(tagChip(1, true)).toContain('bg-orange-100');
    expect(tagChip(1, false)).not.toContain('bg-');
    expect(tagChip(1, false)).toContain('border-orange-400');
  });
});
