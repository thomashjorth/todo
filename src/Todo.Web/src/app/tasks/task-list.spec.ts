import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { API_BASE_URL } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { SettingsStore } from '../settings/settings-store';
import { TaskList } from './task-list';

const items = [
  {
    id: 1,
    sourceId: 'manual',
    title: 'Betal regningen',
    deadline: '2026-08-10',
    requester: 'Anna',
    status: 'open',
    bucket: 'overdue',
    completedAt: null,
    createdAt: '2026-08-13T18:25:56.60+00:00',
    subTasks: [],
  },
  {
    id: 2,
    sourceId: 'manual',
    title: 'Ring til tandlægen',
    deadline: null,
    requester: null,
    status: 'open',
    bucket: 'noDeadline',
    completedAt: null,
    createdAt: '2026-08-13T18:25:56.60+00:00',
    subTasks: [],
  },
];

const waiting = {
  id: 3,
  sourceId: 'manual',
  title: 'Spørg Bo om tallene',
  deadline: null,
  requester: null,
  status: 'waitingFor',
  bucket: 'noDeadline',
  waitingOn: 'Bo',
  waitingSince: '2026-08-14T13:35:15+00:00',
  waitingDays: 0,
  completedAt: null,
  createdAt: '2026-08-13T18:25:56.60+00:00',
  subTasks: [],
};

// The people the user hands tasks to. Seeded straight into the signal rather than flushed from
// /api/settings: measured, nothing on this screen calls SettingsStore.start(), so no settings
// request is ever made here and an expectOne for one would fail.
const delegates = ['Bo Jensen', 'Camilla Vind'];

// items[0] after the server answered the move: the who field is rendered from the reloaded task,
// and waitingSince is the server's to set.
const handedOver = [
  {
    ...items[0],
    status: 'waitingFor',
    waitingOn: null,
    waitingSince: '2026-08-20T09:00:00+00:00',
    waitingDays: 0,
  },
  items[1],
];

const parked = {
  ...waiting,
  id: 4,
  title: 'Lær at spille harmonika',
  status: 'someday',
  waitingOn: null,
  waitingSince: null,
  waitingDays: null,
};

// No deadline at all: overdue beats deferred, so a past deadline would land it in another section.
const deferredItems = [
  {
    ...items[1],
    id: 5,
    title: 'Bestil sommerhus',
    deferUntil: '2026-09-01',
    bucket: 'deferred',
  },
];

// Tilladt med vilje, og derfor et hint frem for en fejl: opgaven bliver stående i Overskredet,
// fordi Overskredet slår Udskudt, så startdatoen gør ingenting.
const conflicting = [{ ...items[0], deferUntil: '2026-08-20' }, items[1]];

// Grænsen: dagen opgaven begynder er også dagen den skal være færdig, og det er stramt frem for
// modstridende. Det er præcis den sag en `>=` ville melde forkert.
const startingOnTheDeadline = [{ ...items[0], deferUntil: items[0].deadline }, items[1]];

const withSubTasks = [
  {
    ...items[0],
    subTasks: [
      { id: 11, title: 'Find kontonummeret', isDone: true },
      { id: 12, title: 'Overfør beløbet', isDone: false },
    ],
  },
  items[1],
];

const note = '**fed** tekst\n\n- et punkt\n\n<script>alert(1)</script>';

const withNote = [{ ...items[0], note }, items[1]];

const linkNote = 'Se [dokumentationen](https://example.com/docs) først';

const withLink = [{ ...items[0], note: linkNote }, items[1]];

// externalUrl is computed by the server from the source and the key, and only a Jira task has one.
const fromJira = [
  { ...items[0], sourceId: 'jira', externalUrl: 'https://jira.test/browse/SAAS-1' },
  items[1],
];

// The generated client requests responseType 'blob' and decodes it with FileReader,
// so a flushed response only reaches the template after a later microtask.
function rendered(fixture: ComponentFixture<TaskList>): Promise<HTMLElement> {
  return vi.waitFor(() => {
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.querySelectorAll('[data-testid="task-row"]').length).toBe(items.length);
    return element;
  });
}

