import { Injectable, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { apiErrorMessage } from '../api/api-error-message';
import { ReviewClient, ReviewStatusResponse } from '../api/todo-client';

/**
 * The weekly review's due state, and nothing else.
 *
 * Deliberately not the tasks: those come from `TaskStore`, which `TaskRow` and `TaskDetail` inject
 * directly. A second copy of the list here would go stale the moment anything was saved, and the
 * row would not leave its group — which is the whole payoff of reusing the row. See the design's
 * section 3.
 */
@Injectable({ providedIn: 'root' })
export class ReviewStore {
  private readonly client = inject(ReviewClient);
  private readonly transloco = inject(TranslocoService);

  /**
   * Whether a review is owed. Starts false rather than true, so the marker never flashes on before
   * the first answer has arrived — an app that accused you of being behind for half a second every
   * time it opened would be worse than one that took half a second to say so.
   */
  readonly due = signal(false);

  readonly lastCompletedAt = signal<string | null>(null);

  readonly nextDueAt = signal<string | null>(null);

  readonly error = signal<string | null>(null);

  async load(): Promise<void> {
    try {
      this.read(await firstValueFrom(this.client.getReviewStatus()));
    } catch {
      // The marker is an affordance, not the app. A review state that cannot be read leaves it off
      // rather than putting an error where a nav link should be.
    }
  }

  /** Stamps the review as finished. The reply is authoritative, so nothing is guessed locally. */
  async complete(): Promise<void> {
    this.error.set(null);
    try {
      this.read(await firstValueFrom(this.client.completeReview()));
    } catch (error) {
      this.error.set(apiErrorMessage(this.transloco, error));
    }
  }

  /**
   * Both timestamps stay strings, because the generator emits them as strings and nothing here
   * needs them as dates: the server did the arithmetic, and the screen only formats them.
   */
  private read(status: ReviewStatusResponse): void {
    this.due.set(status.due);
    this.lastCompletedAt.set(status.lastCompletedAt ?? null);
    this.nextDueAt.set(status.nextDueAt);
  }
}
