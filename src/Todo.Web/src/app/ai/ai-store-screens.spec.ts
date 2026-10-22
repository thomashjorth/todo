import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import {
  API_BASE_URL,
  DeadlineBucket,
  RetroPreviewRow,
  TodoStatus,
  TodoTask,
} from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { TaskStore } from '../tasks/task-store';
import { AiStore } from './ai-store';

// The AI on the screens other than the panel: the retro import, the search field, the import
// previews and the weekly review. Kept apart from ai-store.spec.ts so neither has to be read whole.

function configure(): { store: AiStore; http: HttpTestingController } {
  TestBed.configureTestingModule({
    imports: [translocoTesting()],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: API_BASE_URL, useValue: '' },
    ],
  });

  return { store: TestBed.inject(AiStore), http: TestBed.inject(HttpTestingController) };
}

function json(body: unknown): Blob {
  return new Blob([JSON.stringify(body)]);
}

function row(key: string, title: string, owner?: string): RetroPreviewRow {
  return new RetroPreviewRow({
    key,
    title,
    owner,
    zone: 'Went badly',
    isMine: true,
    alreadyImported: false,
  });
}

function task(id: number, title: string): TodoTask {
  return new TodoTask({
    id,
    sourceId: 'manual',
    title,
    status: TodoStatus.Open,
    bucket: DeadlineBucket.NoDeadline,
    createdAt: '2026-10-22T10:00:00Z',
    subTasks: [],
  });
}

