import { Pipe, PipeTransform, inject } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { formatDeadline } from './format-deadline';

/**
 * Impure on purpose: the language changes while the deadline stays the same, and a pure pipe
 * would keep showing the format it was first asked for.
 */
@Pipe({ name: 'deadlineDate', pure: false })
export class DeadlineDate implements PipeTransform {
  private readonly transloco = inject(TranslocoService);

  transform(value: string | null | undefined): string {
    return formatDeadline(value, this.transloco.getActiveLang());
  }
}
