export function resolveLanguage(stored: string | null, system: string): 'da' | 'en' {
  if (stored === 'da' || stored === 'en') return stored;
  return system.toLowerCase().startsWith('da') ? 'da' : 'en';
}
