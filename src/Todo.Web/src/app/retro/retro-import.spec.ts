import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_BASE_URL } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
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

function open(): Screen {
  const fixture = TestBed.createComponent(RetroImport);
  const http = TestBed.inject(HttpTestingController);
  const element = fixture.nativeElement as HTMLElement;
  fixture.detectChanges();

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

async function analyse(body: unknown): Promise<Screen> {
  const screen = open();

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
      imports: [RetroImport, translocoTesting()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
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
    expect(rows[0].textContent).toContain('Deadline: 24. jul. 2026');
    expect(rows[1].textContent).not.toContain('Deadline');
    expect(element.querySelector('table')).toBeNull();
  });

  it('should pre-select the rows I own and leave the others alone', async () => {
    const { element } = await analyse({ rows: [mine, theirs], skippedRatingCards: 0 });

    expect(checkboxes(element).map((c) => c.checked)).toEqual([true, false]);
    expect(importButton(element).textContent).toContain('Importér 1 opgave');
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
    screen.http.expectOne('/api/retro/preview').flush(
      new Blob([
        JSON.stringify({
          code: 'retro.emptyExport',
          message: "It needs a header row with a 'Content' column.",
        }),
      ]),
      { status: 400, statusText: 'Bad Request' },
    );

    const error = await settled(screen, '[data-testid="retro-error"]');

    expect(error.textContent).toContain(
      'Eksporten er tom. Den skal have en overskriftsrække med en Content-kolonne.',
    );
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
    screen.http.expectOne('/api/retro/import').flush(
      new Blob([
        JSON.stringify({
          code: 'retro.rowTitleRequired',
          message: 'A row is missing its title.',
        }),
      ]),
      { status: 400, statusText: 'Bad Request' },
    );

    const error = await settled(screen, '[data-testid="retro-error"]');

    expect(error.textContent).toContain('En række mangler sin titel.');
    expect(screen.element.querySelectorAll('[data-testid="retro-row"]')).toHaveLength(1);
    expect(screen.element.querySelector('[data-testid="retro-receipt"]')!.textContent!.trim()).toBe(
      '',
    );
  });

  it('should point at the settings page rather than edit the aliases here', () => {
    const { element } = open();

    const link = element.querySelector<HTMLAnchorElement>('[data-testid="retro-settings-link"]')!;
    expect(link.getAttribute('href')).toBe('/settings');
    expect(link.textContent!.trim()).toBe('Ret dine navne under Indstillinger');
    expect(element.querySelector('[data-testid="alias-input"]')).toBeNull();
    expect(element.querySelector('[data-testid="retro-alias-section"]')).toBeNull();
  });
});
