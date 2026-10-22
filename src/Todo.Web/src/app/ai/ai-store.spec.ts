import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL, DeadlineBucket, TodoStatus, TodoTask } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { AiStore } from './ai-store';

const reload = '/api/tasks?includeCompleted=false&includeSomeday=true';

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

// The generated client asks for a blob and decodes it with FileReader, so every flushed body is one.
function json(body: unknown): Blob {
  return new Blob([JSON.stringify(body)]);
}

function refusal(code: string): [Blob, { status: number; statusText: string }] {
  return [json({ code, message: 'refused' }), { status: 400, statusText: 'Bad Request' }];
}

const imported = new TodoTask({
  id: 7,
  sourceId: 'jira',
  title: 'CSV-eksport fejler',
  note: 'En lang beskrivelse.',
  externalUrl: 'https://jira.example.com/browse/SAAS-1',
  status: TodoStatus.Open,
  bucket: DeadlineBucket.NoDeadline,
  createdAt: '2026-10-22T10:00:00Z',
  subTasks: [],
});

describe('AiStore', () => {
  it('should start not ready, so nothing flashes on before the answer', () => {
    expect(configure().store.ready()).toBe(false);
  });

  it('should be ready when the host says so', async () => {
    const { store, http } = configure();

    const loading = store.load();
    http.expectOne('/api/ai/status').flush(json({ ready: true }));
    await loading;

    expect(store.ready()).toBe(true);
  });

  it('should stay not ready when the host says not ready', async () => {
    const { store, http } = configure();

    const loading = store.load();
    http.expectOne('/api/ai/status').flush(json({ ready: false }));
    await loading;

    expect(store.ready()).toBe(false);
  });

  it('should read a failed status call as not ready rather than as an error', async () => {
    const { store, http } = configure();

    const loading = store.load();
    http.expectOne('/api/ai/status').flush(json({}), { status: 500, statusText: 'Server Error' });

    await expect(loading).resolves.toBeUndefined();
    expect(store.ready()).toBe(false);
  });

  it('should hold the suggested task until it is accepted, and then create it with every field', async () => {
    const { store, http } = configure();

    const parsing = store.parse('Ring til Anna om fakturaen på fredag');
    expect(store.parsing()).toBe(true);
    const parsed = http.expectOne('/api/ai/parse-task');
    expect(JSON.parse(parsed.request.body).text).toBe('Ring til Anna om fakturaen på fredag');
    parsed.flush(
      json({ title: 'Ring til Anna om fakturaen', requester: 'Anna', deadline: '2026-10-23' }),
    );
    await parsing;

    expect(store.parsing()).toBe(false);
    expect(store.taskSuggestion()?.title).toBe('Ring til Anna om fakturaen');
    http.expectNone('/api/tasks');

    const accepting = store.acceptTask();
    const created = http.expectOne('/api/tasks');
    expect(JSON.parse(created.request.body)).toEqual({
      title: 'Ring til Anna om fakturaen',
      requester: 'Anna',
      deadline: '2026-10-23',
    });
    created.flush(json({}), { status: 201, statusText: 'Created' });
    (await vi.waitFor(() => http.expectOne(reload))).flush(json({ items: [] }));
    await accepting;

    expect(store.taskSuggestion()).toBeNull();
  });

  it('should show why the model could not be asked', async () => {
    const { store, http } = configure();

    const parsing = store.parse('Køb mælk');
    http.expectOne('/api/ai/parse-task').flush(...refusal('ai.unavailable'));
    await parsing;

    expect(store.parseError()).toBe('Den lokale AI-model svarer ikke. Kontrollér at Ollama kører.');
    expect(store.taskSuggestion()).toBeNull();
  });

  it('should tick every suggested subtask and add only the ones still ticked, in order', async () => {
    const { store, http } = configure();

    const splitting = store.split(7);
    http
      .expectOne('/api/ai/tasks/7/split')
      .flush(json({ titles: ['Find ejeren', 'Bestil certifikat', 'Genstart gatewayen'] }));
    await splitting;

    expect(store.subTaskSuggestions()?.value.every((c) => c.chosen)).toBe(true);
    store.choose(1, false);

    const accepting = store.acceptSubTasks();
    const first = await vi.waitFor(() => http.expectOne('/api/tasks/7/subtasks'));
    expect(JSON.parse(first.request.body).title).toBe('Find ejeren');
    first.flush(json({ id: 1, title: 'Find ejeren', isDone: false }), {
      status: 201,
      statusText: 'Created',
    });
    const second = await vi.waitFor(() => http.expectOne('/api/tasks/7/subtasks'));
    expect(JSON.parse(second.request.body).title).toBe('Genstart gatewayen');
    second.flush(json({ id: 2, title: 'Genstart gatewayen', isDone: false }), {
      status: 201,
      statusText: 'Created',
    });
    (await vi.waitFor(() => http.expectOne(reload))).flush(json({ items: [] }));
    await accepting;

    expect(store.subTaskSuggestions()).toBeNull();
  });

  it('should keep a failed split to the task it was asked for', async () => {
    const { store, http } = configure();

    const splitting = store.split(7);
    http.expectOne('/api/ai/tasks/7/split').flush(...refusal('ai.unreadableAnswer'));
    await splitting;

    expect(store.splitError()).toEqual({
      taskId: 7,
      value: 'AI-modellen gav et svar der ikke kunne læses. Prøv igen.',
    });
    expect(store.splitting()).toBeNull();
  });

  it('should replace the note with the summary only when it is accepted', async () => {
    const { store, http } = configure();

    const summarizing = store.summarize(7);
    http
      .expectOne('/api/ai/tasks/7/summary')
      .flush(json({ summary: 'Eksporten fejler over 10.000 rækker.' }));
    await summarizing;

    expect(store.summary()).toEqual({ taskId: 7, value: 'Eksporten fejler over 10.000 rækker.' });
    http.expectNone('/api/tasks/7');

    const accepting = store.acceptSummary(imported);
    const saved = http.expectOne('/api/tasks/7');
    expect(saved.request.method).toBe('PUT');
    expect(JSON.parse(saved.request.body).note).toBe('Eksporten fejler over 10.000 rækker.');
    saved.flush(json(imported.toJSON()));
    (await vi.waitFor(() => http.expectOne(reload))).flush(json({ items: [] }));
    await accepting;

    expect(store.summary()).toBeNull();
  });

  it('should not put one task summary on another', async () => {
    const { store, http } = configure();

    const summarizing = store.summarize(8);
    http.expectOne('/api/ai/tasks/8/summary').flush(json({ summary: 'En anden opgave.' }));
    await summarizing;

    await store.acceptSummary(imported);

    http.expectNone('/api/tasks/7');
    expect(store.summary()?.taskId).toBe(8);
  });
});
