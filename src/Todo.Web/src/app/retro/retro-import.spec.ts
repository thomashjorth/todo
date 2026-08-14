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

interface Screen {
  fixture: ComponentFixture<RetroImport>;
  element: HTMLElement;
  http: HttpTestingController;
}

function open(aliases: string[] = []): Screen {
  const fixture = TestBed.createComponent(RetroImport);
  const http = TestBed.inject(HttpTestingController);
  const element = fixture.nativeElement as HTMLElement;
  fixture.detectChanges();
  http.expectOne('/api/retro/aliases').flush(new Blob([JSON.stringify({ aliases })]));

  return { fixture, element, http };
}

// The generated client requests responseType 'blob' and decodes it with FileReader,
// so a flushed response only reaches the template after a later microtask.
function settled(screen: Screen, selector: string): Promise<HTMLElement> {
  return vi.waitFor(() => {
    screen.fixture.detectChanges();
    const found = screen.element.querySelector<HTMLElement>(selector);
    expect(found).not.toBeNull();
    return found!;
  });
}

async function analyse(body: unknown, aliases: string[] = []): Promise<Screen> {
  const screen = open(aliases);

  const textarea = screen.element.querySelector<HTMLTextAreaElement>('[data-testid="retro-csv"]')!;
  textarea.value = csv;
  textarea.dispatchEvent(new Event('input'));
  screen.element.querySelector<HTMLButtonElement>('[data-testid="retro-analyse"]')!.click();

  const preview = await vi.waitFor(() => screen.http.expectOne('/api/retro/preview'));
  expect(JSON.parse(preview.request.body).csv).toBe(csv);
  preview.flush(new Blob([JSON.stringify(body)]));

  await settled(screen, '[data-testid="retro-skipped"], [data-testid="retro-error"]');
  return screen;
}

function checkboxes(element: HTMLElement): HTMLInputElement[] {
  return [...element.querySelectorAll<HTMLInputElement>('[data-testid="retro-row"] input')];
}

