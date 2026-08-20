import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_BASE_URL } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { SettingsStore } from '../settings/settings-store';
import { AdoImport } from './ado-import';

/**
 * The wire shape, not the generated class: this is what the server sends. The fourth hand-written
 * wire fixture in the repository, and like the other three it has no compiler over it - so
 * `workItemType`, which does not exist on a Jira row, is spelled out here rather than left to a
 * type. A field the screen forgot to read would be a silently missing line, not a compiler error.
 */
interface PreviewRowJson {
  key: string;
  title: string;
  url: string;
  note?: string;
  deadline?: string;
  requester?: string;
  state: string;
  workItemType: string;
  isWaiting: boolean;
  waitingSince?: string;
  alreadyImported: boolean;
  excluded?: string;
}

function row(overrides: Partial<PreviewRowJson> = {}): PreviewRowJson {
  const merged: PreviewRowJson = {
    key: '15664',
    title: 'Ret rapporten',
    url: '',
    state: 'Active',
    workItemType: 'Bug',
    // The server derives a deadline for every row unless the day count is zero, so the ordinary row
    // carries one - a fixture without it would make the "no deadline" line the normal case.
    deadline: '2026-08-23',
    isWaiting: false,
    alreadyImported: false,
    ...overrides,
  };

  // Derived from whatever key won, so two rows in the same fixture do not share one URL - a test
  // that opens the second row would otherwise pass against the first row's address.
  return {
    ...merged,
    url: overrides.url ?? `https://ado.example/Min%20Samling/Saas/_workitems/edit/${merged.key}`,
  };
}

const waitingReason = 'Du venter på den, og ventende sager er slået fra.';

interface Screen {
  fixture: ComponentFixture<AdoImport>;
  element: HTMLElement;
  http: HttpTestingController;
}

/** Azure DevOps is set up, which is what puts the Load button on the screen at all. */
function configure(): void {
  const settings = TestBed.inject(SettingsStore);
  settings.adoBaseUrl.set('https://ado.test/Min%20Samling');
  settings.adoProject.set('Saas');
  settings.hasAdoToken.set(true);
}

