import { Component, inject } from '@angular/core';
import { DeadlineBucket } from '../api/todo-client';
import { TaskStore } from './task-store';

const bucketLabels: Record<DeadlineBucket, string> = {
  [DeadlineBucket.Overdue]: 'Overskredet',
  [DeadlineBucket.Today]: 'I dag',
  [DeadlineBucket.ThisWeek]: 'Denne uge',
  [DeadlineBucket.Later]: 'Senere',
  [DeadlineBucket.NoDeadline]: 'Uden deadline',
};

@Component({
  selector: 'app-task-list',
  templateUrl: './task-list.html',
})
export class TaskList {
  protected readonly store = inject(TaskStore);
  protected readonly overdue = DeadlineBucket.Overdue;

  constructor() {
    // A failed load needs no message of its own: the health line already reports the API down.
    this.store.load().catch(() => {});
  }

  protected label(bucket: DeadlineBucket): string {
    return bucketLabels[bucket];
  }
}