function importButton(element: HTMLElement): HTMLButtonElement {
  return element.querySelector<HTMLButtonElement>('[data-testid="retro-import"]')!;
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
    const { element } = open();

    expect(element.querySelector('[data-testid="retro-csv"]')).not.toBeNull();
    expect(element.querySelectorAll('[data-testid="retro-row"]')).toHaveLength(0);
    expect(element.querySelector('[data-testid="retro-skipped"]')).toBeNull();
    expect(element.querySelector('[data-testid="retro-import"]')).toBeNull();
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
    expect(importButton(element).textContent).toContain('Importér 1 opgaver');
    expect(importButton(element).disabled).toBe(false);
    expect(element.querySelector('[data-testid="retro-none-mine"]')).toBeNull();
  });

  it('should explain an empty pre-selection when no row has me as owner', async () => {
    const { element } = await analyse({ rows: [theirs], skippedRatingCards: 0 });

    expect(checkboxes(element).map((c) => c.checked)).toEqual([false]);
    expect(element.querySelector('[data-testid="retro-none-mine"]')!.textContent).toContain(
      'Ingen af rækkerne har dig som ejer.',
    );
  });

  it('should send no import request while nothing is selected', async () => {
    const { element, http } = await analyse({ rows: [theirs], skippedRatingCards: 0 });

    expect(importButton(element).textContent).toContain('Importér 0 opgaver');
    expect(importButton(element).disabled).toBe(true);

    importButton(element).click();

    http.verify();
  });

  it('should refuse to select a row that was imported before', async () => {
    const { fixture, element, http } = await analyse({
      rows: [{ ...mine, alreadyImported: true }],
      skippedRatingCards: 0,
    });

    const [checkbox] = checkboxes(element);
    expect(checkbox.disabled).toBe(true);
    expect(checkbox.checked).toBe(false);
    expect(element.querySelector('[data-testid="retro-already-imported"]')!.textContent).toContain(
      'importeret tidligere',
    );

    checkbox.checked = true;
    checkbox.dispatchEvent(new Event('change', { bubbles: true }));
    fixture.detectChanges();

    expect(importButton(element).textContent).toContain('Importér 0 opgaver');
    http.verify();
  });

  it('should count the rating cards the server dropped', async () => {
    const { element } = await analyse({ rows: [mine], skippedRatingCards: 18 });

    expect(element.querySelector('[data-testid="retro-skipped"]')!.textContent).toContain(
      'Sprang 18 afstemningskort over.',
    );
  });

  it('should show the reason the server rejected the export instead of the rows', async () => {
    const screen = open();

    screen.element.querySelector<HTMLButtonElement>('[data-testid="retro-analyse"]')!.click();
    screen.http
      .expectOne('/api/retro/preview')
      .flush(
        new Blob([
          JSON.stringify({
            code: 'retro.emptyExport',
            message: "It needs a header row with a 'Content' column.",
          }),
        ]),
        { status: 400, statusText: 'Bad Request' },
      );

    const error = await settled(screen, '[data-testid="retro-error"]');

    expect(error.textContent).toContain("It needs a header row with a 'Content' column.");
    expect(error.getAttribute('role')).toBe('alert');
    expect(screen.element.querySelectorAll('[data-testid="retro-row"]')).toHaveLength(0);
  });

  it('should import the selected rows, announce the receipt and stay on the screen', async () => {
    const screen = await analyse({ rows: [mine, theirs], skippedRatingCards: 0 });

    importButton(screen.element).click();

    const imported = screen.http.expectOne('/api/retro/import');
    expect(JSON.parse(imported.request.body)).toEqual({
      rows: [
        {
          key: 'aaaa',
          title: 'Write the retro summary',
          requester: 'Thomas Hjorth',
          deadline: '2026-07-24',
        },
      ],
    });
    imported.flush(new Blob([JSON.stringify({ imported: 1, skipped: 0 })]));

    const preview = await vi.waitFor(() => screen.http.expectOne('/api/retro/preview'));
    preview.flush(
      new Blob([
        JSON.stringify({
          rows: [{ ...mine, alreadyImported: true }, theirs],
          skippedRatingCards: 0,
        }),
      ]),
    );

    await vi.waitFor(() => {
      screen.fixture.detectChanges();
      expect(checkboxes(screen.element)[0].disabled).toBe(true);
    });

    const receipt = await settled(screen, '[data-testid="retro-receipt"]');
    expect(receipt.textContent).toContain('1 importeret, 0 sprunget over');
    expect(receipt.getAttribute('aria-live')).toBe('polite');
    expect(screen.element.querySelector('[data-testid="retro-csv"]')).not.toBeNull();
    expect(importButton(screen.element).textContent).toContain('Importér 0 opgaver');
  });

  it('should keep the rows when the import is refused, and say why', async () => {
    const screen = await analyse({ rows: [mine], skippedRatingCards: 0 });

    importButton(screen.element).click();
    screen.http
      .expectOne('/api/retro/import')
      .flush(
        new Blob([
          JSON.stringify({
            code: 'retro.rowTitleRequired',
            message: 'A row is missing its title.',
          }),
        ]),
        { status: 400, statusText: 'Bad Request' },
      );

    const error = await settled(screen, '[data-testid="retro-error"]');

    expect(error.textContent).toContain('A row is missing its title.');
    expect(screen.element.querySelectorAll('[data-testid="retro-row"]')).toHaveLength(1);
    expect(screen.element.querySelector('[data-testid="retro-receipt"]')!.textContent!.trim()).toBe(
      '',
    );
  });

  it('should add an alias on Enter and analyse the export again', async () => {
    const screen = await analyse({ rows: [theirs], skippedRatingCards: 0 }, ['TH']);

    const input = screen.element.querySelector<HTMLInputElement>('[data-testid="alias-input"]')!;
    input.value = '  Mette Kirkegaard  ';
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));

    const saved = screen.http.expectOne('/api/retro/aliases');
    expect(saved.request.method).toBe('PUT');
    expect(JSON.parse(saved.request.body)).toEqual({ aliases: ['TH', 'Mette Kirkegaard'] });
    expect(input.value).toBe('');
    saved.flush(new Blob([JSON.stringify({ aliases: ['Mette Kirkegaard', 'TH'] })]));

    const preview = await vi.waitFor(() => screen.http.expectOne('/api/retro/preview'));
    preview.flush(
      new Blob([JSON.stringify({ rows: [{ ...theirs, isMine: true }], skippedRatingCards: 0 })]),
    );

    await vi.waitFor(() => {
      screen.fixture.detectChanges();
      expect(checkboxes(screen.element).map((c) => c.checked)).toEqual([true]);
    });

    const rows = screen.element.querySelectorAll('[data-testid="alias-row"]');
    expect([...rows].map((r) => r.querySelector('span')!.textContent!.trim())).toEqual([
      'Mette Kirkegaard',
      'TH',
    ]);
  });

  it('should remove an alias from its own labelled button', async () => {
    const screen = await analyse({ rows: [mine], skippedRatingCards: 0 }, ['TH', 'Thomas Hjorth']);

    const rows = screen.element.querySelectorAll('[data-testid="alias-row"]');
    const remove = rows[1].querySelector<HTMLButtonElement>('[data-testid="remove-alias"]')!;
    expect(remove.getAttribute('aria-label')).toBe('Fjern Thomas Hjorth');

    remove.click();

    const saved = screen.http.expectOne('/api/retro/aliases');
    expect(saved.request.method).toBe('PUT');
    expect(JSON.parse(saved.request.body)).toEqual({ aliases: ['TH'] });
  });

  it('should send no alias request for a blank or repeated name', async () => {
    const screen = open(['TH']);
    await vi.waitFor(() => {
      screen.fixture.detectChanges();
      expect(screen.element.querySelectorAll('[data-testid="alias-row"]')).toHaveLength(1);
    });

    const input = screen.element.querySelector<HTMLInputElement>('[data-testid="alias-input"]')!;
    for (const value of ['   ', 'TH']) {
      input.value = value;
      input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));
    }

    screen.http.verify();
    expect(input.value).toBe('TH');
  });
});
