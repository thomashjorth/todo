import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { API_BASE_URL } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { TaskList } from './task-list';

const items = [
  {
    id: '11111111-1111-1111-1111-111111111111',
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
    id: '22222222-2222-2222-2222-222222222222',
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

const withSubTasks = [
  {
    ...items[0],
    subTasks: [
      { id: 'aaaa', title: 'Find kontonummeret', isDone: true },
      { id: 'bbbb', title: 'Overfør beløbet', isDone: false },
    ],
  },
  items[1],
];

const note = '**fed** tekst\n\n- et punkt\n\n<script>alert(1)</script>';

const withNote = [{ ...items[0], note }, items[1]];

const linkNote = 'Se [dokumentationen](https://example.com/docs) først';

const withLink = [{ ...items[0], note: linkNote }, items[1]];

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
      .expectOne('/api/tasks?includeCompleted=false')
      .flush(new Blob([JSON.stringify({ items })]));

    const element = await rendered(fixture);

    const headings = [...element.querySelectorAll('[data-testid="task-section"] h2')].map((h) =>
      h.textContent?.trim(),
    );
    expect(headings).toEqual(['Overskredet', 'Uden deadline']);
  });

  it('should show the deadline written out in the active language', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false')
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
      .expectOne('/api/tasks?includeCompleted=false')
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
      .expectOne('/api/tasks?includeCompleted=false')
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
      .expectOne('/api/tasks?includeCompleted=false')
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
      .expectOne('/api/tasks?includeCompleted=false')
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
      .expectOne('/api/tasks?includeCompleted=false')
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
      .expectOne('/api/tasks?includeCompleted=false')
      .flush(new Blob([JSON.stringify({ items })]));
    const element = await rendered(fixture);

    element.querySelector<HTMLInputElement>('[data-testid="show-completed"]')!.click();
    fixture.detectChanges();
    http
      .expectOne('/api/tasks?includeCompleted=true')
      .flush(
        new Blob([JSON.stringify({ items: [...items, { ...items[0], id: 'x', status: 'done' }] })]),
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
      .expectOne('/api/tasks?includeCompleted=false')
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
      .expectOne('/api/tasks?includeCompleted=false')
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
      .expectOne('/api/tasks?includeCompleted=false')
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
      .expectOne('/api/tasks?includeCompleted=false')
      .flush(new Blob([JSON.stringify({ items: withSubTasks })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;
    row.querySelector('button')!.click();
    fixture.detectChanges();

    const checkbox = row.querySelectorAll('[data-testid="subtask-row"]')[1].querySelector('input')!;
    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();

    const request = http.expectOne(`/api/tasks/${items[0].id}/subtasks/bbbb`);
    expect(JSON.parse(request.request.body)).toEqual({ title: 'Overfør beløbet', isDone: true });
    expect(row.querySelector('[data-testid="task-detail"]')).not.toBeNull();
  });

  it('should delete a subtask from its own row', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false')
      .flush(new Blob([JSON.stringify({ items: withSubTasks })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;
    row.querySelector('button')!.click();
    fixture.detectChanges();

    row.querySelector<HTMLButtonElement>('[data-testid="delete-subtask"]')!.click();

    const request = http.expectOne(`/api/tasks/${items[0].id}/subtasks/aaaa`);
    expect(request.request.method).toBe('DELETE');
  });

  it('should show the note as rendered markdown rather than as its source', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false')
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
      .expectOne('/api/tasks?includeCompleted=false')
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
      .expectOne('/api/tasks?includeCompleted=false')
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
      .expectOne('/api/tasks?includeCompleted=false')
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
      .expectOne('/api/tasks?includeCompleted=false')
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

  it('should save and close the editor on Escape', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false')
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
      .expectOne('/api/tasks?includeCompleted=false')
      .flush(new Blob([JSON.stringify({ items })]));
    const row = (await rendered(fixture)).querySelector('[data-testid="task-row"]')!;

    row.querySelector('button')!.click();
    fixture.detectChanges();

    const placeholder = row.querySelector('[data-testid="note-rendered"]')!;
    expect(placeholder.textContent!.trim()).toBe('Tilføj en note');
    expect(placeholder.className).toContain('italic');
  });

  it('should not create a task from a blank input', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/tasks?includeCompleted=false')
      .flush(new Blob([JSON.stringify({ items })]));
    const element = await rendered(fixture);

    const input = element.querySelector<HTMLInputElement>('[data-testid="new-task-input"]')!;
    input.value = '   ';
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));

    http.verify();
    expect(input.value).toBe('   ');
  });
});
