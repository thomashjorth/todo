import { Pipe, PipeTransform, inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { formatTimestamp } from './format-timestamp';

/**
 * Impure on purpose, for the reason `DeadlineDate` next door gives: the language changes while the
 * moment stays the same, and a pure pipe would keep showing the format it was first asked for. The
 * active language is not a signal, so a `computed` would cache the Danish sentence - see
 * `AdoStore.reconciliationMessage` for the same trap answered the same way.
 */
@Pipe({ name: 'timestamp', pure: false })
export class Timestamp implements PipeTransform {
  private readonly transloco = inject(TranslocoService);

  transform(value: string | null | undefined): string {
    return formatTimestamp(value, this.transloco.getActiveLang());
  }
}
