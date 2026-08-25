import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { API_BASE_URL } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { WideScreen } from '../layout/wide-screen';
import { SettingsStore } from '../settings/settings-store';
import { ShortcutStore } from '../shortcuts/shortcut-store';
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

/**
 * Ti valgbare opgaver plus en fuldført. Rækkefølgen på skærmen er otte i sektionen, den ventende,
 * den fuldførte og til sidst den parkerede - så den fuldførte står mellem "Venter på" og "Måske",
 * hvor den er nummereringens tænder: uden en fuldført række i listen ville påstanden om at en
 * fuldført ikke har et nummer bestå, fordi der slet ikke var en at måle.
 */
const numberedItems = [
  ...Array.from({ length: 8 }, (_, i) => ({
    ...items[1],
    id: 101 + i,
    title: `Opgave ${i + 1}`,
  })),
  { ...waiting, id: 109, title: 'Venter på Bo' },
  {
    ...items[1],
    id: 110,
    title: 'Ryd op i skuret',
    status: 'done',
    completedAt: '2026-08-20T09:00:00+00:00',
  },
  { ...parked, id: 111, title: 'Lær at spille harmonika' },
];

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

  // Typing in the box removes the rows that do not match. The store is unit-tested on the filter
  // itself; what this adds is that the field is wired to it and that the list re-renders.
  /**
   * Visible and on top, which is one requirement in two halves. The second item is the one in
   * progress and it is in the later bucket, so it has to overtake its own section-mate - and the
   * marker has to be somewhere a reader can see it.
   */
  it('should mark an in-progress task and lift it to the top of its section', async () => {
    // The in-progress task is seeded *second*, and that ordering is the assertion's only teeth: put
    // it first and an unsorted section would pass this test too.
    const inProgress = [
      items[1],
      { ...items[1], id: 3, title: 'Under vejs', status: 'inProgress' },
    ];

    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: [items[0], ...inProgress] })]));

    const element = await vi.waitFor(() => {
      fixture.detectChanges();
      const host = fixture.nativeElement as HTMLElement;
      expect(host.querySelectorAll('[data-testid="task-row"]').length).toBe(3);
      return host;
    });

    const marker = element.querySelector('[data-testid="in-progress"]');
    expect(marker).not.toBeNull();
    expect(marker!.textContent?.trim()).toBe('I gang');

    // The last section is the one holding both, and the in-progress task comes first in it.
    const sections = element.querySelectorAll('[data-testid="task-section"]');
    const rows = sections[sections.length - 1].querySelectorAll('[data-testid="task-row"]');
    expect(rows[0].textContent).toContain('Under vejs');

    // Outside the row's button, so it stays out of the accessible name the E2E screen matches whole.
    const button = rows[0].querySelector('button')!;
    expect(button.textContent).not.toContain('I gang');
  });
  it('should remove the rows that do not match what is typed in the search box', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));

    const element = await rendered(fixture);

    const search = element.querySelector<HTMLInputElement>('[data-testid="task-search"]')!;
    search.value = 'tandl';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const rows = element.querySelectorAll('[data-testid="task-row"]');
    expect(rows).toHaveLength(1);
    expect(rows[0].textContent).toContain('Ring til tandl');
  });

  // "No tasks" in front of a search that simply found nothing would read as if the list had been
  // lost, so the two kinds of empty say different things.
  it('should say nothing matched rather than that there are no tasks', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));

    const element = await rendered(fixture);

    const search = element.querySelector<HTMLInputElement>('[data-testid="task-search"]')!;
    search.value = 'nothing that exists';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(element.querySelectorAll('[data-testid="task-row"]')).toHaveLength(0);

    const message = element.querySelector('[data-testid="no-matches"]');
    expect(message).not.toBeNull();
    expect(message!.textContent).toContain('Ingen opgaver passer');
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

  /**
   * Side by side is a different DOM, not a wider one: the panel moves out of its row and into a
   * column of its own. Every test in here sets the signal before the component renders, because
   * jsdom has no matchMedia and the service therefore starts out narrow - which is what all the
   * tests above measure.
   */
  describe('side by side', () => {
    beforeEach(() => {
      TestBed.inject(WideScreen).wide.set(true);
    });

    /**
     * Auto-selection, and the seeding order is the whole of the assertion's teeth: the two tasks
     * are flushed with the *no deadline* one first, so an implementation that reached for the
     * server's `items[0]` would answer "Ring til tandlægen" and fail. The expected answer is the
     * first task in *visual* order, which is the overdue section's.
     */
    it('should show the first task on screen without anyone opening a row', async () => {
      const fixture = TestBed.createComponent(TaskList);
      TestBed.inject(HttpTestingController)
        .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
        .flush(new Blob([JSON.stringify({ items: [items[1], items[0]] })]));

      // Waited for the panel rather than the column: the column renders as soon as the signal says
      // wide, which is before the task list has arrived - the generated client decodes its response
      // through a Blob, so it lands a microtask later. Waiting for the column alone measured the
      // empty state and called it the wrong answer.
      const detail = await shown(
        fixture,
        '[data-testid="detail-column"] [data-testid="task-detail"]',
      );

      expect(detail.querySelector<HTMLInputElement>('input[type="date"]')!.value).toBe(
        '2026-08-10',
      );
    });

    it('should say which row the panel belongs to', async () => {
      const fixture = TestBed.createComponent(TaskList);
      TestBed.inject(HttpTestingController)
        .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
        .flush(new Blob([JSON.stringify({ items })]));
      const rows = (await rendered(fixture)).querySelectorAll('[data-testid="task-row"]');

      // The accent, and on the auto-selected row rather than a clicked one, so it also covers that
      // the mark and the panel agree about which task is showing.
      expect(rows[0].querySelector('.border-blue-600')).not.toBeNull();
      expect(rows[1].querySelector('.border-blue-600')).toBeNull();
    });

    /**
     * Exactly one panel in the document. Without the `!wide()` term on the row's `expanded` input
     * there would be two elements carrying `data-testid="task-detail"`, and a Playwright locator
     * would silently pick the first - which is the reason the breakpoint is a signal at all.
     */
    it('should render the panel once, outside the row', async () => {
      const fixture = TestBed.createComponent(TaskList);
      TestBed.inject(HttpTestingController)
        .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
        .flush(new Blob([JSON.stringify({ items })]));
      const element = await rendered(fixture);

      element.querySelectorAll('[data-testid="task-row"]')[1].querySelector('button')!.click();
      fixture.detectChanges();

      expect(element.querySelectorAll('[data-testid="task-detail"]').length).toBe(1);
      expect(
        element
          .querySelectorAll('[data-testid="task-row"]')[1]
          .querySelector('[data-testid="task-detail"]'),
      ).toBeNull();
    });

    it('should keep the selection when the selected row is clicked again', async () => {
      const fixture = TestBed.createComponent(TaskList);
      TestBed.inject(HttpTestingController)
        .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
        .flush(new Blob([JSON.stringify({ items })]));
      const element = await rendered(fixture);
      const second = element.querySelectorAll('[data-testid="task-row"]')[1];

      second.querySelector('button')!.click();
      fixture.detectChanges();
      const column = element.querySelector('[data-testid="detail-column"]')!;
      // Ring til tandlægen has no deadline, so an empty date field is how the panel names it apart
      // from the auto-selected first task.
      expect(column.querySelector<HTMLInputElement>('input[type="date"]')!.value).toBe('');

      second.querySelector('button')!.click();
      fixture.detectChanges();

      expect(column.querySelector('[data-testid="task-detail"]')).not.toBeNull();
      expect(column.querySelector<HTMLInputElement>('input[type="date"]')!.value).toBe('');
    });

    it('should move the panel to the first task left when the selected one is searched away', async () => {
      const fixture = TestBed.createComponent(TaskList);
      TestBed.inject(HttpTestingController)
        .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
        .flush(new Blob([JSON.stringify({ items })]));
      const element = await rendered(fixture);

      element.querySelectorAll('[data-testid="task-row"]')[1].querySelector('button')!.click();
      fixture.detectChanges();
      const column = element.querySelector('[data-testid="detail-column"]')!;
      expect(column.querySelector<HTMLInputElement>('input[type="date"]')!.value).toBe('');

      const search = element.querySelector<HTMLInputElement>('[data-testid="task-search"]')!;
      search.value = 'Betal';
      search.dispatchEvent(new Event('input'));
      fixture.detectChanges();

      expect(column.querySelector<HTMLInputElement>('input[type="date"]')!.value).toBe(
        '2026-08-10',
      );
    });

    /**
     * Completed tasks have no panel behind them - their row is a plain `<li>` - so they are not
     * selectable, and with only completed tasks in view the column has nothing to show. The row
     * assertion is the teeth: without it the prompt would also pass on an empty list, which is a
     * different state reached a different way.
     */
    it('should ask for a pick when the only tasks in view are completed', async () => {
      const finished = {
        ...items[0],
        id: 6,
        title: 'Ryd op i skuret',
        status: 'done',
        completedAt: '2026-08-20T09:00:00+00:00',
      };
      const fixture = TestBed.createComponent(TaskList);
      const http = TestBed.inject(HttpTestingController);
      http
        .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
        .flush(new Blob([JSON.stringify({ items: [] })]));
      // shown returns the element it matched, not the root, so the switch is what comes back here.
      const showCompleted = await shown(fixture, '[data-testid="show-completed"]');

      showCompleted.click();
      http
        .expectOne('/api/tasks?includeCompleted=true&includeSomeday=false')
        .flush(new Blob([JSON.stringify({ items: [finished] })]));
      const row = await shown(
        fixture,
        '[data-testid="completed-section"] [data-testid="task-row"]',
      );
      const element = fixture.nativeElement as HTMLElement;

      expect(row.textContent).toContain('Ryd op i skuret');
      expect(element.querySelector('[data-testid="task-detail"]')).toBeNull();
      expect(element.querySelector('[data-testid="detail-empty"]')!.textContent!.trim()).toBe(
        'Vælg en opgave for at se detaljerne.',
      );
    });
  });

  /**
   * The narrow half of the two rules above, and it is a rule rather than an accident: nothing is
   * auto-selected in one column, because an unfolded panel pushes the rest of the list some 300px
   * down and an app that opens with task number one unfolded hides the others.
   */
  it('should open nothing by itself in one column', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));

    const element = await rendered(fixture);

    expect(element.querySelector('[data-testid="detail-column"]')).toBeNull();
    expect(element.querySelector('[data-testid="task-detail"]')).toBeNull();
  });

  it('should fold the panel away in one column when the selected task is searched away', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items })]));
    const element = await rendered(fixture);
    const row = element.querySelectorAll('[data-testid="task-row"]')[0];

    row.querySelector('button')!.click();
    fixture.detectChanges();
    expect(row.querySelector('[data-testid="task-detail"]')).not.toBeNull();

    const search = element.querySelector<HTMLInputElement>('[data-testid="task-search"]')!;
    search.value = 'tandl';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    expect(element.querySelector('[data-testid="task-detail"]')).toBeNull();
  });

  // Begge kontakter slået til, så den fuldførte og den parkerede sektion er på skærmen.
  async function everythingShown(
    fixture: ComponentFixture<TaskList>,
    http: HttpTestingController,
  ): Promise<HTMLElement> {
    http
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: numberedItems })]));
    (await shown(fixture, '[data-testid="show-completed"]')).click();
    fixture.detectChanges();
    http
      .expectOne('/api/tasks?includeCompleted=true&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: numberedItems })]));
    (await shown(fixture, '[data-testid="show-someday"]')).click();
    fixture.detectChanges();
    http
      .expectOne('/api/tasks?includeCompleted=true&includeSomeday=true')
      .flush(new Blob([JSON.stringify({ items: numberedItems })]));

    return vi.waitFor(() => {
      fixture.detectChanges();
      const element = fixture.nativeElement as HTMLElement;
      expect(element.querySelectorAll('[data-testid="task-row"]').length).toBe(
        numberedItems.length,
      );
      return element;
    });
  }

  it('should number the first nine selectable rows and leave the rest without one', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const element = await everythingShown(fixture, TestBed.inject(HttpTestingController));

    const labels = [...element.querySelectorAll('[data-testid="task-row"]')].map(
      (row) => row.querySelector('button')?.getAttribute('aria-keyshortcuts') ?? null,
    );

    // Række ét til ni i skærmens rækkefølge, og den niende er den ventende: numrene krydser
    // sektionsgrænsen frem for at begynde forfra i hver sektion.
    expect(labels.slice(0, 9)).toEqual([
      'Alt+1',
      'Alt+2',
      'Alt+3',
      'Alt+4',
      'Alt+5',
      'Alt+6',
      'Alt+7',
      'Alt+8',
      'Alt+9',
    ]);

    // Den tiende valgbare - den parkerede, som står efter den fuldførte blok - har ingen genvej.
    const tenth = element.querySelector(
      '[data-testid="someday-section"] [data-testid="task-row"] button',
    )!;
    expect(tenth.getAttribute('aria-keyshortcuts')).toBeNull();

    // Og den fuldførte har intet panel at vælge, så der er slet ingen genvej i rækken.
    const completedRow = element.querySelector(
      '[data-testid="completed-section"] [data-testid="task-row"]',
    )!;
    expect(completedRow.querySelector('[aria-keyshortcuts]')).toBeNull();
  });

  /**
   * En række beholder sin komponentinstans, når dens nummer skifter - `@for` sporer på id - så
   * direktivets effekt er det eneste der flytter registreringen med. Påstanden er derfor både på
   * attributten og på registret: den overtagne tast skal ramme den nye række, og den tast der ikke
   * længere er på skærmen skal være væk.
   */
  it('should hand a number over to the row that takes its place', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const shortcuts = TestBed.inject(ShortcutStore);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
      .flush(new Blob([JSON.stringify({ items: numberedItems.slice(0, 3) })]));

    const element = await vi.waitFor(() => {
      fixture.detectChanges();
      const host = fixture.nativeElement as HTMLElement;
      expect(host.querySelectorAll('[data-testid="task-row"]').length).toBe(3);
      return host;
    });

    const before = [...element.querySelectorAll('[data-testid="task-row"] button')].map((b) =>
      b.getAttribute('aria-keyshortcuts'),
    );
    expect(before).toEqual(['Alt+1', 'Alt+2', 'Alt+3']);

    // Søgningen efterlader den tredje række alene, så den overtager Alt+1.
    const search = element.querySelector<HTMLInputElement>('[data-testid="task-search"]')!;
    search.value = 'Opgave 3';
    search.dispatchEvent(new Event('input'));
    fixture.detectChanges();

    const row = element.querySelector('[data-testid="task-row"]')!;
    expect(row.textContent).toContain('Opgave 3');
    expect(row.querySelector('button')!.getAttribute('aria-keyshortcuts')).toBe('Alt+1');

    // Registret fulgte med: Alt+1 rammer den nye ejer, og Alt+3 er afmeldt.
    expect(shortcuts.activate('alt+3')).toBe(false);
    expect(shortcuts.activate('alt+1')).toBe(true);
    fixture.detectChanges();
    expect(row.querySelector('[data-testid="task-detail"]')).not.toBeNull();
  });
  /**
   * The names the browser morphs rows by. Both kinds of row have to carry one, and they are two
   * separate bindings in two files: a host binding on `TaskRow`, which covers the three `@for` loops
   * over `li[appTaskRow]`, and one in this template for the completed section's plain `<li>`.
   *
   * jsdom keeps `view-transition-name` - measured against 28.1.0, both through `setProperty` and the
   * camelCase property, and it reflects into the style attribute - so the binding is visible here
   * rather than only from Playwright.
   */
  describe('section transitions', () => {
    it('should name every task row so the browser can morph it', async () => {
      const fixture = TestBed.createComponent(TaskList);
      TestBed.inject(HttpTestingController)
        .expectOne('/api/tasks?includeCompleted=false&includeSomeday=false')
        .flush(new Blob([JSON.stringify({ items })]));

      const element = await rendered(fixture);

      const names = [...element.querySelectorAll<HTMLElement>('[data-testid="task-row"]')].map(
        (row) => row.style.getPropertyValue('view-transition-name'),
      );
      // Pinned to the ids rather than merely non-empty: the name is the identity the morph is
      // matched on, so two rows sharing one would make the browser skip the transition outright.
      expect(names).toEqual(['task-1', 'task-2']);
    });

    /**
     * The completed row is a plain `<li>` with no `appTaskRow`, so the host binding does not reach
     * it - and it is exactly the row a task lands on when it is ticked off with completed tasks
     * shown. Without a name on both sides of that move there is no morph, only a cross-fade.
     */
    it('should name a completed row too, since a task ticked off moves into that list', async () => {
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

      const row = await vi.waitFor(() => {
        fixture.detectChanges();
        const found = element.querySelector<HTMLElement>(
          '[data-testid="completed-section"] [data-testid="task-row"]',
        );
        expect(found).not.toBeNull();
        return found!;
      });

      expect(row.style.getPropertyValue('view-transition-name')).toBe('task-9');
    });
  });
});
