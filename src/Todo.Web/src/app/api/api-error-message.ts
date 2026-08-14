import { TranslocoService } from '@jsverse/transloco';
import { ApiError } from './todo-client';

// A rejected request throws the ApiError body itself, and its code is the translation key.
// A code with no translation yet still says something: the server's own English message.
export function apiErrorMessage(transloco: TranslocoService, error: unknown): string {
  if (!(error instanceof ApiError)) {
    return transloco.translate('errors.generic');
  }

  const key = `errors.${error.code}`;
  const translated = transloco.translate(key);

  return translated === key ? error.message || transloco.translate('errors.generic') : translated;
}
