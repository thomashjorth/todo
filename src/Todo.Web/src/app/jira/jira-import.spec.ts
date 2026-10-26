import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_BASE_URL } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { SettingsStore } from '../settings/settings-store';
import { JiraImport } from './jira-import';

interface PreviewRowJson {
  key: string;
  title: string;
  url: string;
  note?: string;
  deadline?: string;
  requester?: string;
  // Nullable, because this interface describes the wire and reconciliation made the status so.
  status?: string | null;
  isWaiting: boolean;
  isDuty: boolean;
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

/**
 * The wire shape, not the generated class: this is what the server sends. <c>isDuty</c> is spelled
 * out here rather than left to the type, because this interface is its own shape — a field the
 * screen forgot to read would be a silently missing label, not a compiler error.
 */
function row(overrides: Partial<PreviewRowJson> = {}): PreviewRowJson {
  const merged: PreviewRowJson = {
    key: 'SAAS-1',
    title: 'Ret rapporten',
    url: '',
    status: 'I gang',
    isWaiting: false,
    isDuty: false,
    alreadyImported: false,
    suggestsClosing: false,
    suggestsReopening: false,
    // The ordinary row came from the fetch. The reconciled ones set it true, and the screen's
    // "showing N of total" line counts on the difference - total is the fetch's number alone.
    reconciled: false,
    ...overrides,
  };

  // Derived from whatever key won, so two rows in the same fixture do not share one URL — a test
  // that opens the second row would otherwise pass against the first row's address.
  return { ...merged, url: overrides.url ?? `https://jira.example/browse/${merged.key}` };
}

const waitingReason = 'Du venter på den, og ventende sager er slået fra.';

interface Screen {
  fixture: ComponentFixture<JiraImport>;
  element: HTMLElement;
  http: HttpTestingController;
}

/** Jira is set up, which is what puts the Load button on the screen at all. */
function configure(onDuty = false): void {
  const settings = TestBed.inject(SettingsStore);
  settings.jiraBaseUrl.set('https://jira.test');
  settings.jiraProjectKey.set('SAAS');
  settings.hasJiraToken.set(true);
  settings.jiraOnDuty.set(onDuty);
}

function open(): Screen {
  const fixture = TestBed.createComponent(JiraImport);
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

async function preview(body: unknown, onDuty = false): Promise<Screen> {
  configure(onDuty);
  const screen = open();

  screen.element.querySelector<HTMLButtonElement>('[data-testid="jira-preview"]')!.click();

  const request = await vi.waitFor(() => screen.http.expectOne('/api/jira/preview'));
  expect(request.request.method).toBe('POST');
  request.flush(new Blob([JSON.stringify(body)]));

  await settled(
    screen,
    '[data-testid="jira-showing"], [data-testid="jira-none-assigned"], [data-testid="jira-import-error"]',
  );
  return screen;
}

function rows(element: HTMLElement): HTMLElement[] {
  return [...element.querySelectorAll<HTMLElement>('[data-testid="jira-row"]')];
}

function checkboxes(element: HTMLElement): HTMLInputElement[] {
  return [...element.querySelectorAll<HTMLInputElement>('[data-testid="jira-row"] input')];
}

function importButton(element: HTMLElement): HTMLButtonElement {
  return element.querySelector<HTMLButtonElement>('[data-testid="jira-import"]')!;
}

describe('JiraImport', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [JiraImport, translocoTesting()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '' },
      ],
    }).compileComponents();
  });

  // A spy on window.open must not outlive the test that made it, failed or not.
  afterEach(() => vi.restoreAllMocks());

  it('should say that Jira is not set up and point at the settings page', () => {
    const { element, http } = open();

    expect(element.querySelector('[data-testid="jira-not-configured"]')!.textContent).toContain(
      'Jira er ikke sat op',
    );

    const link = element.querySelector<HTMLAnchorElement>('[data-testid="jira-settings-link"]')!;
    expect(link.getAttribute('href')).toBe('/settings');
    expect(link.textContent!.trim()).toBe('Sæt Jira op under Indstillinger');

    // Nothing to press, so nothing was asked of a Jira that cannot answer.
    expect(element.querySelector('[data-testid="jira-preview"]')).toBeNull();
    http.verify();
  });

  it('should keep an excluded issue on screen, switched off, with its reason', async () => {
    const { element } = await preview({
      total: 2,
      rows: [
        row({ key: 'SAAS-1' }),
        row({
          key: 'SAAS-2',
          title: 'Afvent svaret fra general',
          status: 'Afventer general',
          isWaiting: true,
          excluded: 'jira.excludedWaiting',
        }),
      ],
    });

    // Visible rather than hidden: a hidden row looks like an issue Jira lost, and it would make
    // the jiraIncludeWaiting setting invisible.
    expect(rows(element)).toHaveLength(2);
    expect(rows(element)[1].textContent).toContain('Afvent svaret fra general');
    expect(rows(element)[1].textContent).toContain('Status: Afventer general');

    expect(checkboxes(element).map((c) => c.disabled)).toEqual([false, true]);
    expect(checkboxes(element).map((c) => c.checked)).toEqual([true, false]);
    expect(element.querySelector('[data-testid="jira-excluded"]')!.textContent).toContain(
      waitingReason,
    );
    expect(importButton(element).textContent).toContain('Importér 1 sag');
  });

  it('should switch an issue imported before off too, and give a different reason', async () => {
    const { element } = await preview({
      total: 2,
      rows: [
        row({ key: 'SAAS-1' }),
        row({ key: 'SAAS-2', title: 'Skriv driftsvejledningen', alreadyImported: true }),
      ],
    });

    expect(rows(element)).toHaveLength(2);
    expect(rows(element)[1].textContent).toContain('Skriv driftsvejledningen');
    expect(checkboxes(element).map((c) => c.disabled)).toEqual([false, true]);

    const reason = element.querySelector('[data-testid="jira-already-imported"]')!;
    expect(reason.textContent).toContain('importeret tidligere');
    // Not the excluded reason: the two states have to be told apart on the screen.
    expect(reason.textContent).not.toContain(waitingReason);
    expect(element.querySelector('[data-testid="jira-excluded"]')).toBeNull();
  });

  it('should label the pool issue, and only it, without switching it off', async () => {
    // On the wire a duty row is told apart from an ordinary open one by isDuty alone: isWaiting is
    // false, excluded is absent and so is waitingSince. Both rows here are identical but for that
    // one field, so the label cannot be right by accident.
    const { element } = await preview(
      {
        total: 2,
        rows: [
          row({
            key: 'SAAS-1',
            title: 'Nulstil brugerens kodeord',
            status: 'Afventer general',
            isDuty: true,
          }),
          row({ key: 'SAAS-2', title: 'Min egen sag', status: 'Afventer general' }),
        ],
      },
      true,
    );

    const labels = [...element.querySelectorAll('[data-testid="jira-duty"]')];
    expect(labels).toHaveLength(1);
    expect(labels[0].textContent!.trim()).toBe('fra 2nd. level supporten');
    expect(rows(element)[0].contains(labels[0])).toBe(true);

    // Context, not a reason to skip: the pool's issues are exactly the ones the duty is for.
    expect(checkboxes(element).map((c) => c.disabled)).toEqual([false, false]);
    expect(checkboxes(element).map((c) => c.checked)).toEqual([true, true]);
    expect(element.querySelector('[data-testid="jira-excluded"]')).toBeNull();
  });

  it('should say that the duty is on, and say nothing when it is off', async () => {
    const on = await preview({ total: 1, rows: [row({ isDuty: true })] }, true);

    const notice = on.element.querySelector('[data-testid="jira-on-duty-notice"]');
    expect(notice).not.toBeNull();
    expect(notice!.textContent).toContain('Du har vagten — 2nd. level-sagerne er med.');

    // The same issues move silently into "Waiting for" when the switch is off, so the marker is
    // the only way the state can be seen at all.
    TestBed.inject(SettingsStore).jiraOnDuty.set(false);
    on.fixture.detectChanges();
    expect(on.element.querySelector('[data-testid="jira-on-duty-notice"]')).toBeNull();
  });

  it('should say which of the two reasons emptied the selection, and how many', async () => {
    const { element } = await preview({
      total: 3,
      rows: [
        row({ key: 'SAAS-1', excluded: 'jira.excludedWaiting' }),
        row({ key: 'SAAS-2', title: 'To', excluded: 'jira.excludedWaiting' }),
        row({ key: 'SAAS-3', title: 'Tre', alreadyImported: true }),
      ],
    });

    const summary = element.querySelector('[data-testid="jira-nothing-to-select"]')!;
    expect(summary.textContent).toContain('2 sager er udeladt af importen.');
    expect(summary.textContent).toContain('1 sag er importeret tidligere.');
    expect(importButton(element).disabled).toBe(true);
  });

  it('should call an empty answer an answer rather than an error', async () => {
    const { element } = await preview({ total: 0, rows: [] });

    expect(element.querySelector('[data-testid="jira-none-assigned"]')!.textContent).toContain(
      'Ingen sager er tildelt dig.',
    );
    expect(element.querySelector('[data-testid="jira-import-error"]')).toBeNull();
    expect(rows(element)).toHaveLength(0);
  });

  it('should show how many of Jiras total are on screen', async () => {
    const { element } = await preview({ total: 40, rows: [row(), row({ key: 'SAAS-2' })] });

    expect(element.querySelector('[data-testid="jira-showing"]')!.textContent).toContain(
      'Viser 2 af 40 sager.',
    );
  });

  /**
   * The twin of the ADO one, which carries the reasoning: `total` is the source's number for the
   * fetch alone, a reconciled row was never in it, and every assertion that predates this one runs
   * in a state where nothing has been reconciled - so `shown == total` held whichever number was used.
   */
  it('should count only the rows the fetch answered with, not the ones it went looking for', async () => {
    const { element } = await preview({
      total: 3,
      rows: [
        row({ key: 'SAAS-1' }),
        row({ key: 'SAAS-2', title: 'To' }),
        row({ key: 'SAAS-3', title: 'Tre' }),
        row({
          key: 'SAAS-9',
          title: 'Den der faldt ud af hentningen',
          status: 'Løst',
          alreadyImported: true,
          suggestsClosing: true,
          // Nothing here reads the label, so the reason is not what this test measures — it is here
          // because a closing row without one is a body the server cannot send, and since the chain
          // asks for the reason such a row would quietly render "importeret tidligere" instead. See
          // the ADO twin, where the tightening was measured.
          closureReason: 'resolved',
          reconciled: true,
        }),
      ],
    });

    expect(rows(element)).toHaveLength(4);
    expect(element.querySelector('[data-testid="jira-showing"]')!.textContent).toContain(
      'Viser 3 af 3 sager.',
    );
  });

  /**
   * The third set on screen, and the twin of the ADO one. The two rows beside it are the teeth and
   * they fail differently: the plain imported-before row would also pass a screen that stopped
   * blocking anything, and the unexplained row is imported before *and* reconciled, so a rule that
   * unblocked reconciled rows wholesale would let it through.
   */
  it('should offer an issue the source is not finished with for reopening, ticked', async () => {
    const { element } = await preview({
      // Two of the three rows came from the fetch; the unexplained one is reconciled.
      total: 2,
      rows: [
        row({
          key: 'SAAS-10',
          title: 'Den er ikke færdig endda',
          status: 'I gang',
          alreadyImported: true,
          suggestsReopening: true,
          reopenTo: 'open',
        }),
        row({ key: 'SAAS-2', title: 'Skriv driftsvejledningen', alreadyImported: true }),
        row({
          key: 'SAAS-11',
          title: 'Den Jira ikke kender',
          status: null,
          alreadyImported: true,
          reconciled: true,
          excluded: 'jira.missingInSource',
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
   * The button says what it does for each set it acts on - three sets, three clauses, counted from
   * the sets rather than from `total`. The twin of the ADO one.
   */
  it('should say what it does for each of the three sets it acts on', async () => {
    const screen = await preview({
      // Two, because two of the three rows came from the fetch - and deliberately not one, which is
      // what makes the three counts measurable: with `total: 1` a clause taken from `total` instead
      // of from its own set would print the same "1" and the assertion could not fail. See the ADO
      // twin, where that was measured.
      total: 2,
      rows: [
        row({ key: 'SAAS-1', title: 'Ret rapporten' }),
        row({
          key: 'SAAS-9',
          title: 'Ret regningen',
          status: 'Løst',
          alreadyImported: true,
          suggestsClosing: true,
          // Named rather than left out: the store's `hasReason` predicate drops a closure that does
          // not say why, where a `?? ClosureReason.Resolved` fallback used to relabel it.
          closureReason: 'resolved',
          doneAt: '2026-08-15T09:00:00Z',
        }),
        row({
          key: 'SAAS-10',
          title: 'Den er ikke færdig endda',
          status: 'I gang',
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

    const request = await vi.waitFor(() => screen.http.expectOne('/api/jira/import'));
    const body = JSON.parse(request.request.body);
    expect(body.rows.map((r: { key: string }) => r.key)).toEqual(['SAAS-1']);
    expect(body.closures.map((r: { key: string }) => r.key)).toEqual(['SAAS-9']);
    expect(body.reopenings).toEqual([{ key: 'SAAS-10', status: 'I gang' }]);

    request.flush(new Blob([JSON.stringify({ imported: 1, skipped: 0, closed: 1, reopened: 1 })]));
    const reload = await vi.waitFor(() => screen.http.expectOne('/api/jira/preview'));
    reload.flush(new Blob([JSON.stringify({ rows: [], total: 0 })]));

    const receipt = await settled(screen, '[data-testid="jira-receipt"]');

    // Three sentences, each only when its own number is not zero.
    expect(receipt.textContent).toContain(
      '1 importeret, 0 sprunget over. 1 opgave lukket. 1 opgave genåbnet.',
    );
  });

  it('should ask the shell to open an issue before it has been imported', async () => {
    const screen = await preview({
      total: 2,
      rows: [row({ key: 'SAAS-1' }), row({ key: 'SAAS-2', title: 'Den anden' })],
    });

    // The second row, not the first: opening the first one could pass against a screen that had
    // hard-wired one address for every row.
    const second = rows(screen.element)[1];
    const open = second.querySelector<HTMLButtonElement>('[data-testid="jira-open-issue"]')!;

    // A BUTTON, not an anchor: the app window has no address bar and no way back.
    expect(open.tagName).toBe('BUTTON');
    expect(open.textContent!.trim()).toBe('Åbn sagen');

    const opened = vi.spyOn(window, 'open').mockReturnValue(null);

    open.click();

    expect(opened).toHaveBeenCalledWith('https://jira.example/browse/SAAS-2', '_blank', 'noopener');
  });

  it('should show why Jira refused instead of a list', async () => {
    configure();
    const screen = open();

    screen.element.querySelector<HTMLButtonElement>('[data-testid="jira-preview"]')!.click();
    const request = await vi.waitFor(() => screen.http.expectOne('/api/jira/preview'));
    request.flush(new Blob([JSON.stringify({ code: 'jira.unreachable', message: 'Nope.' })]), {
      status: 400,
      statusText: 'Bad Request',
    });

    const error = await settled(screen, '[data-testid="jira-import-error"]');

    expect(error.textContent).toContain('Jira kunne ikke nås. Kontrollér basisURL og netværket.');
    expect(error.getAttribute('role')).toBe('alert');
    expect(rows(screen.element)).toHaveLength(0);
  });

  it('should send only the ticked issues, and no opinion about waiting', async () => {
    const screen = await preview({
      total: 3,
      rows: [
        row({
          key: 'SAAS-1',
          title: 'Ret rapporten',
          note: 'Se **bilaget**',
          deadline: '2026-08-24',
          requester: 'Mette Kirkegaard',
          status: 'Afventer general',
          isWaiting: true,
          waitingSince: '2026-08-14T08:00:00Z',
        }),
        row({ key: 'SAAS-2', title: 'Den jeg ikke valgte' }),
        row({ key: 'SAAS-3', title: 'Den udeladte', excluded: 'jira.excludedWaiting' }),
      ],
    });

    const [, second] = checkboxes(screen.element);
    second.checked = false;
    second.dispatchEvent(new Event('change', { bubbles: true }));
    screen.fixture.detectChanges();

    expect(importButton(screen.element).textContent).toContain('Importér 1 sag');
    importButton(screen.element).click();

    const request = screen.http.expectOne('/api/jira/import');
    const body = JSON.parse(request.request.body);
    expect(body).toEqual({
      rows: [
        {
          key: 'SAAS-1',
          title: 'Ret rapporten',
          note: 'Se **bilaget**',
          deadline: '2026-08-24',
          requester: 'Mette Kirkegaard',
          status: 'Afventer general',
          waitingSince: '2026-08-14T08:00:00Z',
        },
      ],
    });
    // The server looks the status up in the user's waiting list, so the client must not send its
    // own answer to that question.
    expect(Object.keys(body.rows[0])).not.toContain('isWaiting');
    request.flush(new Blob([JSON.stringify({ imported: 1, skipped: 0 })]));

    // Importing reloads, so a row that just became "imported before" says so without a click.
    const reloaded = await vi.waitFor(() => screen.http.expectOne('/api/jira/preview'));
    reloaded.flush(
      new Blob([
        JSON.stringify({
          total: 3,
          rows: [
            row({ key: 'SAAS-1', alreadyImported: true }),
            row({ key: 'SAAS-2', title: 'Den jeg ikke valgte' }),
            row({ key: 'SAAS-3', title: 'Den udeladte', excluded: 'jira.excludedWaiting' }),
          ],
        }),
      ]),
    );

    const receipt = await settled(screen, '[data-testid="jira-receipt"]');
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
   * Two sentences rather than one, and the twin of the ADO one. Both rows carry `suggestsClosing`, so
   * a chain reading that boolean alone would print one sentence over both. Asserted whole rather than
   * by a fragment: the resolved sentence is byte-identical to the one that predates the split, so a
   * `toContain` on "luk opgaven her" would pass on either.
   */
  it('should tell a resolved closure apart from one handed to somebody else', async () => {
    const { element } = await preview({
      total: 0,
      rows: [
        row({
          key: 'SAAS-9',
          title: 'Ret regningen',
          status: 'Løst',
          alreadyImported: true,
          suggestsClosing: true,
          closureReason: 'resolved',
          doneAt: '2026-08-15T09:00:00Z',
          reconciled: true,
        }),
        row({
          key: 'SAAS-12',
          title: 'Den en anden overtog',
          status: 'I gang',
          alreadyImported: true,
          suggestsClosing: true,
          closureReason: 'unassigned',
          reconciled: true,
        }),
      ],
    });

    const [resolved, handedOn] = rows(element);

    expect(
      resolved.querySelector('[data-testid="jira-suggests-closing"]')!.textContent!.trim(),
    ).toBe('Løst i Jira — luk opgaven her.');
    expect(
      handedOn.querySelector('[data-testid="jira-suggests-closing"]')!.textContent!.trim(),
    ).toBe('Tildelt en anden i Jira — luk opgaven her.');
  });

  /**
   * The reopening's label and the status it would restore. The contract puts `reopenTo` on the row so
   * the suggestion says what it will do, and nothing read it until here. Both rows are
   * `alreadyImported`, which is what gives the last assertion teeth.
   */
  it('should say a reopening is on offer and which status it would restore', async () => {
    const { element } = await preview({
      total: 2,
      rows: [
        row({
          key: 'SAAS-10',
          title: 'Den er ikke færdig endda',
          status: 'I gang',
          alreadyImported: true,
          suggestsReopening: true,
          reopenTo: 'open',
        }),
        row({
          key: 'SAAS-13',
          title: 'Den der venter på nogen',
          status: 'Afventer general',
          alreadyImported: true,
          suggestsReopening: true,
          reopenTo: 'waitingFor',
        }),
      ],
    });

    const [toOpen, toWaiting] = rows(element);

    expect(toOpen.querySelector('[data-testid="jira-suggests-reopening"]')!.textContent).toContain(
      'Ikke færdig i Jira',
    );

    // Two rows rather than one, differing only in `reopenTo`: with a single row a template that
    // hard-wired either sentence would pass.
    expect(toOpen.querySelector('[data-testid="jira-reopen-to"]')!.textContent!.trim()).toBe(
      'Genåbnes som åben.',
    );
    expect(toWaiting.querySelector('[data-testid="jira-reopen-to"]')!.textContent!.trim()).toBe(
      'Genåbnes som ventende.',
    );

    for (const one of [toOpen, toWaiting]) {
      expect(one.querySelector('[data-testid="jira-already-imported"]')).toBeNull();
    }
  });

  /**
   * The field reconciliation made nullable. Jira has one where Azure DevOps has two — there is no
   * type line here — and a status left blank cannot be told apart from one the screen forgot to fill
   * in. The answered row beside it is the other half: without it, a template that had dropped the
   * line altogether would pass.
   */
  it('should say out loud that the source reported no status', async () => {
    const { element } = await preview({
      total: 1,
      rows: [
        row({ key: 'SAAS-1' }),
        row({
          key: 'SAAS-11',
          title: 'Den Jira ikke kender',
          status: null,
          alreadyImported: true,
          reconciled: true,
          excluded: 'jira.missingInSource',
        }),
      ],
    });

    const [answered, unknown] = rows(element);

    expect(answered.querySelector('[data-testid="jira-status"]')!.textContent).toContain(
      'Status: I gang',
    );
    expect(answered.querySelector('[data-testid="jira-no-status"]')).toBeNull();

    expect(unknown.querySelector('[data-testid="jira-no-status"]')!.textContent!.trim()).toBe(
      'Jira oplyste ingen status.',
    );
    expect(unknown.querySelector('[data-testid="jira-status"]')).toBeNull();
  });

  /**
   * The notice the server puts on the response when the reconciliation had nothing to say. Not folded
   * into the error line: the preview did not fail, and the fetch's own rows are on screen and worth
   * keeping — which is the second assertion here.
   */
  it('should say why the reconciliation had nothing to say, and drop it on the next preview', async () => {
    const screen = await preview({
      total: 1,
      rows: [row({ key: 'SAAS-1' })],
      reconciliationNotice: 'jira.noneRecognized',
    });

    expect(
      screen.element.querySelector('[data-testid="jira-reconciliation-notice"]')!.textContent,
    ).toContain('genkendte ingen af de opgaver');
    expect(rows(screen.element)).toHaveLength(1);
    expect(screen.element.querySelector('[data-testid="jira-import-error"]')).toBeNull();

    // Gone again on the next preview, which a one-shot fixture cannot see: a notice left standing
    // would explain a reconciliation that has since succeeded.
    screen.element.querySelector<HTMLButtonElement>('[data-testid="jira-preview"]')!.click();
    const again = await vi.waitFor(() => screen.http.expectOne('/api/jira/preview'));
    again.flush(new Blob([JSON.stringify({ total: 1, rows: [row({ key: 'SAAS-1' })] })]));

    await vi.waitFor(() => {
      screen.fixture.detectChanges();
      expect(screen.element.querySelector('[data-testid="jira-reconciliation-notice"]')).toBeNull();
    });
  });

  /**
   * Where the notice sits. Above the branch choosing between "nothing is assigned to you" and the
   * list, because a refused reconciliation on top of an empty fetch is exactly where it matters most
   * — and a notice inside the list's branch would be invisible there while looking right everywhere
   * else. The code is a plain refusal rather than noneRecognized because the reconciliation catches
   * its own SourceException and puts the code here too.
   */
  it('should still explain a refused reconciliation when the fetch itself answered with nothing', async () => {
    const { element } = await preview({
      total: 0,
      rows: [],
      reconciliationNotice: 'jira.refused',
    });

    expect(element.querySelector('[data-testid="jira-none-assigned"]')).not.toBeNull();
    expect(
      element.querySelector('[data-testid="jira-reconciliation-notice"]')!.textContent,
    ).toContain('afviste tokenet');
  });
});
