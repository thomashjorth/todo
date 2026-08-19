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
  status: string;
  isWaiting: boolean;
  isDuty: boolean;
  waitingSince?: string;
  alreadyImported: boolean;
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

  it('should ask the shell to open an issue before it has been imported', async () => {
    const screen = await preview({
      total: 2,
      rows: [row({ key: 'SAAS-1' }), row({ key: 'SAAS-2', title: 'Den anden' })],
    });

    // The second row, not the first: opening the first one could pass against a screen that had
    // hard-wired one address for every row.
    const second = rows(screen.element)[1];
    const open = second.querySelector<HTMLButtonElement>('[data-testid="jira-open-issue"]')!;

    // A BUTTON, not an anchor: the Photino window has no address bar and no way back.
    expect(open.tagName).toBe('BUTTON');
    expect(open.textContent!.trim()).toBe('Åbn sagen');

    open.click();

    const request = await vi.waitFor(() => screen.http.expectOne('/api/system/open-link'));
    expect(JSON.parse(request.request.body).url).toBe('https://jira.example/browse/SAAS-2');
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
});
