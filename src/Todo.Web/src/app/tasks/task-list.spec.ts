import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api/todo-client';
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
      imports: [TaskList],
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

  it('should show the deadline as the plain date string the API returned', async () => {
    const fixture = TestBed.createComponent(TaskList);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/tasks?includeCompleted=false')
      .flush(new Blob([JSON.stringify({ items })]));

    const rows = (await rendered(fixture)).querySelectorAll('[data-testid="task-row"]');

    expect(rows[0].textContent).toContain('Betal regningen');
    expect(rows[0].textContent).toContain('2026-08-10');
    expect(rows[0].textContent).toContain('Anna');
    expect(rows[1].textContent).not.toContain('Deadline');
  });

  it('should create a task on Enter and clear the input', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/tasks?includeCompleted=false').flush(new Blob([JSON.stringify({ items })]));
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
    expect(rows[1].querySelector('[data-testid="task-detail"]')).toBeNull();

    rows[1].querySelector('button')!.click();
    fixture.detectChanges();

    expect(rows[0].querySelector('[data-testid="task-detail"]')).toBeNull();
    expect(rows[1].querySelector('[data-testid="task-detail"]')).not.toBeNull();
  });

  it('should delete the task without asking first', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/tasks?includeCompleted=false').flush(new Blob([JSON.stringify({ items })]));
    const element = await rendered(fixture);
    const row = element.querySelector('[data-testid="task-row"]')!;

    row.querySelector('button')!.click();
    fixture.detectChanges();
    row.querySelector<HTMLButtonElement>('[data-testid="delete-task"]')!.click();

    const request = http.expectOne(`/api/tasks/${items[0].id}`);
    expect(request.request.method).toBe('DELETE');
  });

  it('should not create a task from a blank input', async () => {
    const fixture = TestBed.createComponent(TaskList);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/tasks?includeCompleted=false').flush(new Blob([JSON.stringify({ items })]));
    const element = await rendered(fixture);

    const input = element.querySelector<HTMLInputElement>('[data-testid="new-task-input"]')!;
    input.value = '   ';
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));

    http.verify();
    expect(input.value).toBe('   ');
  });
});