describe('AiStore on the other screens', () => {
  it('should read search readiness apart from the model being ready', async () => {
    const { store, http } = configure();

    const loading = store.load();
    http.expectOne('/api/ai/status').flush(json({ ready: true, searchReady: false }));
    await loading;

    expect(store.ready()).toBe(true);
    expect(store.searchReady()).toBe(false);
  });

  it('should send the rows as notes and use every suggestion until switched back', async () => {
    const { store, http } = configure();

    const asking = store.suggestRetroActions([
      row('a', 'We should test more'),
      row('b', 'Book', 'Bo'),
    ]);
    const request = http.expectOne('/api/ai/retro-actions');
    expect(JSON.parse(request.request.body).rows).toEqual([
      { key: 'a', text: 'We should test more' },
      { key: 'b', text: 'Book', owner: 'Bo' },
    ]);
    request.flush(
      json({
        rows: [{ key: 'a', title: 'Write more tests', owner: 'Thomas', ownerIsMine: true }],
      }),
    );
    await asking;

    expect(store.retroActions().get('a')).toEqual({
      title: 'Write more tests',
      owner: 'Thomas',
      ownerIsMine: true,
      used: true,
    });
    expect(store.retroActions().has('b')).toBe(false);

    store.useRetroAction('a', false);

    expect(store.retroActions().get('a')?.used).toBe(false);
  });

  // The key is the original's, so deduplication does not move; the board's owner wins over a guess.
  it('should import a row with the suggestion in use under its own key', () => {
    const { store } = configure();
    store.retroActions.set(
      new Map([
        ['a', { title: 'Write more tests', owner: 'Thomas', ownerIsMine: true, used: true }],
        ['b', { title: 'Book the room', owner: 'Thomas', ownerIsMine: true, used: true }],
        ['c', { title: 'Not used', owner: null, ownerIsMine: false, used: false }],
      ]),
    );

    const a = store.asImported(row('a', 'We should test more'));
    const b = store.asImported(row('b', 'Book', 'Bo'));
    const c = store.asImported(row('c', 'Keep me'));

    expect([a.key, a.title, a.owner]).toEqual(['a', 'Write more tests', 'Thomas']);
    expect([b.title, b.owner]).toEqual(['Book the room', 'Bo']);
    expect(c.title).toBe('Keep me');
  });

  it('should hand the meaning hits to the task list for the query they were found for', async () => {
    const { store, http } = configure();
    const tasks = TestBed.inject(TaskStore);

    const searching = store.searchByMeaning('  Certifikat ');
    expect(store.searchingByMeaning()).toBe(true);
    const request = http.expectOne('/api/ai/search');
    expect(JSON.parse(request.request.body).query).toBe('Certifikat');
    request.flush(json({ taskIds: [3, 1] }));
    await searching;

    expect(tasks.meaning()).toEqual({ query: 'certifikat', ids: new Set([3, 1]) });
    expect(store.searchingByMeaning()).toBe(false);
  });

  it('should add the tasks found by meaning to the ones the words match, while the words stand', () => {
    configure();
    const tasks = TestBed.inject(TaskStore);
    tasks.tasks.set([
      task(1, 'Forny TLS på testserveren'),
      task(2, 'Køb mælk'),
      task(3, 'Certifikat'),
    ]);

    tasks.query.set('certifikat');
    tasks.meaning.set({ query: 'certifikat', ids: new Set([1]) });

    expect(tasks.sections().flatMap((s) => s.tasks.map((t) => t.id))).toEqual([1, 3]);
    expect(tasks.searchingByMeaning()).toBe(true);

    tasks.query.set('certifikatet');

    expect(tasks.sections().flatMap((s) => s.tasks.map((t) => t.id))).toEqual([]);
    expect(tasks.searchingByMeaning()).toBe(false);
  });

  it('should not look for duplicates when search is not ready', async () => {
    const { store, http } = configure();

    await store.findDuplicates('jira', [{ key: 'SAAS-1', title: 'Login' }]);

    http.expectNone('/api/ai/duplicates');
  });

  it('should keep the duplicates per source and key', async () => {
    const { store, http } = configure();
    store.searchReady.set(true);

    const finding = store.findDuplicates('jira', [{ key: 'SAAS-1', title: 'Login', note: null }]);
    const request = http.expectOne('/api/ai/duplicates');
    expect(JSON.parse(request.request.body)).toEqual({
      sourceId: 'jira',
      rows: [{ key: 'SAAS-1', title: 'Login' }],
    });
    request.flush(json({ matches: [{ key: 'SAAS-1', taskId: 7, taskTitle: 'Ret login' }] }));
    await finding;

    expect(store.duplicateOf('jira', 'SAAS-1')).toEqual({ taskId: 7, taskTitle: 'Ret login' });
    expect(store.duplicateOf('ado', 'SAAS-1')).toBeUndefined();
  });

  // A warning that could not be computed is no warning: the import must work as before.
  it('should show no duplicates and no error when the check fails', async () => {
    const { store, http } = configure();
    store.searchReady.set(true);

    const finding = store.findDuplicates('jira', [{ key: 'SAAS-1', title: 'Login' }]);
    http
      .expectOne('/api/ai/duplicates')
      .flush(json({ code: 'ai.unavailable', message: 'x' }), {
        status: 400,
        statusText: 'Bad Request',
      });

    await expect(finding).resolves.toBeUndefined();
    expect(store.duplicateOf('jira', 'SAAS-1')).toBeUndefined();
  });

  it('should hold the review points and say why when they could not be written', async () => {
    const { store, http } = configure();

    const writing = store.writeReviewOverview();
    expect(store.writingReview()).toBe(true);
    http.expectOne('/api/ai/review-overview').flush(json({ points: ['Et', 'To'] }));
    await writing;

    expect(store.reviewPoints()).toEqual(['Et', 'To']);
    expect(store.writingReview()).toBe(false);

    const failing = store.writeReviewOverview();
    http
      .expectOne('/api/ai/review-overview')
      .flush(json({ code: 'ai.unavailable', message: 'x' }), {
        status: 400,
        statusText: 'Bad Request',
      });
    await failing;

    expect(store.reviewError()).toBe(
      'Den lokale AI-model svarer ikke. Kontrollér at Ollama kører.',
    );
  });
});
