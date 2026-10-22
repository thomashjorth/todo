import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AiStore } from '../ai/ai-store';
import { API_BASE_URL, DeadlineBucket, TagRef, TodoStatus, TodoTask } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { TaskStore } from '../tasks/task-store';
import { TagStore } from './tag-store';

const reload = '/api/tasks?includeCompleted=false&includeSomeday=true';

const privat = new TagRef({ id: 1, name: 'Privat' });
const indkob = new TagRef({ id: 2, name: 'Indkøb' });

function configure(): HttpTestingController {
  TestBed.configureTestingModule({
    imports: [translocoTesting()],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: API_BASE_URL, useValue: '' },
    ],
  });

  return TestBed.inject(HttpTestingController);
}

function json(body: unknown): Blob {
  return new Blob([JSON.stringify(body)]);
}

function task(id: number, title: string, tags: TagRef[] = []): TodoTask {
  return new TodoTask({
    id,
    sourceId: 'manual',
    title,
    status: TodoStatus.Open,
    bucket: DeadlineBucket.NoDeadline,
    createdAt: '2026-10-22T10:00:00Z',
    subTasks: [],
    tags,
  });
}

describe('Tags', () => {
  it('should reload the list after a tag is added, in the server order', async () => {
    const http = configure();
    const store = TestBed.inject(TagStore);

    const adding = store.add('Privat');
    const post = http.expectOne('/api/tags');
    expect(JSON.parse(post.request.body).name).toBe('Privat');
    post.flush(json(privat.toJSON()), { status: 201, statusText: 'Created' });
    (await vi.waitFor(() => http.expectOne('/api/tags'))).flush(
      json({ items: [indkob.toJSON(), privat.toJSON()] }),
    );

    expect(await adding).toBe(true);
    expect(store.tags().map((t) => t.name)).toEqual(['Indkøb', 'Privat']);
  });

  it('should say why a name was refused and keep the field for it', async () => {
    const http = configure();
    const store = TestBed.inject(TagStore);

    const adding = store.add('kunde');
    http.expectOne('/api/tags').flush(json({ code: 'tags.duplicate', message: 'x' }), {
      status: 400,
      statusText: 'Bad Request',
    });

    expect(await adding).toBe(false);
    expect(store.error()).toBe('Der findes allerede et tag med det navn.');
  });

  // AND, like the search: each pressed tag takes tasks away. The task carrying both is seeded last,
  // so a filter that kept the first match only would fail.
  it('should keep only the tasks that carry every pressed tag', () => {
    configure();
    const tasks = TestBed.inject(TaskStore);
    tasks.tasks.set([
      task(1, 'Ring til tandlægen', [privat]),
      task(2, 'Køb kaffe til kontoret', [indkob]),
      task(3, 'Køb mælk', [indkob, privat]),
    ]);
    const shown = () => tasks.sections().flatMap((s) => s.tasks.map((t) => t.id));

    tasks.toggleTagFilter(privat.id);
    expect(shown()).toEqual([1, 3]);

    tasks.toggleTagFilter(indkob.id);
    expect(shown()).toEqual([3]);

    tasks.toggleTagFilter(privat.id);
    tasks.toggleTagFilter(indkob.id);
    expect(shown()).toEqual([1, 2, 3]);
  });

  it('should not ask for tags while the model is not ready or no tags exist', async () => {
    const http = configure();
    const ai = TestBed.inject(AiStore);

    await ai.suggestTags(task(5, 'Køb mælk'));
    ai.ready.set(true);
    await ai.suggestTags(task(5, 'Køb mælk'));

    http.expectNone('/api/ai/tasks/5/tag-suggestions');
  });

  it('should suggest tags by name and add them to what the task carries by then', async () => {
    const http = configure();
    const ai = TestBed.inject(AiStore);
    const tasks = TestBed.inject(TaskStore);
    ai.ready.set(true);
    TestBed.inject(TagStore).tags.set([indkob, privat]);

    const suggesting = ai.suggestTags(task(5, 'Køb mælk'));
    http.expectOne('/api/ai/tasks/5/tag-suggestions').flush(json({ tagIds: [indkob.id] }));
    await suggesting;

    expect(ai.tagSuggestion()).toEqual({ taskId: 5, title: 'Køb mælk', tagIds: [indkob.id] });
    expect(ai.tagSuggestionNames()).toEqual(['Indkøb']);

    // Ticked in the panel meanwhile: accepting adds to it rather than replacing it.
    tasks.tasks.set([task(5, 'Køb mælk', [privat])]);
    const accepting = ai.acceptTags();
    const put = http.expectOne('/api/tasks/5/tags');
    expect(JSON.parse(put.request.body).tagIds).toEqual([privat.id, indkob.id]);
    put.flush(null, { status: 204, statusText: 'No Content' });
    (await vi.waitFor(() => http.expectOne(reload))).flush(json({ items: [] }));
    await accepting;

    expect(ai.tagSuggestion()).toBeNull();
  });

  it('should show nothing for an empty answer or a failed one', async () => {
    const http = configure();
    const ai = TestBed.inject(AiStore);
    ai.ready.set(true);
    TestBed.inject(TagStore).tags.set([privat]);

    const empty = ai.suggestTags(task(5, 'Læs en bog'));
    http.expectOne('/api/ai/tasks/5/tag-suggestions').flush(json({ tagIds: [] }));
    await empty;
    expect(ai.tagSuggestion()).toBeNull();

    const failing = ai.suggestTags(task(6, 'Læs en bog'));
    http
      .expectOne('/api/ai/tasks/6/tag-suggestions')
      .flush(json({ code: 'ai.unavailable', message: 'x' }), {
        status: 400,
        statusText: 'Bad Request',
      });
    await expect(failing).resolves.toBeUndefined();
    expect(ai.tagSuggestion()).toBeNull();
  });
});