function open(): Screen {
  const fixture = TestBed.createComponent(AdoImport);
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

async function preview(body: unknown): Promise<Screen> {
  configure();
  const screen = open();

  screen.element.querySelector<HTMLButtonElement>('[data-testid="ado-preview"]')!.click();

  const request = await vi.waitFor(() => screen.http.expectOne('/api/ado/preview'));
  expect(request.request.method).toBe('POST');
  request.flush(new Blob([JSON.stringify(body)]));

  await settled(
    screen,
    '[data-testid="ado-showing"], [data-testid="ado-none-assigned"], [data-testid="ado-import-error"]',
  );
  return screen;
}

function rows(element: HTMLElement): HTMLElement[] {
  return [...element.querySelectorAll<HTMLElement>('[data-testid="ado-row"]')];
}

function checkboxes(element: HTMLElement): HTMLInputElement[] {
  return [...element.querySelectorAll<HTMLInputElement>('[data-testid="ado-row"] input')];
}

function importButton(element: HTMLElement): HTMLButtonElement {
  return element.querySelector<HTMLButtonElement>('[data-testid="ado-import"]')!;
}

describe('AdoImport', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AdoImport, translocoTesting()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '' },
      ],
    }).compileComponents();
  });

  it('should say that Azure DevOps is not set up and point at the settings page', () => {
    const { element, http } = open();

    expect(element.querySelector('[data-testid="ado-not-configured"]')!.textContent).toContain(
      'Azure DevOps er ikke sat op',
    );

    const link = element.querySelector<HTMLAnchorElement>('[data-testid="ado-settings-link"]')!;
    expect(link.getAttribute('href')).toBe('/settings');
    expect(link.textContent!.trim()).toBe('Sæt Azure DevOps op under Indstillinger');

    // Nothing to press, so nothing was asked of a server that cannot answer.
    expect(element.querySelector('[data-testid="ado-preview"]')).toBeNull();
    // And the notice about the proposed deadline belongs with the button, not with the refusal.
    expect(element.querySelector('[data-testid="ado-deadline-notice"]')).toBeNull();
    http.verify();
  });

  // The one thing no Jira screen has to say. Jira's deadline came off the issue; this one is the
  // app's own arithmetic, and a date shown without the words reads as an agreement somebody made.
  it('should call the deadline a proposal, and say when there is none', async () => {
    const { element } = await preview({
      total: 2,
      rows: [
        row({ key: '15664' }),
        row({ key: '16901', title: 'Uden deadline', deadline: undefined }),
      ],
    });

    expect(element.querySelector('[data-testid="ado-deadline-notice"]')!.textContent).toContain(
      'Azure DevOps har ingen deadline, så appen foreslår en',
    );

    const proposed = rows(element)[0].querySelector('[data-testid="ado-deadline"]')!;
    expect(proposed.textContent).toContain('Foreslået deadline');
    expect(proposed.textContent).toContain('2026');
    expect(rows(element)[0].querySelector('[data-testid="ado-no-deadline"]')).toBeNull();

    // Said out loud rather than left blank: a missing line cannot be told apart from a setting that
    // did not take, and zero days is a deliberate choice.
    const none = rows(element)[1].querySelector('[data-testid="ado-no-deadline"]')!;
    expect(none.textContent).toContain('Uden deadline');
    expect(rows(element)[1].querySelector('[data-testid="ado-deadline"]')).toBeNull();
  });

  // A state name does not mean the same thing across types: a Test Suite uses In Progress where a
  // Bug uses Active. Without the type the user cannot tell whether the state is the one they marked
  // as waiting.
  it('should show the work item type beside the state', async () => {
    const { element } = await preview({
      total: 2,
      rows: [
        row({ key: '15664', state: 'Active', workItemType: 'Bug' }),
        row({ key: '16901', title: 'Den anden', state: 'In Progress', workItemType: 'Test Suite' }),
      ],
    });

    const types = [...element.querySelectorAll('[data-testid="ado-type"]')];
    expect(types.map((t) => t.textContent!.trim())).toEqual(['Type: Bug', 'Type: Test Suite']);
    expect(rows(element)[1].textContent).toContain('Tilstand: In Progress');
  });

  it('should say what else comes along with a row, and nothing about what does not', async () => {
    const { element } = await preview({
      total: 2,
      rows: [
        row({
          key: '15664',
          requester: 'Mette Kirkegaard',
          note: '<div>Kan ikke logge ind<br>Fejl 500</div>',
          state: 'Blocked',
          isWaiting: true,
          waitingSince: '2026-08-14T12:00:00Z',
        }),
        row({ key: '16901', title: 'Bar sag' }),
      ],
    });

    const [full, bare] = rows(element);

    expect(full.querySelector('[data-testid="ado-requester"]')!.textContent).toContain(
      'Opgavestiller: Mette Kirkegaard',
    );

    // That there is a description, not what it says: the field is Azure DevOps' raw HTML rather than
    // CommonMark, so the markup shown here would be neither the note the user ends up reading nor an
    // honest rendering of it.
    const note = full.querySelector('[data-testid="ado-note"]')!;
    expect(note.textContent).toContain('Beskrivelsen følger med');
    expect(note.textContent).not.toContain('<div>');

    expect(full.querySelector('[data-testid="ado-waiting"]')!.textContent).toContain(
      'Importeres som ventende',
    );

    const since = full.querySelector('[data-testid="ado-waiting-since"]')!;
    expect(since.textContent).toContain('Venter siden');
    expect(since.textContent).toMatch(/\b14\b/);
    expect(since.textContent).toContain('2026');

    // The other half: a row without any of it says none of it, so the four lines above are about
    // these fields rather than about paragraphs that are always there.
    for (const testid of ['ado-requester', 'ado-note', 'ado-waiting', 'ado-waiting-since']) {
      expect(bare.querySelector(`[data-testid="${testid}"]`)).toBeNull();
    }
  });

  it('should keep an excluded work item on screen, switched off, with its reason', async () => {
    const { element } = await preview({
      total: 2,
      rows: [
        row({ key: '15664' }),
        row({
          key: '16901',
          title: 'Afventer kunden',
          state: 'Blocked',
          isWaiting: true,
          excluded: 'ado.excludedWaiting',
        }),
      ],
    });

    // Visible rather than hidden: a hidden row looks like a work item Azure DevOps lost, and it
    // would make the adoIncludeWaiting setting invisible.
    expect(rows(element)).toHaveLength(2);
    expect(rows(element)[1].textContent).toContain('Afventer kunden');
    expect(rows(element)[1].textContent).toContain('Tilstand: Blocked');

    expect(checkboxes(element).map((c) => c.disabled)).toEqual([false, true]);
    expect(checkboxes(element).map((c) => c.checked)).toEqual([true, false]);
    expect(element.querySelector('[data-testid="ado-excluded"]')!.textContent).toContain(
      waitingReason,
    );
    expect(importButton(element).textContent).toContain('Importér 1 sag');
  });

  it('should switch a work item imported before off too, and give a different reason', async () => {
    const { element } = await preview({
      total: 2,
      rows: [
        row({ key: '15664' }),
        row({ key: '16901', title: 'Skriv driftsvejledningen', alreadyImported: true }),
      ],
    });

    expect(rows(element)).toHaveLength(2);
    expect(checkboxes(element).map((c) => c.disabled)).toEqual([false, true]);

    const reason = element.querySelector('[data-testid="ado-already-imported"]')!;
    expect(reason.textContent).toContain('importeret tidligere');
    // Not the excluded reason: the two states have to be told apart on the screen.
    expect(reason.textContent).not.toContain(waitingReason);
    expect(element.querySelector('[data-testid="ado-excluded"]')).toBeNull();
  });

  it('should say which of the two reasons emptied the selection, and how many', async () => {
    const { element } = await preview({
      total: 3,
      rows: [
        row({ key: '15664', excluded: 'ado.excludedWaiting' }),
        row({ key: '16901', title: 'To', excluded: 'ado.excludedWaiting' }),
        row({ key: '17170', title: 'Tre', alreadyImported: true }),
      ],
    });

    const summary = element.querySelector('[data-testid="ado-nothing-to-select"]')!;
    expect(summary.textContent).toContain('2 sager er udeladt af importen.');
    expect(summary.textContent).toContain('1 sag er importeret tidligere.');
    expect(importButton(element).disabled).toBe(true);
  });

  it('should call an empty answer an answer rather than an error', async () => {
    const { element } = await preview({ total: 0, rows: [] });

    expect(element.querySelector('[data-testid="ado-none-assigned"]')!.textContent).toContain(
      'Ingen sager er tildelt dig.',
    );
    expect(element.querySelector('[data-testid="ado-import-error"]')).toBeNull();
    expect(rows(element)).toHaveLength(0);
  });

  it('should show how many of the total are on screen', async () => {
    const { element } = await preview({ total: 40, rows: [row(), row({ key: '16901' })] });

    expect(element.querySelector('[data-testid="ado-showing"]')!.textContent).toContain(
      'Viser 2 af 40 sager.',
    );
  });

  it('should ask the shell to open a work item before it has been imported', async () => {
    const screen = await preview({
      total: 2,
      rows: [row({ key: '15664' }), row({ key: '16901', title: 'Den anden' })],
    });

    // The second row, not the first: opening the first one could pass against a screen that had
    // hard-wired one address for every row.
    const second = rows(screen.element)[1];
    const open = second.querySelector<HTMLButtonElement>('[data-testid="ado-open-item"]')!;

    // A BUTTON, not an anchor: the Photino window has no address bar and no way back.
    expect(open.tagName).toBe('BUTTON');
    expect(open.textContent!.trim()).toBe('Åbn sagen');

    open.click();

    const request = await vi.waitFor(() => screen.http.expectOne('/api/system/open-link'));
    expect(JSON.parse(request.request.body).url).toBe(
      'https://ado.example/Min%20Samling/Saas/_workitems/edit/16901',
    );
  });

  it('should say beside the row that its work item could not be opened, and only there', async () => {
    const screen = await preview({
      total: 2,
      rows: [row({ key: '15664' }), row({ key: '16901', title: 'Den anden' })],
    });

    const [first, second] = rows(screen.element);

    // Absent before the click, so the assertion below is about the click rather than about a
    // paragraph that was always there.
    expect(second.querySelector('[data-testid="ado-open-error"]')).toBeNull();

    second.querySelector<HTMLButtonElement>('[data-testid="ado-open-item"]')!.click();

    const request = await vi.waitFor(() => screen.http.expectOne('/api/system/open-link'));
    request.flush(
      new Blob([
        JSON.stringify({
          code: 'system.unsupportedScheme',
          message: 'Only http and https links can be opened.',
        }),
      ]),
      { status: 400, statusText: 'Bad Request' },
    );

    const error = await vi.waitFor(() => {
      screen.fixture.detectChanges();
      const element = second.querySelector('[data-testid="ado-open-error"]');
      expect(element).not.toBeNull();
      return element!;
    });

    // The text, not only the element: a paragraph carrying an empty message would satisfy a claim
    // about its presence and still say nothing at all to the user.
    expect(error.textContent!.trim()).toBe('Kun http- og https-links kan åbnes.');
    expect(error.getAttribute('role')).toBe('alert');

    // The half that measures the key match. SystemStore.error is a single screen-level signal, so a
    // paragraph bound straight to it would stand in every row at once.
    expect(first.querySelector('[data-testid="ado-open-error"]')).toBeNull();
  });

  it('should show why Azure DevOps refused instead of a list', async () => {
    configure();
    const screen = open();

    screen.element.querySelector<HTMLButtonElement>('[data-testid="ado-preview"]')!.click();
    const request = await vi.waitFor(() => screen.http.expectOne('/api/ado/preview'));
    request.flush(new Blob([JSON.stringify({ code: 'ado.unreachable', message: 'Nope.' })]), {
      status: 400,
      statusText: 'Bad Request',
    });

    const error = await settled(screen, '[data-testid="ado-import-error"]');

    expect(error.textContent).toContain('Azure DevOps kunne ikke nås.');
    expect(error.getAttribute('role')).toBe('alert');
    expect(rows(screen.element)).toHaveLength(0);
  });

  it('should send only the ticked work items, and no decision the server owns', async () => {
    const screen = await preview({
      total: 3,
      rows: [
        row({
          key: '15664',
          title: 'Ret rapporten',
          note: '<div>Se bilaget</div>',
          requester: 'Mette Kirkegaard',
          state: 'Blocked',
          workItemType: 'User Story',
          isWaiting: true,
          waitingSince: '2026-08-14T12:00:00Z',
        }),
        row({ key: '16901', title: 'Den jeg ikke valgte' }),
        row({ key: '17170', title: 'Den udeladte', excluded: 'ado.excludedWaiting' }),
      ],
    });

    const [, second] = checkboxes(screen.element);
    second.checked = false;
    second.dispatchEvent(new Event('change', { bubbles: true }));
    screen.fixture.detectChanges();

    expect(importButton(screen.element).textContent).toContain('Importér 1 sag');
    importButton(screen.element).click();

    const request = screen.http.expectOne('/api/ado/import');
    const body = JSON.parse(request.request.body);
    expect(body).toEqual({
      rows: [
        {
          key: '15664',
          title: 'Ret rapporten',
          note: '<div>Se bilaget</div>',
          requester: 'Mette Kirkegaard',
          state: 'Blocked',
          workItemType: 'User Story',
          waitingSince: '2026-08-14T12:00:00Z',
        },
      ],
    });
    // The server looks the state up in the user's waiting list and derives the deadline from its own
    // clock, so the client must not send its answer to either question.
    expect(Object.keys(body.rows[0])).not.toContain('isWaiting');
    expect(Object.keys(body.rows[0])).not.toContain('deadline');
    request.flush(new Blob([JSON.stringify({ imported: 1, skipped: 0 })]));

    // Importing reloads, so a row that just became "imported before" says so without a click.
    const reloaded = await vi.waitFor(() => screen.http.expectOne('/api/ado/preview'));
    reloaded.flush(
      new Blob([
        JSON.stringify({
          total: 3,
          rows: [
            row({ key: '15664', alreadyImported: true }),
            row({ key: '16901', title: 'Den jeg ikke valgte' }),
            row({ key: '17170', title: 'Den udeladte', excluded: 'ado.excludedWaiting' }),
          ],
        }),
      ]),
    );

    const receipt = await settled(screen, '[data-testid="ado-receipt"]');
    await vi.waitFor(() => {
      screen.fixture.detectChanges();
      expect(receipt.textContent).toContain('1 importeret, 0 sprunget over');
    });
    expect(receipt.getAttribute('aria-live')).toBe('polite');
    await vi.waitFor(() => {
      screen.fixture.detectChanges();
      expect(checkboxes(screen.element)[0].disabled).toBe(true);
    });
  });
});
