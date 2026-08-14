import { marked } from 'marked';

/**
 * Renders markdown without sanitising it. The sanitising happens where the result is bound with
 * `[innerHTML]`, so this is not a security layer.
 */
export function renderMarkdown(source: string | null | undefined): string {
  if (!source?.trim()) {
    return '';
  }

  return marked.parse(source, { async: false, gfm: true, breaks: true });
}