function shown(fixture: ComponentFixture<TaskList>, selector: string): Promise<HTMLElement> {
  return vi.waitFor(() => {
    fixture.detectChanges();
    const element = (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>(selector);
    expect(element).not.toBeNull();
    return element!;
  });
}

describe('TaskList', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TaskList, translocoTesting()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '' },
      ],
    }).compileComponents();
  });

  it('should render a Danish heading per deadline section', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));

    const element = await rendered(fixture);

    const headings = [...element.querySelectorAll('[data-testid="task-section"] h2')].map((h) =>
      h.textContent?.trim(),
    );
    expect(headings).toEqual(['Overskredet', 'Uden deadline']);
  });

  it('should put a deferred task in its own last section and prefill its start date', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: [items[0], ...deferredItems] })]));

    const element = await rendered(fixture);

    const headings = [...element.querySelectorAll('[data-testid="task-section"] h2')].map((h) =>
      h.textContent?.trim(),
    );
    expect(headings).toEqual(['Overskredet', 'Udskudt']);

    const row = element.querySelectorAll('[data-testid="task-row"]')[1];
    row.querySelector('button')!.click();
    fixture.detectChanges();

    expect(row.querySelector<HTMLInputElement>('[data-testid="defer-until-input"]')!.value).toBe(
      '2026-09-01',
    );
  });

  it('should say in the panel that a start date after the deadline does nothing', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: conflicting })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;

    row.querySelector('button')!.click();
    fixture.detectChanges();

    const hint = row.querySelector('[data-testid="defer-until-conflict"]');
    expect(hint).not.toBeNull();
    expect(hint!.textContent!.trim()).toBe(
      'Startdatoen ligger efter deadline, så opgaven vises som overskredet.',
    );
  });

  it('should not call a start date on the deadline itself a conflict', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: startingOnTheDeadline })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;

    row.querySelector('button')!.click();
    fixture.detectChanges();

    // Panelet er åbent, så fraværet er hintets og ikke hele panelets.
    expect(row.querySelector('[data-testid="task-detail"]')).not.toBeNull();
    expect(row.querySelector('[data-testid="defer-until-conflict"]')).toBeNull();
  });

  it('should show the deadline written out in the active language', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));

    const rows = (await rendered(fixture)).querySelectorAll('[data-testid="task-row"]');

    expect(rows[0].textContent).toContain('Betal regningen');
    expect(rows[0].textContent).toContain('Deadline: 10. aug. 2026');
    expect(rows[0].textContent).toContain('Anna');
    expect(rows[1].textContent).not.toContain('Deadline');
  });

  // The language changes while the row does not, so a pure date pipe would keep the old format.
  it('should rewrite the deadline when the language changes under it', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));
    const element = await rendered(fixture);

    TestBed.inject(TranslocoService).setActiveLang('en');

    await vi.waitFor(() => {
      fixture.detectChanges();
      const row = element.querySelector('[data-testid="task-row"]')!;
      expect(row.textContent).toContain('Deadline: Aug 10, 2026');
    });
  });

  it('should create a task on Enter and clear the input', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));
    const element = await rendered(fixture);

    const input = element.querySelector<HTMLInputElement>('[data-testid="new-task-input"]')!;
    input.value = 'Vand blomsterne';
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));

    const created = await vi.waitFor(() => http.expectOne('/api/tasks'));
    expect(JSON.parse(created.request.body).title).toBe('Vand blomsterne');
    expect(input.value).toBe('');
  });

  it('should expand one row at a time and prefill it with the task', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));
    const element = await rendered(fixture);
    const rows = element.querySelectorAll('[data-testid="task-row"]');

    rows[0].querySelector('button')!.click();
    fixture.detectChanges();

    const detail = rows[0].querySelector('[data-testid="task-detail"]')!;
    expect(detail.querySelector<HTMLInputElement>('input[type="date"]')!.value).toBe('2026-08-10');
    expect(detail.querySelector<HTMLSelectElement>('select')!.value).toBe('open');
    expect([...detail.querySelectorAll('option')].map((o) => o.textContent!.trim())).toEqual([
      'Åben',
      'I gang',
      'Venter på',
      'Måske',
      'Færdig',
    ]);
    expect(rows[1].querySelector('[data-testid="task-detail"]')).toBeNull();

    rows[1].querySelector('button')!.click();
    fixture.detectChanges();

    expect(rows[0].querySelector('[data-testid="task-detail"]')).toBeNull();
    expect(rows[1].querySelector('[data-testid="task-detail"]')).not.toBeNull();
  });

  it('should delete the task without asking first', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));
    const element = await rendered(fixture);
    const row = element.querySelector('[data-testid="task-row"]')!;

    row.querySelector('button')!.click();
    fixture.detectChanges();
    row.querySelector<HTMLButtonElement>('[data-testid="delete-task"]')!.click();

    const request = http.expectOne(`/api/tasks/${items[0].id}`);
    expect(request.request.method).toBe('DELETE');
  });

  it('should complete a task from the row checkbox without expanding it', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));
    const element = await rendered(fixture);
    const row = element.querySelector('[data-testid="task-row"]')!;

    const checkbox = row.querySelector<HTMLInputElement>('[data-testid="complete-toggle"]')!;
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();

    const request = http.expectOne(`/api/tasks/${items[0].id}`);
    expect(JSON.parse(request.request.body).status).toBe('done');
    expect(row.querySelector('[data-testid="task-detail"]')).toBeNull();
  });

  it('should show completed tasks struck through in their own section', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));
    const element = await rendered(fixture);

    element.querySelector<HTMLInputElement>('[data-testid="show-completed"]')!.click();
    fixture.detectChanges();
    http
      .expectOne('/api/tasks?includeCompleted=true&includeSomeday=false')
      .flush(
        new Blob([JSON.stringify({ items: [...items, { ...items[0], id: 9, status: 'done' }] })]),
      );

    const completed = await vi.waitFor(() => {
      fixture.detectChanges();
      const section = element.querySelector('[data-testid="completed-section"]');
      expect(section).not.toBeNull();
      return section!;
    });

    const rows = completed.querySelectorAll('[data-testid="task-row"]');
    expect(rows).toHaveLength(1);
    expect(rows[0].querySelector('span')!.className).toContain('line-through');
    expect(
      rows[0].querySelector<HTMLInputElement>('[data-testid="complete-toggle"]')!.checked,
    ).toBe(true);
    expect(
      element.querySelectorAll('[data-testid="task-section"] [data-testid="task-row"]'),
    ).toHaveLength(2);
  });

  it('should show the ticked-off count on the row that has subtasks', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: withSubTasks })]));

    const rows = (await rendered(fixture)).querySelectorAll('[data-testid="task-row"]');

    expect(rows[0].querySelector('[data-testid="subtask-progress"]')!.textContent!.trim()).toBe(
      '1/2',
    );
    expect(rows[1].querySelector('[data-testid="subtask-progress"]')).toBeNull();
  });

  it('should list the subtasks of the expanded row', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: withSubTasks })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;

    row.querySelector('button')!.click();
    fixture.detectChanges();

    const subTaskRows = row.querySelectorAll('[data-testid="subtask-row"]');
    expect([...subTaskRows].map((r) => r.querySelector('span')!.textContent)).toEqual([
      'Find kontonummeret',
      'Overfør beløbet',
    ]);
    expect(subTaskRows[0].querySelector('span')!.className).toContain('line-through');
    expect(subTaskRows[0].querySelector<HTMLInputElement>('input')!.checked).toBe(true);
    expect(subTaskRows[1].querySelector<HTMLInputElement>('input')!.checked).toBe(false);
  });

  it('should add a subtask on Enter and clear the input', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: withSubTasks })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;
    row.querySelector('button')!.click();
    fixture.detectChanges();

    const input = row.querySelector<HTMLInputElement>('[data-testid="new-subtask-input"]')!;
    input.value = 'Gem kvitteringen';
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));

    const created = await vi.waitFor(() => http.expectOne(`/api/tasks/${items[0].id}/subtasks`));
    expect(JSON.parse(created.request.body).title).toBe('Gem kvitteringen');
    expect(input.value).toBe('');
  });

  it('should tick a subtask off without collapsing the row', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: withSubTasks })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;
    row.querySelector('button')!.click();
    fixture.detectChanges();

    const checkbox = row.querySelectorAll('[data-testid="subtask-row"]')[1].querySelector('input')!;
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();

    const request = http.expectOne(`/api/tasks/${items[0].id}/subtasks/12`);
    expect(JSON.parse(request.request.body)).toEqual({ title: 'Overfør beløbet', isDone: true });
    expect(row.querySelector('[data-testid="task-detail"]')).not.toBeNull();
  });

  it('should delete a subtask from its own row', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: withSubTasks })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;
    row.querySelector('button')!.click();
    fixture.detectChanges();

    row.querySelector<HTMLButtonElement>('[data-testid="delete-subtask"]')!.click();

    const request = http.expectOne(`/api/tasks/${items[0].id}/subtasks/11`);
    expect(request.request.method).toBe('DELETE');
  });

  it('should show the note as rendered markdown rather than as its source', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: withNote })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;

    row.querySelector('button')!.click();
    fixture.detectChanges();

    const view = row.querySelector('[data-testid="note-rendered"]')!;
    expect(view.querySelector('strong')!.textContent).toBe('fed');
    expect(view.querySelector('li')!.textContent).toBe('et punkt');
    expect(view.querySelector('script')).toBeNull();
    expect(row.querySelector('[data-testid="note-editor"]')).toBeNull();
  });

  it('should open the editor on the raw markdown when the rendered note is clicked', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: withNote })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;
    row.querySelector('button')!.click();
    fixture.detectChanges();

    row.querySelector<HTMLElement>('[data-testid="note-rendered"]')!.click();
    fixture.detectChanges();

    const editor = row.querySelector<HTMLTextAreaElement>('[data-testid="note-editor"]')!;
    expect(editor.value).toBe(note);
    expect(row.querySelector('[data-testid="note-rendered"]')).toBeNull();
    expect(document.activeElement).toBe(editor);
  });

  it('should open the editor from the button as well, for anyone who cannot click', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: withNote })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;
    row.querySelector('button')!.click();
    fixture.detectChanges();

    const edit = row.querySelector<HTMLButtonElement>('[data-testid="note-edit"]')!;
    expect(edit.textContent!.trim()).toBe('Redigér noten');
    edit.click();
    fixture.detectChanges();

    expect(row.querySelector<HTMLTextAreaElement>('[data-testid="note-editor"]')!.value).toBe(note);
    expect(row.querySelector('[data-testid="note-edit"]')).toBeNull();
  });

  // Following the link in place would replace the app with a website, in a window with no way back.
  it('should send a link in the note to the system browser rather than open the editor', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: withLink })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;
    row.querySelector('button')!.click();
    fixture.detectChanges();

    const click = new MouseEvent('click', { bubbles: true, cancelable: true });
    row.querySelector<HTMLAnchorElement>('[data-testid="note-rendered"] a')!.dispatchEvent(click);
    fixture.detectChanges();

    expect(click.defaultPrevented).toBe(true);
    expect(JSON.parse(http.expectOne('/api/system/open-link').request.body)).toEqual({
      url: 'https://example.com/docs',
    });
    expect(row.querySelector('[data-testid="note-editor"]')).toBeNull();
    expect(row.querySelector('[data-testid="note-rendered"]')).not.toBeNull();
  });

  it('should say beside the note when a link could not be opened', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: withLink })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;
    row.querySelector('button')!.click();
    fixture.detectChanges();

    row
      .querySelector<HTMLAnchorElement>('[data-testid="note-rendered"] a')!
      .dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));
    http
      .expectOne('/api/system/open-link')
      .flush(new Blob([JSON.stringify({ code: 'system.unsupportedScheme', message: 'nope' })]), {
        status: 400,
        statusText: 'Bad Request',
      });

    const error = await vi.waitFor(() => {
      fixture.detectChanges();
      const element = row.querySelector('[data-testid="note-link-error"]');
      expect(element).not.toBeNull();
      return element!;
    });

    expect(error.textContent!.trim()).toBe('Kun http- og https-links kan åbnes.');
  });

  it('should open the issue through the system, from a button and outside the row button', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: fromJira })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;

    const link = row.querySelector<HTMLElement>('[data-testid="external-link"]')!;
    // An <a href> would take the whole window with it, and this window has no way back.
    expect(link.tagName).toBe('BUTTON');
    expect(link.textContent!.trim()).toBe('Åbn sagen');

    // Outside the row button: its label would otherwise join the row's accessible name, which
    // TaskListScreen.RowTitled matches in full — and the failure would read as a missing row.
    expect(row.querySelector('button')!.textContent).not.toContain('Åbn sagen');

    link.click();

    expect(JSON.parse(http.expectOne('/api/system/open-link').request.body)).toEqual({
      url: 'https://jira.test/browse/SAAS-1',
    });
  });

  it('should show no issue link on a task that has none', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;

    expect(row.querySelector('[data-testid="external-link"]')).toBeNull();
  });

  it('should save and close the editor on Escape', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: withNote })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;
    row.querySelector('button')!.click();
    fixture.detectChanges();
    row.querySelector<HTMLElement>('[data-testid="note-rendered"]')!.click();
    fixture.detectChanges();

    const editor = row.querySelector<HTMLTextAreaElement>('[data-testid="note-editor"]')!;
    editor.value = '# En overskrift';
    editor.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }));
    fixture.detectChanges();

    expect(JSON.parse(http.expectOne(`/api/tasks/${items[0].id}`).request.body).note).toBe(
      '# En overskrift',
    );
    expect(row.querySelector('[data-testid="note-editor"]')).toBeNull();
    expect(row.querySelector('[data-testid="note-rendered"]')).not.toBeNull();
  });

  it('should invite a note where there is none instead of leaving the row bare', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;

    row.querySelector('button')!.click();
    fixture.detectChanges();

    const placeholder = row.querySelector('[data-testid="note-rendered"]')!;
    expect(placeholder.textContent!.trim()).toBe('Tilføj en note');
    expect(placeholder.className).toContain('italic');
  });

  it('should list a waiting task on its own with who it waits on and for how long', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: [...items, waiting] })]));

    const section = await shown(fixture, '[data-testid="waiting-section"]');

    expect(section.querySelector('h2')!.textContent!.trim()).toBe('Venter på');
    const row = section.querySelector('[data-testid="task-row"]')!;
    expect(row.textContent).toContain('Spørg Bo om tallene');
    expect(row.textContent).toContain('Venter på: Bo');
    expect(row.querySelector('[data-testid="waiting-days"]')!.textContent!.trim()).toBe('0 dage');
    expect(
      (fixture.nativeElement as HTMLElement).querySelectorAll(
        '[data-testid="task-section"] [data-testid="task-row"]',
      ),
    ).toHaveLength(items.length);
  });

  // The E2E screen matches the row button's accessible name in full, so the waiting line has to
  // stay outside it or every waiting row stops being findable by its title.
  it('should keep the waiting line out of the row button', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: [waiting] })]));

    const section = await shown(fixture, '[data-testid="waiting-section"]');

    expect(section.querySelector('button')!.textContent!.trim()).toBe('Spørg Bo om tallene');
  });

  it('should count a single day of waiting in the singular', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: [{ ...waiting, waitingDays: 1 }] })]));

    const days = await shown(fixture, '[data-testid="waiting-days"]');

    expect(days.textContent!.trim()).toBe('1 dag');
  });

  it('should mark an overdue deadline on a waiting task in red', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(
        new Blob([
          JSON.stringify({ items: [{ ...waiting, deadline: '2026-08-10', bucket: 'overdue' }] }),
        ]),
      );

    const section = await shown(fixture, '[data-testid="waiting-section"]');

    const deadline = [...section.querySelectorAll('span')].find((s) =>
      s.textContent!.includes('Deadline:'),
    )!;
    expect(deadline.className).toContain('text-red-600');
  });

  it('should ask who the task waits on only while it is waiting', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: [items[1], waiting] })]));
    const section = await shown(fixture, '[data-testid="waiting-section"]');
    const element = fixture.nativeElement as HTMLElement;

    section.querySelector('button')!.click();
    fixture.detectChanges();

    const input = section.querySelector<HTMLInputElement>('[data-testid="waiting-on-input"]')!;
    expect(input.value).toBe('Bo');

    const open = element.querySelector('[data-testid="task-section"] [data-testid="task-row"]')!;
    open.querySelector('button')!.click();
    fixture.detectChanges();

    expect(open.querySelector('[data-testid="waiting-on-input"]')).toBeNull();
  });

  it('should save who the task waits on when the field loses focus', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: [waiting] })]));
    const section = await shown(fixture, '[data-testid="waiting-section"]');
    section.querySelector('button')!.click();
    fixture.detectChanges();

    const input = section.querySelector<HTMLInputElement>('[data-testid="waiting-on-input"]')!;
    input.value = 'Anna';
    input.dispatchEvent(new Event('blur'));

    const request = http.expectOne(`/api/tasks/${waiting.id}`);
    expect(JSON.parse(request.request.body).waitingOn).toBe('Anna');
  });

  // Uddelegering er en genvej til Venter på + hvem: vælgeren spørger, og feltet skal være det
  // næste man står i. Uden denne påstand ville brugeren skulle finde feltet selv.
  it('should put the cursor in the who field when a task is handed over', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;
    row.querySelector('button')!.click();
    fixture.detectChanges();

    const select = row.querySelector<HTMLSelectElement>('[data-testid="task-detail"] select')!;
    select.value = 'waitingFor';
    select.dispatchEvent(new Event('change'));

    // Feltet findes ikke endnu: @if hænger på opgavens status, som først skifter når PUT'en er
    // svaret og listen genindlæst. Uden dette led ville et fokus, der kun virker fordi feltet
    // tilfældigvis stod der i forvejen, se ud som om det virkede.
    expect(row.querySelector('[data-testid="waiting-on-input"]')).toBeNull();

    const put = await vi.waitFor(() => http.expectOne(`/api/tasks/${items[0].id}`));
    put.flush(new Blob([JSON.stringify(handedOver[0])]));
    const reload = await vi.waitFor(() =>
      http.expectOne('/api/tasks?includeCompleted=false&includeSomeday=false'),
    );
    reload.flush(new Blob([JSON.stringify({ items: handedOver })]));

    const input = await shown(fixture, '[data-testid="waiting-on-input"]');
    expect(document.activeElement).toBe(input);
  });

  // Det er valget der flytter fokus, ikke feltets tilstedeværelse. Uden denne påstand ville et
  // ubetinget focus() bestå alt — målt — og enhver udvidelse af en ventende række ville rive
  // fokus væk fra rækkeknappen man netop trykkede på.
  it('should leave the focus alone when a waiting row is merely expanded', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: [waiting] })]));
    const section = await shown(fixture, '[data-testid="waiting-section"]');

    const button = section.querySelector<HTMLButtonElement>('button')!;
    button.focus();
    button.click();
    fixture.detectChanges();

    expect(section.querySelector('[data-testid="waiting-on-input"]')).not.toBeNull();
    expect(document.activeElement).toBe(button);
  });

  // Forslag, ikke krav. Denne og den næste er de to tilstande et strengt valg ville gøre
  // uopnåelige, og de findes begge i dag.
  it('should still let a task wait for nobody in particular', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(SettingsStore).delegates.set(delegates);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: [waiting] })]));
    const section = await shown(fixture, '[data-testid="waiting-section"]');
    section.querySelector('button')!.click();
    fixture.detectChanges();

    const input = section.querySelector<HTMLInputElement>('[data-testid="waiting-on-input"]')!;
    input.value = '';
    input.dispatchEvent(new Event('blur'));

    const body = JSON.parse(http.expectOne(`/api/tasks/${waiting.id}`).request.body);
    expect(body.waitingOn).toBeUndefined();
    expect(body.status).toBe('waitingFor');
  });

  it('should save a name that is not on the list, because the list only suggests', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    TestBed.inject(SettingsStore).delegates.set(delegates);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: [waiting] })]));
    const section = await shown(fixture, '[data-testid="waiting-section"]');
    section.querySelector('button')!.click();
    fixture.detectChanges();

    const input = section.querySelector<HTMLInputElement>('[data-testid="waiting-on-input"]')!;
    input.value = 'Supporten hos leverandøren';
    input.dispatchEvent(new Event('blur'));

    expect(JSON.parse(http.expectOne(`/api/tasks/${waiting.id}`).request.body).waitingOn).toBe(
      'Supporten hos leverandøren',
    );
  });

  // Én liste for alle rækker: et id pr. række ville være et duplikeret id, som browseren ikke
  // klager over — den vælger blot den første.
  it('should offer the delegates as suggestions from a single shared list', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(SettingsStore).delegates.set(delegates);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: [...items, waiting] })]));
    const section = await shown(fixture, '[data-testid="waiting-section"]');
    section.querySelector('button')!.click();
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    const lists = element.querySelectorAll('datalist');
    expect(lists).toHaveLength(1);
    // Et tomt id på begge sider ville ellers gøre påstanden nedenfor sand om ingenting.
    expect(lists[0].id).not.toBe('');
    expect(
      section
        .querySelector<HTMLInputElement>('[data-testid="waiting-on-input"]')!
        .getAttribute('list'),
    ).toBe(lists[0].id);
    expect([...lists[0].querySelectorAll('option')].map((o) => o.value)).toEqual(delegates);
  });

  it('should hide a parked task until the someday toggle is on', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));
    const element = await rendered(fixture);

    expect(element.querySelector('[data-testid="someday-section"]')).toBeNull();

    element.querySelector<HTMLInputElement>('[data-testid="show-someday"]')!.click();
    fixture.detectChanges();
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=true')
      .flush(new Blob([JSON.stringify({ items: [...items, parked] })]));

    const section = await shown(fixture, '[data-testid="someday-section"]');

    expect(section.querySelector('h2')!.textContent!.trim()).toBe('Måske');
    expect(section.querySelector('[data-testid="task-row"]')!.textContent).toContain(
      'Lær at spille harmonika',
    );
  });

  it('should let a parked task be expanded so its status can be changed back', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));
    await rendered(fixture);
    (fixture.nativeElement as HTMLElement)
      .querySelector<HTMLInputElement>('[data-testid="show-someday"]')!
      .click();
    fixture.detectChanges();
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=true')
      .flush(new Blob([JSON.stringify({ items: [parked] })]));
    const section = await shown(fixture, '[data-testid="someday-section"]');

    section.querySelector('button')!.click();
    fixture.detectChanges();

    const select = section.querySelector<HTMLSelectElement>('[data-testid="task-detail"] select')!;
    expect(select.value).toBe('someday');

    select.value = 'open';
    select.dispatchEvent(new Event('change'));

    expect(JSON.parse(http.expectOne(`/api/tasks/${parked.id}`).request.body).status).toBe('open');
  });

  it('should not create a task from a blank input', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));
    const element = await rendered(fixture);

    const input = element.querySelector<HTMLInputElement>('[data-testid="new-task-input"]')!;
    input.value = '   ';
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));

    http.verify();
    expect(input.value).toBe('   ');
  });
});
