import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { AiStore } from '../ai/ai-store';
import { TodoTask } from '../api/todo-client';
import { Timestamp } from '../i18n/timestamp';
import { Shortcut } from '../shortcuts/shortcut';
import { ShortcutStore } from '../shortcuts/shortcut-store';
import { SystemStore } from '../system/system-store';
import { TaskRow } from '../tasks/task-row';
import { TaskStore } from '../tasks/task-store';
import { ReviewGroupName, localToday, reviewGroups } from './review-groups';
import { ReviewSection } from './review-section';
import { ReviewStore } from './review-store';

@Component({
  selector: 'app-review',
  imports: [ReviewSection, Shortcut, TaskRow, Timestamp, TranslocoPipe],
  templateUrl: './review.html',
  // The shell's ceiling is raised on xl so the task list can be two columns. This screen is not two
  // columns - it is a walk through five questions, and a list stretched over 1440px is unreadable -
  // so it puts the ceiling back, like the four other non-list screens.
  host: {
    class: 'block xl:max-w-2xl',
  },
})
export class Review {
  protected readonly store = inject(TaskStore);
  protected readonly review = inject(ReviewStore);
  protected readonly ai = inject(AiStore);
  protected readonly shortcuts = inject(ShortcutStore);
  private readonly system = inject(SystemStore);

  /**
   * The five groups, over the store's <em>raw</em> list rather than `matching()`: a search left
   * running on the task list must not narrow a review, and there is no search field here to undo it
   * with. `localToday()` is read once per recomputation, which is often enough - the window is a
   * week, and nobody leaves the screen open across midnight and expects it to notice.
   */
  protected readonly groups = computed(() => reviewGroups(this.store.tasks(), localToday()));

  /**
   * Which group is unfolded. Null - nothing open - is reachable by clicking the open heading, and is
   * what an all-empty review shows.
   *
   * Unlike the settings page, which arrives with nothing open: that page is a reference you go to in
   * order to change one thing, while this one is a sequence, and a click to get started is a step
   * with no purpose. So the first group holding anything opens itself.
   */
  private readonly chosen = signal<ReviewGroupName | null | undefined>(undefined);

  protected readonly open = computed(() => {
    const chosen = this.chosen();

    return chosen === undefined
      ? (this.groups().find((g) => g.tasks.length > 0)?.name ?? null)
      : chosen;
  });

  /** Whether the whole review is empty, so the screen can say so rather than showing five zeroes. */
  protected readonly empty = computed(() => this.groups().every((g) => g.tasks.length === 0));

  protected readonly editingNote = signal<number | null>(null);

  protected readonly expandedId = signal<number | null>(null);

  constructor() {
    // Both, because the screen needs the tasks and the marker needs the state, and neither store
    // loads itself.
    this.store.load().catch(() => {});
    this.review.load().catch(() => {});
  }

  protected headingKey(name: ReviewGroupName): string {
    return `review.groups.${name}`;
  }

  protected isOpen(name: ReviewGroupName): boolean {
    return this.open() === name;
  }

  protected toggleSection(name: ReviewGroupName): void {
    this.chosen.set(this.open() === name ? null : name);
  }

  protected toggle(task: TodoTask): void {
    this.system.clearError();
    this.editingNote.set(null);
    this.expandedId.update((id) => (id === task.id ? null : task.id));
  }

  protected editNote(task: TodoTask): void {
    this.editingNote.set(task.id);
  }

  protected stopEditingNote(): void {
    this.editingNote.set(null);
  }

  protected remove(task: TodoTask): void {
    this.editingNote.set(null);
    this.store.remove(task.id).catch(() => {});
  }

  protected complete(): void {
    this.review.complete().catch(() => {});
  }

  protected writeOverview(): void {
    this.ai.writeReviewOverview().catch(() => {});
  }
}
