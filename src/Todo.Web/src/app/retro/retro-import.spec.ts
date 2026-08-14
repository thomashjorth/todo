import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api/todo-client';
import { RetroImport } from './retro-import';

const csv = '"Content","Author","Zone"\n"Write the retro summary","Thomas Hjorth","Actions"';

const mine = {
  key: 'aaaa',
  title: 'Write the retro summary',
  owner: 'Thomas Hjorth',
  author: 'Thomas Hjorth',
  zone: 'Actions',
  deadline: '2026-07-24',
  isMine: true,
  alreadyImported: false,
};

const theirs = {
  key: 'bbbb',
  title: 'Book a room for the next one',
  owner: 'Mette Kirkegaard',
  author: 'Mette Kirkegaard',
  zone: 'Actions',
  deadline: null,
  isMine: false,
  alreadyImported: false,
};

interface Analysed {
  fixture: ComponentFixture<RetroImport>;
  element: HTMLElement;
  http: HttpTestingController;
}

async function analyse(body: unknown): Promise<Analysed> {
  const fixture = TestBed.createComponent(RetroImport);
  const http = TestBed.inject(HttpTestingController);
  const element = fixture.nativeElement as HTMLElement;
  fixture.detectChanges();

  const textarea = element.querySelector<HTMLTextAreaElement>('[data-testid="retro-csv"]')!;
  textarea.value = csv;
  textarea.dispatchEvent(new Event('input'));
  element.querySelector<HTMLButtonElement>('[data-testid="retro-analyse"]')!.click();

  const preview = await vi.waitFor(() => http.expectOne('/api/retro/preview'));
  expect(JSON.parse(preview.request.body).csv).toBe(csv);
  preview.flush(new Blob([JSON.stringify(body)]));

  // The generated client requests responseType 'blob' and decodes it with FileReader,
  // so a flushed response only reaches the template after a later microtask.
  await vi.waitFor(() => {
    fixture.detectChanges();
    expect(
      element.querySelector('[data-testid="retro-skipped"], [data-testid="retro-error"]'),
    ).not.toBeNull();
  });

  return { fixture, element, http };
}

function checkboxes(element: HTMLElement): HTMLInputElement[] {
  return [...element.querySelectorAll<HTMLInputElement>('[data-testid="retro-row"] input')];
}

describe('RetroImport', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RetroImport],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '' },
      ],
    }).compileComponents();
  });

  it('should show nothing but the paste box until the export is analysed', () => {
    const fixture = TestBed.createComponent(RetroImport);
    fixture.detectChanges();
    const element = fixture.nativeElement as HTMLElement;

    expect(element.querySelector('[data-testid="retro-csv"]')).not.toBeNull();
    expect(element.querySelectorAll('[data-testid="retro-row"]')).toHaveLength(0);
    expect(element.querySelector('[data-testid="retro-skipped"]')).toBeNull();
  });

  it('should show each row as a card with its zone, owner and deadline', async () => {
    const { element } = await analyse({ rows: [mine, theirs], skippedRatingCards: 0 });

    const rows = element.querySelectorAll('[data-testid="retro-row"]');
    expect(rows).toHaveLength(2);
    expect(rows[0].textContent).toContain('Write the retro summary');
    expect(rows[0].textContent).toContain('Zone: Actions');
    expect(rows[0].textContent).toContain('Ejer: Thomas Hjorth');
    expect(rows[0].textContent).toContain('Deadline: 2026-07-24');
    expect(rows[1].textContent).not.toContain('Deadline');
    expect(element.querySelector('table')).toBeNull();
  });

  it('should pre-select the rows I own and leave the others alone', async () => {
    const { element } = await analyse({ rows: [mine, theirs], skippedRatingCards: 0 });

    expect(checkboxes(element).map((c) => c.checked)).toEqual([true, false]);
    expect(element.querySelector('[data-testid="retro-none-mine"]')).toBeNull();
  });

  it('should explain an empty pre-selection when no row has me as owner', async () => {
    const { element } = await analyse({ rows: [theirs], skippedRatingCards: 0 });

    expect(checkboxes(element).map((c) => c.checked)).toEqual([false]);
    expect(element.querySelector('[data-testid="retro-none-mine"]')!.textContent).toContain(
      'Ingen af rækkerne har dig som ejer.',
    );
  });

  it('should refuse to select a row that was imported before', async () => {
    const { element } = await analyse({
      rows: [{ ...mine, alreadyImported: true }],
      skippedRatingCards: 0,
    });

    const [checkbox] = checkboxes(element);
    expect(checkbox.disabled).toBe(true);
    expect(checkbox.checked).toBe(false);
    expect(element.querySelector('[data-testid="retro-already-imported"]')!.textContent).toContain(
      'importeret tidligere',
    );
  });

  it('should count the rating cards the server dropped', async () => {
    const { element } = await analyse({ rows: [mine], skippedRatingCards: 18 });

    expect(element.querySelector('[data-testid="retro-skipped"]')!.textContent).toContain(
      'Sprang 18 afstemningskort over.',
    );
  });

  it('should show the reason the server rejected the export instead of the rows', async () => {
    const fixture = TestBed.createComponent(RetroImport);
    const http = TestBed.inject(HttpTestingController);
    const element = fixture.nativeElement as HTMLElement;
    fixture.detectChanges();

    element.querySelector<HTMLButtonElement>('[data-testid="retro-analyse"]')!.click();
    http
      .expectOne('/api/retro/preview')
      .flush(new Blob([JSON.stringify("It needs a header row with a 'Content' column.")]), {
        status: 400,
        statusText: 'Bad Request',
      });

    const error = await vi.waitFor(() => {
      fixture.detectChanges();
      const message = element.querySelector('[data-testid="retro-error"]');
      expect(message).not.toBeNull();
      return message!;
    });

    expect(error.textContent).toContain("It needs a header row with a 'Content' column.");
    expect(element.querySelectorAll('[data-testid="retro-row"]')).toHaveLength(0);
  });
});
