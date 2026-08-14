// Danish and English share the same one/other split, so two keys per phrase beat an ICU dependency.
export function pluralKey(count: number, base: string): string {
  return `${base}.${count === 1 ? 'one' : 'other'}`;
}
