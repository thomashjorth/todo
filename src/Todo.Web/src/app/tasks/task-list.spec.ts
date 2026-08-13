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
});
