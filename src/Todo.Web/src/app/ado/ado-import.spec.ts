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
  // Nullable, because this interface describes the wire and reconciliation made both fields so.
  state?: string | null;
  workItemType?: string | null;
  isWaiting: boolean;
  waitingSince?: string;
  alreadyImported: boolean;
  suggestsClosing: boolean;
  closureReason?: string;
  suggestsReopening: boolean;
  // Required on the contract, and spelled here because nothing else would notice if it went missing:
  // the `as I*PreviewRow` cast in this file's generated-row helper accepts an object without it, so
  // even the compiler-visible fixture is blind to a new required field. Measured when it was added.
  reconciled: boolean;
  reopenTo?: string;
  doneAt?: string;
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
    suggestsClosing: false,
    suggestsReopening: false,
    // The ordinary row came from the fetch. The reconciled ones set it true, and the screen's
    // "showing N of total" line counts on the difference - total is the fetch's number alone.
    reconciled: false,
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

  /**
   * The line counts what the fetch answered with, because `total` is the source's number for the
   * fetch alone and a reconciled row was never in it. Measured with a probe before the field existed:
   * pairing `rows().length` with `total` said "Viser 4 af 3 sager." the moment anything was reconciled.
   *
   * No existing assertion could see it. All five of them run in states where nothing has been
   * reconciled, so `shown == total` holds whichever number is used - which is why this fixture has
   * three fetched rows and a fourth the app went looking for.
   */
  it('should count only the rows the fetch answered with, not the ones it went looking for', async () => {
    const { element } = await preview({
      total: 3,
      rows: [
        row({ key: '15664' }),
        row({ key: '16901', title: 'To' }),
        row({ key: '17170', title: 'Tre' }),
        row({
          key: '17165',
          title: 'Den der faldt ud af hentningen',
          state: 'Resolved',
          alreadyImported: true,
          suggestsClosing: true,
          reconciled: true,
        }),
      ],
    });

    expect(rows(element)).toHaveLength(4);
    expect(element.querySelector('[data-testid="ado-showing"]')!.textContent).toContain(
      'Viser 3 af 3 sager.',
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

  /**
   * A closure candidate is imported-before, and the old rule blocked every imported-before row. It has
   * to be tickable again — and ticked on arrival, because it is a suggestion and the user unticks what
   * should not happen. The blocked row beside it is the teeth: without it, "the checkbox is enabled"
   * would also pass on a screen that stopped blocking anything at all.
   */
  it('should offer a finished work item for closing, ticked, beside a row that stays blocked', async () => {
    const { element } = await preview({
      total: 2,
      rows: [
        row({
          key: '16901',
          title: 'Ret regningen',
          state: 'Resolved',
          alreadyImported: true,
          suggestsClosing: true,
          doneAt: '2026-08-15T09:00:00Z',
        }),
        row({ key: '17170', title: 'Skriv testene', alreadyImported: true }),
      ],
    });

    const [closing, seenBefore] = rows(element);
    const [closingBox, seenBeforeBox] = checkboxes(element);

    expect(closing.querySelector('[data-testid="ado-suggests-closing"]')!.textContent).toContain(
      'Løst i Azure DevOps',
    );
    expect(closingBox.disabled).toBe(false);
    expect(closingBox.checked).toBe(true);

    // The label it took the place of, on the row that did not earn the offer.
    expect(closing.querySelector('[data-testid="ado-already-imported"]')).toBeNull();
    expect(seenBefore.querySelector('[data-testid="ado-suggests-closing"]')).toBeNull();
    expect(seenBeforeBox.disabled).toBe(true);
    expect(seenBeforeBox.checked).toBe(false);
  });

  /**
   * One press, two lists. The row that would be imported and the row that would be closed go into
   * separate arrays, because the server writes one and updates the other — and `doneAt` rides along
   * as a fact, since the import deliberately never calls Azure DevOps.
   */
  it('should split one press into the rows it imports and the tasks it closes', async () => {
    const screen = await preview({
      total: 2,
      rows: [
        row({ key: '15664', title: 'Ret rapporten' }),
        row({
          key: '16901',
          title: 'Ret regningen',
          state: 'Resolved',
          alreadyImported: true,
          suggestsClosing: true,
          doneAt: '2026-08-15T09:00:00Z',
        }),
      ],
    });

    // Both are ticked on arrival, so the button has to say it does two different things - one
    // clause per set, which is the shape that survived a third set being added.
    expect(importButton(screen.element).textContent).toContain('Importér 1 sag · Luk 1 opgave');

    importButton(screen.element).click();

    const request = await vi.waitFor(() => screen.http.expectOne('/api/ado/import'));
    const body = JSON.parse(request.request.body);

    expect(body.rows.map((r: { key: string }) => r.key)).toEqual(['15664']);
    // The reason rides along because the server cannot work it out from the state alone: its first
    // guard asks whether the state is in the done list, which an unassigned closure's never is.
    // This fixture names no reason, so the row reads as the resolved one - the only kind the fetch
    // could suggest before reconciliation - which is what makes the fallback measured here. It used
    // to be the only place: no spec under jira/ asserted on the closures its store sends until
    // e01c6f6, and the note saying so outlived the hole. Both sources are measured now.
    expect(body.closures).toEqual([
      { key: '16901', state: 'Resolved', reason: 'resolved', doneAt: '2026-08-15T09:00:00Z' },
    ]);

    request.flush(new Blob([JSON.stringify({ imported: 1, skipped: 0, closed: 1 })]));
    // Waited for rather than expected outright: the generated client decodes its response through
    // a Blob, so the reload the import triggers is one microtask behind the flush above.
    const reload = await vi.waitFor(() => screen.http.expectOne('/api/ado/preview'));
    reload.flush(new Blob([JSON.stringify({ rows: [], total: 0 })]));

    const receipt = await settled(screen, '[data-testid="ado-receipt"]');

    // Two sentences, and the second only when something was closed: a receipt that always said
    // "0 lukket" would train the eye to skip the line that matters on the day it is not zero.
    expect(receipt.textContent).toContain('1 importeret, 0 sprunget over. 1 opgave lukket.');
  });

  /**
   * The third set on screen. A reopening candidate was imported before, and the old rule blocked
   * every imported-before row - so `isBlocked` has to ask about all three sets or the suggestion is
   * offered with its checkbox switched off.
   *
   * The two rows beside it are the teeth, and they are two rather than one because they fail
   * differently: the plain imported-before row would also pass a screen that stopped blocking
   * anything at all, and the unexplained row is the one reconciliation added - it is imported before
   * *and* reconciled, so a rule that unblocked reconciled rows wholesale would let it through.
   */
  it('should offer a work item the source is not finished with for reopening, ticked', async () => {
    const { element } = await preview({
      // Two of the three rows came from the fetch; the unexplained one is reconciled.
      total: 2,
      rows: [
        row({
          key: '17170',
          title: 'Den er ikke færdig endda',
          state: 'Active',
          alreadyImported: true,
          suggestsReopening: true,
          reopenTo: 'open',
        }),
        row({ key: '16901', title: 'Skriv testene', alreadyImported: true }),
        row({
          key: '17165',
          title: 'Den kilden ikke kender',
          state: null,
          workItemType: null,
          alreadyImported: true,
          reconciled: true,
          excluded: 'ado.missingInSource',
        }),
      ],
    });

    const [reopeningBox, seenBeforeBox, unexplainedBox] = checkboxes(element);

    expect(reopeningBox.disabled).toBe(false);
    expect(reopeningBox.checked).toBe(true);
    expect(seenBeforeBox.disabled).toBe(true);
    expect(unexplainedBox.disabled).toBe(true);
  });

  /**
   * The button says what it does for each set it acts on, because it now does three things and one
   * number cannot say which of them happened. Three sets, three clauses, and the counts come from
   * the sets rather than from `total` - which is the source's number for the fetch and has never
   * counted a reconciliation row.
   */
  it('should say what it does for each of the three sets it acts on', async () => {
    const screen = await preview({
      // Two, because two of the three rows came from the fetch - and deliberately not one, which is
      // what makes the three counts measurable. With `total: 1` a clause taken from `total` instead
      // of from its own set would print the same "1" and the assertion could not fail. Measured:
      // with the old fixture, mutating the first clause to `store.total()` felled three older tests
      // and left this one green.
      total: 2,
      rows: [
        row({ key: '15664', title: 'Ret rapporten' }),
        row({
          key: '16901',
          title: 'Ret regningen',
          state: 'Resolved',
          alreadyImported: true,
          suggestsClosing: true,
          doneAt: '2026-08-15T09:00:00Z',
        }),
        row({
          key: '17170',
          title: 'Den er ikke færdig endda',
          state: 'Active',
          alreadyImported: true,
          suggestsReopening: true,
          reopenTo: 'open',
          reconciled: true,
        }),
      ],
    });

    // All three arrive ticked, which is what makes this the union rather than one of the parts.
    expect(importButton(screen.element).textContent).toContain(
      'Importér 1 sag · Luk 1 opgave · Genåbn 1 opgave',
    );

    importButton(screen.element).click();

    const request = await vi.waitFor(() => screen.http.expectOne('/api/ado/import'));
    const body = JSON.parse(request.request.body);
    expect(body.rows.map((r: { key: string }) => r.key)).toEqual(['15664']);
    expect(body.closures.map((r: { key: string }) => r.key)).toEqual(['16901']);
    expect(body.reopenings).toEqual([{ key: '17170', state: 'Active' }]);

    request.flush(new Blob([JSON.stringify({ imported: 1, skipped: 0, closed: 1, reopened: 1 })]));
    const reload = await vi.waitFor(() => screen.http.expectOne('/api/ado/preview'));
    reload.flush(new Blob([JSON.stringify({ rows: [], total: 0 })]));

    const receipt = await settled(screen, '[data-testid="ado-receipt"]');

    // Three sentences, each only when its own number is not zero: a receipt that always said
    // "0 genåbnet" would train the eye to skip the line that matters on the day it is not.
    expect(receipt.textContent).toContain(
      '1 importeret, 0 sprunget over. 1 opgave lukket. 1 opgave genåbnet.',
    );
  });

  /**
   * A finished work item nobody imported is kept out with a reason of its own, rather than brought in
   * as a task that is already over. The reason is an error code, translated through the same path a
   * refusal takes.
   */
  it('should keep a finished work item nobody imported out, and say why', async () => {
    const { element } = await preview({
      total: 1,
      rows: [
        row({
          key: '17170',
          title: 'Luk den gamle sag',
          state: 'Closed',
          excluded: 'ado.excludedDone',
        }),
      ],
    });

    const [only] = rows(element);

    expect(only.querySelector('[data-testid="ado-excluded"]')!.textContent).toContain(
      'løst i Azure DevOps',
    );
    expect(checkboxes(element)[0].disabled).toBe(true);
    expect(importButton(element).disabled).toBe(true);
  });

  /**
   * Two sentences rather than one, because the two findings are not the same news: a finished work
   * item says the work is over, and one handed to somebody else says it stopped being yours. Both
   * rows carry `suggestsClosing`, so a chain reading that boolean alone would print one sentence over
   * both — which is what this measures. The texts are asserted whole rather than by a fragment: the
   * resolved one is byte-identical to the sentence that predates the split, and a `toContain` on
   * "luk opgaven her" would pass on either of them.
   */
  it('should tell a resolved closure apart from one handed to somebody else', async () => {
    const { element } = await preview({
      total: 0,
      rows: [
        row({
          key: '16901',
          title: 'Ret regningen',
          state: 'Resolved',
          alreadyImported: true,
          suggestsClosing: true,
          closureReason: 'resolved',
          doneAt: '2026-08-15T09:00:00Z',
          reconciled: true,
        }),
        row({
          key: '17170',
          title: 'Den en anden overtog',
          state: 'Active',
          alreadyImported: true,
          suggestsClosing: true,
          closureReason: 'unassigned',
          reconciled: true,
        }),
      ],
    });

    const [resolved, handedOn] = rows(element);

    expect(
      resolved.querySelector('[data-testid="ado-suggests-closing"]')!.textContent!.trim(),
    ).toBe('Løst i Azure DevOps — luk opgaven her.');
    expect(
      handedOn.querySelector('[data-testid="ado-suggests-closing"]')!.textContent!.trim(),
    ).toBe('Tildelt en anden i Azure DevOps — luk opgaven her.');
  });

  /**
   * The reopening's own label, and the status it would restore beside it — the contract puts
   * `reopenTo` on the row so the suggestion says what it will do, and nothing read it until here.
   *
   * Both rows are `alreadyImported`, which is what gives the last assertion teeth: a reopening
   * candidate is imported-before by definition, so a chain asking that first would swallow this
   * branch and print "importeret tidligere" over an offer the user is meant to act on.
   */
  it('should say a reopening is on offer and which status it would restore', async () => {
    const { element } = await preview({
      total: 2,
      rows: [
        row({
          key: '17170',
          title: 'Den er ikke færdig endda',
          state: 'Active',
          alreadyImported: true,
          suggestsReopening: true,
          reopenTo: 'open',
        }),
        row({
          key: '16901',
          title: 'Den der venter på nogen',
          state: 'Blocked',
          alreadyImported: true,
          suggestsReopening: true,
          reopenTo: 'waitingFor',
        }),
      ],
    });

    const [toOpen, toWaiting] = rows(element);

    expect(toOpen.querySelector('[data-testid="ado-suggests-reopening"]')!.textContent).toContain(
      'Ikke færdig i Azure DevOps',
    );

    // Two rows rather than one, and they differ only in `reopenTo`: with a single row a template
    // that hard-wired either sentence would pass.
    expect(toOpen.querySelector('[data-testid="ado-reopen-to"]')!.textContent!.trim()).toBe(
      'Genåbnes som åben.',
    );
    expect(toWaiting.querySelector('[data-testid="ado-reopen-to"]')!.textContent!.trim()).toBe(
      'Genåbnes som ventende.',
    );

    for (const one of [toOpen, toWaiting]) {
      expect(one.querySelector('[data-testid="ado-already-imported"]')).toBeNull();
    }
  });

  /**
   * The two fields reconciliation made nullable. A key Azure DevOps does not know answered with
   * neither, and a line left blank cannot be told apart from one the screen forgot to fill in — so
   * both absences are said out loud. The answered row beside it is the other half: without it, a
   * template that had dropped the two lines altogether would pass.
   */
  it('should say out loud that the source reported no type and no state', async () => {
    const { element } = await preview({
      total: 1,
      rows: [
        row({ key: '15664' }),
        row({
          key: '17165',
          title: 'Den kilden ikke kender',
          state: null,
          workItemType: null,
          alreadyImported: true,
          reconciled: true,
          excluded: 'ado.missingInSource',
        }),
      ],
    });

    const [answered, unknown] = rows(element);

    expect(answered.querySelector('[data-testid="ado-type"]')!.textContent).toContain('Type: Bug');
    expect(answered.querySelector('[data-testid="ado-state"]')!.textContent).toContain(
      'Tilstand: Active',
    );
    expect(answered.querySelector('[data-testid="ado-no-type"]')).toBeNull();
    expect(answered.querySelector('[data-testid="ado-no-state"]')).toBeNull();

    expect(unknown.querySelector('[data-testid="ado-no-type"]')!.textContent!.trim()).toBe(
      'Azure DevOps oplyste ingen sagstype.',
    );
    expect(unknown.querySelector('[data-testid="ado-no-state"]')!.textContent!.trim()).toBe(
      'Azure DevOps oplyste ingen tilstand.',
    );
    expect(unknown.querySelector('[data-testid="ado-type"]')).toBeNull();
    expect(unknown.querySelector('[data-testid="ado-state"]')).toBeNull();
  });

  /**
   * The notice the server puts on the response when the reconciliation had nothing to say. It is not
   * folded into the error line: the preview did not fail, and the fetch's own rows are on screen and
   * worth keeping — which is the second assertion here.
   */
  it('should say why the reconciliation had nothing to say, and drop it on the next preview', async () => {
    const screen = await preview({
      total: 1,
      rows: [row({ key: '15664' })],
      reconciliationNotice: 'ado.noneRecognized',
    });

    expect(
      screen.element.querySelector('[data-testid="ado-reconciliation-notice"]')!.textContent,
    ).toContain('genkendte ingen af de opgaver');
    expect(rows(screen.element)).toHaveLength(1);
    expect(screen.element.querySelector('[data-testid="ado-import-error"]')).toBeNull();

    // Gone again on the next preview, which a one-shot fixture cannot see: a notice left standing
    // would explain a reconciliation that has since succeeded.
    screen.element.querySelector<HTMLButtonElement>('[data-testid="ado-preview"]')!.click();
    const again = await vi.waitFor(() => screen.http.expectOne('/api/ado/preview'));
    again.flush(new Blob([JSON.stringify({ total: 1, rows: [row({ key: '15664' })] })]));

    await vi.waitFor(() => {
      screen.fixture.detectChanges();
      expect(screen.element.querySelector('[data-testid="ado-reconciliation-notice"]')).toBeNull();
    });
  });

  /**
   * Where the notice sits, and the state that decides it. It stands above the branch choosing between
   * "nothing is assigned to you" and the list, because a refused reconciliation on top of an empty
   * fetch is exactly where it matters most — and a notice placed inside the list's branch would be
   * invisible in that state while looking right in every other one.
   *
   * The code is a plain refusal rather than noneRecognized on purpose: the reconciliation catches its
   * own SourceException and puts the code here, so ado.refused reaches this line as well.
   */
  it('should still explain a refused reconciliation when the fetch itself answered with nothing', async () => {
    const { element } = await preview({
      total: 0,
      rows: [],
      reconciliationNotice: 'ado.refused',
    });

    expect(element.querySelector('[data-testid="ado-none-assigned"]')).not.toBeNull();
    expect(
      element.querySelector('[data-testid="ado-reconciliation-notice"]')!.textContent,
    ).toContain('afviste tokenet');
  });
});
