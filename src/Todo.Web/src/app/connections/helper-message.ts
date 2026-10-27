/**
 * What the Connections page tells the app around it, and all it ever tells: that it is there, how tall
 * it is, and that something changed so the app should read its settings again. None of them carries a
 * secret - the page is on the helper's origin precisely so the token never crosses to the app's.
 */
export type HelperMessageBody =
  { type: 'ready' } | { type: 'height'; height: number } | { type: 'changed' };

/** Stamped with its sender, so the app can tell it from any other message a window might get. */
export type HelperMessage = HelperMessageBody & { source: typeof HELPER_SOURCE };

export const HELPER_SOURCE = 'todo-helper';

/** Whether a message event's data has the shape above, checked rather than cast. */
export function isHelperMessage(data: unknown): data is HelperMessage {
  if (typeof data !== 'object' || data === null) return false;

  const message = data as Record<string, unknown>;
  if (message['source'] !== HELPER_SOURCE) return false;

  return (
    message['type'] === 'ready' ||
    message['type'] === 'changed' ||
    (message['type'] === 'height' && typeof message['height'] === 'number')
  );
}
