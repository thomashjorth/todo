import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_BASE_URL } from '../api/todo-client';
import { SYSTEM_LANGUAGE } from '../i18n/system-language';
import { translocoTesting } from '../i18n/transloco.testing';
import { Settings } from './settings';
import { SettingsStore } from './settings-store';

interface Screen {
  fixture: ComponentFixture<Settings>;
  element: HTMLElement;
  http: HttpTestingController;
}

// The generated client requests responseType 'blob' and decodes it with FileReader,
// so a flushed response only reaches the template after a later microtask.
function settled<T>(screen: Screen, read: () => T): Promise<T> {
  return vi.waitFor(() => {
    screen.fixture.detectChanges();
    return read();
  });
}

/**
 * Everything the settings response carries besides the language, which the server always answers
 * with in full. Not `ISettingsResponse`: this is the wire body handed to `flush(...)`, so the type
 * checker never sees it, and a field the contract makes required has to be added here by hand.
 */
interface SettingsFixture {
  delegates?: string[];
  jiraBaseUrl?: string | null;
  jiraProjectKey?: string | null;
  jiraWaitingStatuses?: string[];
  jiraIncludeWaiting?: boolean;
  jiraDutyStatuses?: string[];
  jiraDoneStatuses?: string[];
  jiraOnDuty?: boolean;
  hasJiraToken?: boolean;
  adoBaseUrl?: string | null;
  adoProject?: string | null;
  adoWaitingStates?: string[];
  adoDoneStates?: string[];
  adoIncludeWaiting?: boolean;
  adoWorkItemTypes?: string[];
  adoDefaultDeadlineDays?: number;
  hasAdoToken?: boolean;
}

/**
 * What the server answers with when nobody has chosen otherwise. Named because it appears in every
 * expected request body too: the store sends the whole settings shape on every save, so a screen
 * that saved a language still carries the work item types it read.
 */
const defaultTypes = ['Bug', 'User Story', 'Task'];

function settingsJson(language: string | null, rest: SettingsFixture = {}): Blob {
  return new Blob([
    JSON.stringify({
      language,
      delegates: [],
      jiraBaseUrl: null,
      jiraProjectKey: null,
      jiraWaitingStatuses: [],
      jiraIncludeWaiting: false,
      jiraDutyStatuses: [],
      jiraDoneStatuses: [],
      jiraOnDuty: false,
      hasJiraToken: false,
      adoBaseUrl: null,
      adoProject: null,
      adoWaitingStates: [],
      adoDoneStates: [],
      adoIncludeWaiting: false,
      // The three default types, because an empty list is a shape the server cannot send: the read
      // layer answers the defaults for an absent row, and PUT refuses an empty list.
      adoWorkItemTypes: defaultTypes,
      adoDefaultDeadlineDays: 3,
      hasAdoToken: false,
      ...rest,
    }),
  ]);
}

function field(element: HTMLElement, testid: string): HTMLInputElement {
  return element.querySelector<HTMLInputElement>(`[data-testid="${testid}"]`)!;
}

function press(element: HTMLElement, testid: string): void {
  element.querySelector<HTMLButtonElement>(`[data-testid="${testid}"]`)!.click();
}

/** The five groups, named the way the heading button's id and test id are built from. */
type Section = 'language' | 'delegate' | 'jira' | 'ado' | 'retro';

/**
 * Unfolds one group, and folds whichever was open. Every control on this page now lives behind an
 * `@if`, so a lookup for anything but the five headings finds nothing until its group is open —
 * one helper rather than the same click spelled out at each of the ninety-odd lookups.
 */
function expand(screen: Screen, section: Section): void {
  press(screen.element, `${section}-section-toggle`);
  screen.fixture.detectChanges();
}

async function open(
  stored: string | null,
  aliases: string[] = [],
  rest: SettingsFixture = {},
  section: Section | null = null,
): Promise<Screen> {
  const http = TestBed.inject(HttpTestingController);

  const started = TestBed.inject(SettingsStore).start();
  http.expectOne('/api/settings').flush(settingsJson(stored, rest));
  await started;

  const fixture = TestBed.createComponent(Settings);
  const element = fixture.nativeElement as HTMLElement;
  fixture.detectChanges();
  http.expectOne('/api/retro/aliases').flush(new Blob([JSON.stringify({ aliases })]));

  const screen = { fixture, element, http };
  // Nothing is open on arrival, so a test that wants a field has to say which group holds it.
  if (section !== null) {
    expand(screen, section);
  }

  return screen;
}

function toggle(element: HTMLElement, section: Section): HTMLButtonElement {
  return element.querySelector<HTMLButtonElement>(`[data-testid="${section}-section-toggle"]`)!;
}

/**
 * The names of the groups whose heading says it is expanded. Names rather than a count, so a
 * failure says <em>which</em> group was open — "two were open" would not.
 */
function expandedSections(element: HTMLElement): string[] {
  return [...element.querySelectorAll<HTMLButtonElement>('[data-testid$="-section-toggle"]')]
    .filter((button) => button.getAttribute('aria-expanded') === 'true')
    .map((button) => button.id.replace('-section-toggle', ''));
}

/** The ids of the panels actually in the DOM, which is the other half of what aria-expanded says. */
function renderedPanels(element: HTMLElement): string[] {
  return [...element.querySelectorAll('[role="region"]')].map((panel) => panel.id);
}

function headingBecomes(screen: Screen, text: string): Promise<void> {
  return settled(screen, () =>
    expect(screen.element.querySelector('h2')!.textContent!.trim()).toBe(text),
  );
}

function select(element: HTMLElement): HTMLSelectElement {
  return element.querySelector<HTMLSelectElement>('[data-testid="language-select"]')!;
}

function choose(element: HTMLElement, value: string): void {
  const control = select(element);
  control.value = value;
  control.dispatchEvent(new Event('change'));
}

describe('Settings', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Settings, translocoTesting()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '' },
        { provide: SYSTEM_LANGUAGE, useValue: 'da-DK' },
      ],
    }).compileComponents();
  });

  it('should offer the system alongside both languages, and label the choice', async () => {
    const { element } = await open(null, [], {}, 'language');

    const label = element.querySelector<HTMLLabelElement>('label[for="settings-language"]')!;
    expect(label.textContent!.trim()).toBe('Sprog');
    expect(label.htmlFor).toBe(select(element).id);

    const options = [...select(element).options];
    expect(options.map((o) => o.value)).toEqual(['system', 'da', 'en']);
    expect(options.map((o) => o.textContent!.trim())).toEqual([
      'Følg systemet',
      'Dansk',
      'Engelsk',
    ]);
    expect(select(element).value).toBe('system');
  });

  it('should show the stored language as the one in force', async () => {
    const { element } = await open('en', [], {}, 'language');

    expect(select(element).value).toBe('en');
    expect(element.querySelector('h2')!.textContent!.trim()).toBe('Settings');
  });

  it('should store a chosen language and translate the page at once', async () => {
    const screen = await open(null, [], {}, 'language');

    choose(screen.element, 'en');

    const saved = screen.http.expectOne('/api/settings');
    expect(saved.request.method).toBe('PUT');
    expect(JSON.parse(saved.request.body)).toEqual({
      language: 'en',
      adoWorkItemTypes: defaultTypes,
    });
    saved.flush(settingsJson('en'));

    await headingBecomes(screen, 'Settings');
  });

  it('should clear the stored language when the system is chosen again', async () => {
    const screen = await open('en', [], {}, 'language');

    choose(screen.element, 'system');

    const saved = screen.http.expectOne('/api/settings');
    expect(JSON.parse(saved.request.body)).toEqual({ adoWorkItemTypes: defaultTypes });
    saved.flush(settingsJson(null));

    await headingBecomes(screen, 'Indstillinger');
    expect(select(screen.element).value).toBe('system');
  });

  // The five groups, and the order they are in: your own settings first, the sources last, because
  // the sources are set up once while the language and the delegates are what you touch. ADO sits
  // beside Jira because the two groups have the same shape, and the retro import is last because it
  // is not a connected source at all — no URL, no token, just a file.
  it('should hold the settings in five equal groups', async () => {
    const { element } = await open(null);

    const sections = [...element.querySelectorAll('section')];
    expect(sections.map((s) => s.getAttribute('data-testid'))).toEqual([
      'language-settings',
      'delegate-settings',
      'jira-settings',
      'ado-settings',
      'retro-settings',
    ]);

    const headings = sections.map((s) => s.querySelector('h3')!);
    // The heading's own text, not the button's: the chevron is a text node inside it too, so the
    // label is read off the span that carries the words.
    // "Generelt", not "Sprog": the group is general settings and the heading deliberately does not
    // name the one control it holds - which is also why the language select carries a visible label.
    expect(headings.map((h) => h.querySelector('span')!.textContent!.trim())).toEqual([
      'Generelt',
      'Uddelegering',
      'Jira-import',
      'ADO-import',
      'Retro-import',
    ]);

    // Equal groups look equal: one class list for all five, rather than three structures with
    // three levels, which is what the page had.
    expect([...new Set(headings.map((h) => h.className))]).toHaveLength(1);
    expect(element.querySelectorAll('h3')).toHaveLength(5);
  });

  // One group at a time, so each of these has to be unfolded before its subsections exist at all.
  it('should keep the lists a level down inside their own group', async () => {
    const screen = await open(null);

    // Subsections of their source, not groups beside it. Three under Jira — the waiting statuses,
    // the duty pool and the done statuses — and four under ADO: the waiting states, the done states,
    // the type filter and the day count. There is deliberately no duty list under ADO, because Azure
    // DevOps has no such pool.
    expand(screen, 'jira');
    expect(screen.element.querySelectorAll('h4')).toHaveLength(3);

    expand(screen, 'ado');
    expect(screen.element.querySelectorAll('h4')).toHaveLength(4);

    // The board question is the same — it belongs to the retro import, and only to it.
    expand(screen, 'retro');
    expect(screen.element.querySelector('h4')!.textContent!.trim()).toBe('Hvem er du på boardet?');
    expect(screen.element.querySelectorAll('h4')).toHaveLength(1);
  });

  // The whole point of the fold: five headings and nothing else, rather than one long heap. Nothing
  // open is the arrival state and a state a click can return to, as an accordion's own default has
  // it.
  it('should arrive with every group folded, and say so on each heading', async () => {
    const { element } = await open(null);

    expect(expandedSections(element)).toEqual([]);
    expect(renderedPanels(element)).toEqual([]);

    for (const section of ['language', 'delegate', 'jira', 'ado', 'retro'] as const) {
      const button = toggle(element, section);
      expect(button.getAttribute('aria-expanded')).toBe('false');
      expect(button.getAttribute('aria-controls')).toBe(`${section}-section-panel`);
      // Inside the heading, so the heading level survives being made clickable.
      expect(button.parentElement!.tagName).toBe('H3');
    }

    // Not a single field is in the DOM yet, which is what makes the guard on a colour inside a
    // panel honest: it cannot measure text that was never painted.
    expect(element.querySelector('[data-testid="language-select"]')).toBeNull();
    expect(element.querySelector('[data-testid="jira-base-url"]')).toBeNull();
  });

  it('should render only the open group, and point the panel back at its heading', async () => {
    const screen = await open(null, [], {}, 'jira');

    expect(expandedSections(screen.element)).toEqual(['jira']);
    expect(renderedPanels(screen.element)).toEqual(['jira-section-panel']);

    const panel = screen.element.querySelector('#jira-section-panel')!;
    expect(panel.getAttribute('role')).toBe('region');
    expect(panel.getAttribute('aria-labelledby')).toBe('jira-section-toggle');
    expect(toggle(screen.element, 'jira').id).toBe('jira-section-toggle');
    expect(panel.querySelector('[data-testid="jira-base-url"]')).not.toBeNull();
  });

  // Named after the rule, and asserted the way it can actually fail: a check that merely counted
  // the open ones would pass on nothing open at all, which is the state the page starts in.
  it('should open the group clicked and fold the one that was open', async () => {
    const screen = await open(null, [], {}, 'jira');
    expect(expandedSections(screen.element)).toEqual(['jira']);

    expand(screen, 'ado');

    expect(expandedSections(screen.element)).toEqual(['ado']);
    expect(renderedPanels(screen.element)).toEqual(['ado-section-panel']);
    expect(toggle(screen.element, 'jira').getAttribute('aria-expanded')).toBe('false');
    // The fields of the group that closed are gone, not merely hidden.
    expect(screen.element.querySelector('[data-testid="jira-base-url"]')).toBeNull();
    expect(screen.element.querySelector('[data-testid="ado-base-url"]')).not.toBeNull();
  });

  it('should fold a group again when its own heading is clicked', async () => {
    const screen = await open(null, [], {}, 'delegate');
    expect(expandedSections(screen.element)).toEqual(['delegate']);

    expand(screen, 'delegate');

    expect(expandedSections(screen.element)).toEqual([]);
    expect(renderedPanels(screen.element)).toEqual([]);
    expect(screen.element.querySelector('[data-testid="delegate-input"]')).toBeNull();
  });

  // The chevron says the direction to the eye and nothing to assistive tech, which already hears
  // "expanded" from aria-expanded. Without the attribute the character joins the button's
  // accessible name, and the suite matches those in full.
  it('should keep the chevron out of the heading button’s accessible name', async () => {
    const screen = await open(null, [], {}, 'jira');

    const chevrons = [...screen.element.querySelectorAll('[data-testid="section-chevron"]')];
    expect(chevrons).toHaveLength(5);
    expect(chevrons.every((c) => c.getAttribute('aria-hidden') === 'true')).toBe(true);

    // The open one points down, the folded ones sideways.
    expect(chevrons[2].textContent!.trim()).toBe('▾');
    expect(chevrons.filter((c) => c.textContent!.trim() === '▸')).toHaveLength(4);
  });

  it('should say that delegating is bookkeeping only, and that nobody is on the list yet', async () => {
    const { element } = await open(null, [], {}, 'delegate');

    // The one part of this that is a claim about what a user expects rather than about the code:
    // without the words, delegating reads as though the other person was told.
    const hint = element.querySelector('[data-testid="delegates-hint"]')!;
    expect(hint.textContent).toContain('ikke sendt en besked til den anden');
    expect(hint.textContent).toContain('skifter ikke assignee i Jira');

    expect(element.querySelector('[data-testid="delegates-empty"]')!.textContent).toContain(
      'Ingen på listen endnu',
    );
    expect(element.querySelectorAll('[data-testid="delegate-row"]')).toHaveLength(0);
  });

  it('should list the delegates with a labelled button for each', async () => {
    const screen = await open(
      null,
      [],
      { delegates: ['Mette Kirkegaard', 'Flemming'] },
      'delegate',
    );

    const rows = screen.element.querySelectorAll('[data-testid="delegate-row"]');
    expect(rows).toHaveLength(2);
    expect(screen.element.querySelector('[data-testid="delegates-empty"]')).toBeNull();

    const remove = rows[1].querySelector<HTMLButtonElement>('[data-testid="remove-delegate"]')!;
    expect(remove.getAttribute('aria-label')).toBe('Fjern Flemming');

    remove.click();

    const saved = screen.http.expectOne('/api/settings');
    expect(saved.request.method).toBe('PUT');
    expect(JSON.parse(saved.request.body)).toEqual({
      delegates: ['Mette Kirkegaard'],
      adoWorkItemTypes: defaultTypes,
    });
    saved.flush(settingsJson(null, { delegates: ['Mette Kirkegaard'] }));
  });

  it('should add a delegate on Enter, and send nothing for a blank or repeated name', async () => {
    const screen = await open(null, [], { delegates: ['Mette Kirkegaard'] }, 'delegate');

    const input = field(screen.element, 'delegate-input');
    input.value = '  Flemming  ';
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));

    const saved = screen.http.expectOne('/api/settings');
    expect(JSON.parse(saved.request.body)).toEqual({
      delegates: ['Mette Kirkegaard', 'Flemming'],
      adoWorkItemTypes: defaultTypes,
    });
    expect(input.value).toBe('');
    saved.flush(settingsJson(null, { delegates: ['Mette Kirkegaard', 'Flemming'] }));

    await settled(screen, () =>
      expect(screen.element.querySelectorAll('[data-testid="delegate-row"]')).toHaveLength(2),
    );

    for (const value of ['   ', 'Flemming']) {
      input.value = value;
      input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));
    }

    screen.http.verify();
    expect(input.value).toBe('Flemming');
  });

  // The repeat check on screen is exact, so a name differing only in case reaches the server — and
  // the reason has to land beside the list, not up beside the language select.
  it('should show the reason the server refused a delegate, in the delegate group', async () => {
    const screen = await open(null, [], { delegates: ['Mette'] }, 'delegate');

    const input = field(screen.element, 'delegate-input');
    input.value = 'mette';
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));

    screen.http
      .expectOne('/api/settings')
      .flush(
        new Blob([
          JSON.stringify({ code: 'settings.duplicateDelegate', message: 'Duplicate delegate.' }),
        ]),
        { status: 400, statusText: 'Bad Request' },
      );

    const error = await settled(screen, () => {
      const found = screen.element.querySelector(
        '[data-testid="delegate-settings"] [data-testid="delegates-error"]',
      );
      expect(found).not.toBeNull();
      return found!;
    });

    expect(error.textContent).toContain('Den samme person står på listen mere end én gang.');
    expect(screen.element.querySelector('[data-testid="settings-error"]')).toBeNull();
  });

  it('should list the aliases with a labelled button for each', async () => {
    const screen = await open(null, ['TH', 'Thomas Hjorth'], {}, 'retro');

    const rows = await settled(screen, () => {
      const found = screen.element.querySelectorAll('[data-testid="alias-row"]');
      expect(found).toHaveLength(2);
      return found;
    });

    const remove = rows[1].querySelector<HTMLButtonElement>('[data-testid="remove-alias"]')!;
    expect(remove.getAttribute('aria-label')).toBe('Fjern Thomas Hjorth');

    remove.click();

    const saved = screen.http.expectOne('/api/retro/aliases');
    expect(saved.request.method).toBe('PUT');
    expect(JSON.parse(saved.request.body)).toEqual({ aliases: ['TH'] });
  });

  it('should add an alias on Enter', async () => {
    const screen = await open(null, ['TH'], {}, 'retro');
    await settled(screen, () =>
      expect(screen.element.querySelectorAll('[data-testid="alias-row"]')).toHaveLength(1),
    );

    const input = screen.element.querySelector<HTMLInputElement>('[data-testid="alias-input"]')!;
    input.value = '  Mette Kirkegaard  ';
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));

    const saved = screen.http.expectOne('/api/retro/aliases');
    expect(JSON.parse(saved.request.body)).toEqual({ aliases: ['TH', 'Mette Kirkegaard'] });
    expect(input.value).toBe('');
  });

  it('should send no alias request for a blank or repeated name', async () => {
    const screen = await open(null, ['TH'], {}, 'retro');
    await settled(screen, () =>
      expect(screen.element.querySelectorAll('[data-testid="alias-row"]')).toHaveLength(1),
    );

    const input = screen.element.querySelector<HTMLInputElement>('[data-testid="alias-input"]')!;
    for (const value of ['   ', 'TH']) {
      input.value = value;
      input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));
    }

    screen.http.verify();
    expect(input.value).toBe('TH');
  });

  it('should show the reason the server rejected an alias list', async () => {
    const screen = await open(null, ['Thomas'], {}, 'retro');
    await settled(screen, () =>
      expect(screen.element.querySelectorAll('[data-testid="alias-row"]')).toHaveLength(1),
    );

    const input = screen.element.querySelector<HTMLInputElement>('[data-testid="alias-input"]')!;
    input.value = 'thomas';
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));

    screen.http
      .expectOne('/api/retro/aliases')
      .flush(
        new Blob([JSON.stringify({ code: 'retro.duplicateAlias', message: 'Duplicate alias.' })]),
        { status: 400, statusText: 'Bad Request' },
      );

    const error = await settled(screen, () => {
      const found = screen.element.querySelector('[data-testid="alias-error"]');
      expect(found).not.toBeNull();
      return found!;
    });

    expect(error.textContent).toContain('Det samme navn står på listen mere end én gang.');
  });

  it('should show the stored Jira settings in their fields', async () => {
    const screen = await open(
      null,
      [],
      {
        jiraBaseUrl: 'https://jira.test',
        jiraProjectKey: 'SAAS',
        jiraWaitingStatuses: ['Afventer general'],
        jiraIncludeWaiting: true,
        hasJiraToken: true,
      },
      'jira',
    );

    expect(field(screen.element, 'jira-base-url').value).toBe('https://jira.test');
    expect(field(screen.element, 'jira-project-key').value).toBe('SAAS');
    expect(field(screen.element, 'jira-include-waiting').checked).toBe(true);
    expect(screen.element.querySelector('[data-testid="jira-token-stored"]')).not.toBeNull();

    // A stored status is tickable even before the list has been fetched, or it could never be
    // unticked without a working connection.
    const rows = screen.element.querySelectorAll('[data-testid="jira-status-row"]');
    expect(rows).toHaveLength(1);
    expect(rows[0].querySelector<HTMLInputElement>('input')!.checked).toBe(true);
  });

  // The regression from the store's side, seen through the screen: the base URL is saved by the
  // same one path, so the language and the rest go with it.
  it('should keep the other settings when a base URL is typed', async () => {
    const screen = await open(
      'en',
      [],
      { jiraProjectKey: 'SAAS', jiraIncludeWaiting: true },
      'jira',
    );

    const input = field(screen.element, 'jira-base-url');
    input.value = 'https://jira.test';
    input.dispatchEvent(new Event('change'));

    const saved = screen.http.expectOne('/api/settings');
    expect(saved.request.method).toBe('PUT');
    expect(JSON.parse(saved.request.body)).toEqual({
      language: 'en',
      jiraBaseUrl: 'https://jira.test',
      jiraProjectKey: 'SAAS',
      jiraIncludeWaiting: true,
      adoWorkItemTypes: defaultTypes,
    });
    saved.flush(
      settingsJson('en', {
        jiraBaseUrl: 'https://jira.test',
        jiraProjectKey: 'SAAS',
        jiraIncludeWaiting: true,
      }),
    );
  });

  it('should empty the token field once the token is stored, and offer to clear it', async () => {
    const screen = await open(null, [], {}, 'jira');
    expect(screen.element.querySelector('[data-testid="jira-clear-token"]')).toBeNull();

    const input = field(screen.element, 'jira-token');
    expect(input.type).toBe('password');
    input.value = 'et-personligt-adgangstoken';
    input.dispatchEvent(new Event('input'));

    press(screen.element, 'jira-save-token');

    const saved = screen.http.expectOne('/api/settings/jira-token');
    expect(JSON.parse(saved.request.body)).toEqual({ token: 'et-personligt-adgangstoken' });
    saved.flush(settingsJson(null, { hasJiraToken: true }));

    await settled(screen, () =>
      expect(screen.element.querySelector('[data-testid="jira-clear-token"]')).not.toBeNull(),
    );
    expect(field(screen.element, 'jira-token').value).toBe('');

    press(screen.element, 'jira-clear-token');
    const cleared = screen.http.expectOne('/api/settings/jira-token');
    expect(cleared.request.method).toBe('DELETE');
    cleared.flush(settingsJson(null, { hasJiraToken: false }));
  });

  it('should keep a refused token in the field so it can be corrected', async () => {
    const screen = await open(null, [], {}, 'jira');

    const input = field(screen.element, 'jira-token');
    input.value = '   ';
    input.dispatchEvent(new Event('input'));
    press(screen.element, 'jira-save-token');

    screen.http
      .expectOne('/api/settings/jira-token')
      .flush(
        new Blob([JSON.stringify({ code: 'settings.emptyToken', message: 'A token cannot be' })]),
        { status: 400, statusText: 'Bad Request' },
      );

    const error = await settled(screen, () => {
      const found = screen.element.querySelector(
        '[data-testid="jira-settings"] [data-testid="jira-settings-error"]',
      );
      expect(found).not.toBeNull();
      return found!;
    });

    expect(error.textContent).toContain('Tokenet må ikke være tomt.');
    // Not up beside the language select, where SettingsStore.error is shown: with the language
    // group folded, a message written there would be one the user never sees.
    expect(screen.element.querySelector('[data-testid="settings-error"]')).toBeNull();
    expect(field(screen.element, 'jira-token').value).toBe('   ');
  });

  // Every Jira control on this page saves through the one settings route, and until the groups could
  // fold they all answered on the line above the language select. A 500 rather than a coded refusal
  // because PUT /api/settings validates no Jira field at all — the language, the delegates and the
  // two ADO settings are the four it checks — so a failed save is the only failure this path has.
  it('should show a failed Jira save in the Jira group', async () => {
    const screen = await open(null, [], {}, 'jira');

    const input = field(screen.element, 'jira-base-url');
    input.value = 'https://jira.test';
    input.dispatchEvent(new Event('change'));

    screen.http
      .expectOne('/api/settings')
      .flush(new Blob(['boom']), { status: 500, statusText: 'Server Error' });

    const error = await settled(screen, () => {
      const found = screen.element.querySelector(
        '[data-testid="jira-settings"] [data-testid="jira-settings-error"]',
      );
      expect(found).not.toBeNull();
      return found!;
    });

    expect(error.textContent).toContain('Noget gik galt.');
    expect(screen.element.querySelector('[data-testid="settings-error"]')).toBeNull();
  });

  it('should name whoever the token belongs to when the connection is tested', async () => {
    const screen = await open(null, [], {}, 'jira');
    expect(screen.element.querySelector('[data-testid="jira-connection"]')).toBeNull();

    press(screen.element, 'jira-test');
    screen.http
      .expectOne('/api/jira/test')
      .flush(new Blob([JSON.stringify({ displayName: 'Thomas Hjorth Hansen' })]));

    const name = await settled(screen, () => {
      const found = screen.element.querySelector('[data-testid="jira-connection"]');
      expect(found).not.toBeNull();
      return found!;
    });

    expect(name.textContent).toContain('Forbundet som Thomas Hjorth Hansen');
  });

  it('should say why the status list is empty rather than showing nothing', async () => {
    const screen = await open(null, [], {}, 'jira');

    expect(
      screen.element.querySelector('[data-testid="jira-statuses-empty"]')!.textContent,
    ).toContain('Virker forbindelsen ikke, kommer listen tom tilbage.');

    press(screen.element, 'jira-load-statuses');
    screen.http
      .expectOne('/api/jira/statuses')
      .flush(new Blob([JSON.stringify({ names: ['I gang', 'Afventer general'] })]));

    const rows = await settled(screen, () => {
      const found = screen.element.querySelectorAll('[data-testid="jira-status-row"]');
      expect(found).toHaveLength(2);
      return found;
    });

    rows[1].querySelector<HTMLInputElement>('input')!.click();

    const saved = screen.http.expectOne('/api/settings');
    expect(JSON.parse(saved.request.body)).toEqual({
      jiraWaitingStatuses: ['Afventer general'],
      adoWorkItemTypes: defaultTypes,
    });
    saved.flush(settingsJson(null, { jiraWaitingStatuses: ['Afventer general'] }));
  });

  it('should show the stored duty statuses and say what having the duty does', async () => {
    const screen = await open(
      null,
      [],
      {
        jiraDutyStatuses: ['Afventer general'],
        jiraOnDuty: true,
      },
      'jira',
    );

    expect(field(screen.element, 'jira-on-duty').checked).toBe(true);

    const rows = screen.element.querySelectorAll('[data-testid="jira-duty-status-row"]');
    expect(rows).toHaveLength(1);
    expect(rows[0].textContent).toContain('Afventer general');
    expect(rows[0].querySelector<HTMLInputElement>('input')!.checked).toBe(true);

    // A switch that only says "I am on duty" does not say that it also moves issues out of
    // "Waiting for", which is the half a user cannot see anywhere else.
    const hint = screen.element.querySelector('[data-testid="jira-on-duty-hint"]')!;
    expect(hint.textContent).toContain('også når de ikke er tildelt dig');
    expect(hint.textContent).toContain('handlingsklare frem for ventende');
  });

  // Duty overrides waiting, so the page has to ask in that order: read the other way round it
  // looks as though the waiting list wins.
  it('should ask about the duty pool after asking about waiting', async () => {
    const { element } = await open(
      null,
      [],
      {
        jiraWaitingStatuses: ['Afventer kunde'],
        jiraDutyStatuses: ['Afventer general'],
      },
      'jira',
    );

    const order = [...element.querySelectorAll('[data-testid]')].map((e) =>
      e.getAttribute('data-testid'),
    );

    for (const id of ['jira-status-row', 'jira-include-waiting', 'jira-duty-status-row']) {
      expect(order).toContain(id);
    }

    expect(order.indexOf('jira-status-row')).toBeLessThan(order.indexOf('jira-duty-status-row'));
    expect(order.indexOf('jira-include-waiting')).toBeLessThan(order.indexOf('jira-on-duty'));
  });

  it('should offer the one fetched status list for the duty pool too', async () => {
    const screen = await open(null, [], {}, 'jira');

    expect(
      screen.element.querySelector('[data-testid="jira-duty-statuses-empty"]')!.textContent,
    ).toContain('Hent statusserne for at vælge dem.');

    press(screen.element, 'jira-load-statuses');
    screen.http
      .expectOne('/api/jira/statuses')
      .flush(new Blob([JSON.stringify({ names: ['I gang', 'Afventer general'] })]));

    // One answer, both questions: a second call would be a second round trip for a list the
    // screen already has.
    const rows = await settled(screen, () => {
      const found = screen.element.querySelectorAll('[data-testid="jira-duty-status-row"]');
      expect(found).toHaveLength(2);
      return found;
    });
    screen.http.verify();

    rows[1].querySelector<HTMLInputElement>('input')!.click();

    const saved = screen.http.expectOne('/api/settings');
    // The waiting list is absent because it is empty, not because ticking a duty status cleared
    // it — the store sends every field on every save.
    expect(JSON.parse(saved.request.body)).toEqual({
      jiraDutyStatuses: ['Afventer general'],
      adoWorkItemTypes: defaultTypes,
    });
    saved.flush(settingsJson(null, { jiraDutyStatuses: ['Afventer general'] }));
  });

  it('should save the duty switch without disturbing the waiting list', async () => {
    const screen = await open(
      null,
      [],
      {
        jiraWaitingStatuses: ['Afventer general'],
        jiraDutyStatuses: ['Afventer general'],
      },
      'jira',
    );

    field(screen.element, 'jira-on-duty').click();

    const saved = screen.http.expectOne('/api/settings');
    expect(JSON.parse(saved.request.body)).toEqual({
      jiraWaitingStatuses: ['Afventer general'],
      jiraDutyStatuses: ['Afventer general'],
      jiraOnDuty: true,
      adoWorkItemTypes: defaultTypes,
    });
    saved.flush(
      settingsJson(null, {
        jiraWaitingStatuses: ['Afventer general'],
        jiraDutyStatuses: ['Afventer general'],
        jiraOnDuty: true,
      }),
    );

    await settled(screen, () => expect(field(screen.element, 'jira-on-duty').checked).toBe(true));
  });

  it('should show the stored Azure DevOps settings in their fields', async () => {
    const screen = await open(
      null,
      [],
      {
        adoBaseUrl: 'https://ado.test/Min%20Samling',
        adoProject: 'Saas',
        adoWaitingStates: ['Blocked'],
        adoIncludeWaiting: true,
        adoWorkItemTypes: ['Bug', 'Task'],
        adoDefaultDeadlineDays: 7,
        hasAdoToken: true,
      },
      'ado',
    );

    expect(field(screen.element, 'ado-base-url').value).toBe('https://ado.test/Min%20Samling');
    expect(field(screen.element, 'ado-project').value).toBe('Saas');
    expect(field(screen.element, 'ado-include-waiting').checked).toBe(true);
    expect(field(screen.element, 'ado-deadline-days').value).toBe('7');
    expect(screen.element.querySelector('[data-testid="ado-token-stored"]')).not.toBeNull();

    // A stored state is tickable before the list has been fetched, and it matters more here than it
    // does for Jira: the list comes off the user's own work items, so a state nothing is in today is
    // missing from the answer and could never be unticked again.
    const states = screen.element.querySelectorAll('[data-testid="ado-state-row"]');
    expect(states).toHaveLength(1);
    expect(states[0].querySelector<HTMLInputElement>('input')!.checked).toBe(true);

    const types = [...screen.element.querySelectorAll('[data-testid="ado-work-item-type-row"]')];
    expect(types.map((row) => row.querySelector('span')!.textContent!.trim())).toEqual([
      'Bug',
      'Task',
    ]);
    expect(
      types[1].querySelector('[data-testid="remove-work-item-type"]')!.getAttribute('aria-label'),
    ).toBe('Fjern Task');
  });

  it('should offer the states Azure DevOps answered with and save the ticked one', async () => {
    const screen = await open(null, [], {}, 'ado');

    expect(screen.element.querySelector('[data-testid="ado-states-empty"]')!.textContent).toContain(
      'Listen kommer af dine egne sager',
    );

    press(screen.element, 'ado-load-states');
    screen.http
      .expectOne('/api/ado/states')
      .flush(new Blob([JSON.stringify({ names: ['Active', 'Blocked'] })]));

    const rows = await settled(screen, () => {
      const found = screen.element.querySelectorAll('[data-testid="ado-state-row"]');
      expect(found).toHaveLength(2);
      return found;
    });

    rows[1].querySelector<HTMLInputElement>('input')!.click();

    const saved = screen.http.expectOne('/api/settings');
    expect(JSON.parse(saved.request.body)).toEqual({
      adoWaitingStates: ['Blocked'],
      adoWorkItemTypes: defaultTypes,
    });
    saved.flush(settingsJson(null, { adoWaitingStates: ['Blocked'] }));
  });

  it('should add a work item type on Enter, and send nothing for a blank or repeated name', async () => {
    const screen = await open(null, [], { adoWorkItemTypes: ['Bug'] }, 'ado');

    const input = field(screen.element, 'ado-work-item-type-input');
    input.value = '  Task  ';
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));

    const saved = screen.http.expectOne('/api/settings');
    expect(JSON.parse(saved.request.body)).toEqual({ adoWorkItemTypes: ['Bug', 'Task'] });
    expect(input.value).toBe('');
    saved.flush(settingsJson(null, { adoWorkItemTypes: ['Bug', 'Task'] }));

    await settled(screen, () =>
      expect(
        screen.element.querySelectorAll('[data-testid="ado-work-item-type-row"]'),
      ).toHaveLength(2),
    );

    for (const value of ['   ', 'Task']) {
      input.value = value;
      input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));
    }

    screen.http.verify();
    expect(input.value).toBe('Task');
  });

  // Taking the last type off the list is answered rather than undone. The screen deliberately has no
  // guard of its own: the alternative to the server's refusal is that the three defaults come back
  // without anybody saying so, which reads as the app having ignored the click.
  it('should show the refusal when the last work item type is removed, in the ADO group', async () => {
    const screen = await open(null, [], { adoWorkItemTypes: ['Bug'] }, 'ado');

    press(screen.element, 'remove-work-item-type');

    const saved = screen.http.expectOne('/api/settings');
    expect(JSON.parse(saved.request.body)).toEqual({ adoWorkItemTypes: [] });
    saved.flush(
      new Blob([
        JSON.stringify({
          code: 'ado.workItemTypesRequired',
          message: 'At least one work item type is required.',
        }),
      ]),
      { status: 400, statusText: 'Bad Request' },
    );

    const error = await settled(screen, () => {
      const found = screen.element.querySelector(
        '[data-testid="ado-settings"] [data-testid="ado-settings-error"]',
      );
      expect(found).not.toBeNull();
      return found!;
    });

    expect(error.textContent).toContain('Vælg mindst én sagstype.');
    // Not up beside the language select, which is where SettingsStore.error is shown.
    expect(screen.element.querySelector('[data-testid="settings-error"]')).toBeNull();
    expect(screen.element.querySelectorAll('[data-testid="ado-work-item-type-row"]')).toHaveLength(
      1,
    );
  });

  // Zero is the one value that means something of its own — no deadline — so it has to reach the
  // server, while an empty field is not a number of days and must not be read as zero.
  it('should save a day count of zero, and send nothing for an empty field', async () => {
    const screen = await open(null, [], {}, 'ado');

    const input = field(screen.element, 'ado-deadline-days');
    expect(input.value).toBe('3');

    input.value = '0';
    input.dispatchEvent(new Event('change'));

    const saved = screen.http.expectOne('/api/settings');
    expect(JSON.parse(saved.request.body)).toEqual({
      adoWorkItemTypes: defaultTypes,
      adoDefaultDeadlineDays: 0,
    });
    saved.flush(settingsJson(null, { adoDefaultDeadlineDays: 0 }));

    await settled(screen, () => expect(field(screen.element, 'ado-deadline-days').value).toBe('0'));

    input.value = '';
    input.dispatchEvent(new Event('change'));

    screen.http.verify();
  });

  it('should empty the Azure DevOps token field once it is stored, and offer to clear it', async () => {
    const screen = await open(null, [], {}, 'ado');
    expect(screen.element.querySelector('[data-testid="ado-clear-token"]')).toBeNull();

    const input = field(screen.element, 'ado-token');
    expect(input.type).toBe('password');
    input.value = 'et-personligt-adgangstoken';
    input.dispatchEvent(new Event('input'));

    press(screen.element, 'ado-save-token');

    // Its own route, so saving this one cannot be what clears the Jira token.
    const saved = screen.http.expectOne('/api/settings/ado-token');
    expect(saved.request.method).toBe('PUT');
    expect(JSON.parse(saved.request.body)).toEqual({ token: 'et-personligt-adgangstoken' });
    saved.flush(settingsJson(null, { hasAdoToken: true }));

    await settled(screen, () =>
      expect(screen.element.querySelector('[data-testid="ado-clear-token"]')).not.toBeNull(),
    );
    expect(field(screen.element, 'ado-token').value).toBe('');

    press(screen.element, 'ado-clear-token');
    const cleared = screen.http.expectOne('/api/settings/ado-token');
    expect(cleared.request.method).toBe('DELETE');
    cleared.flush(settingsJson(null, { hasAdoToken: false }));
  });

  it('should show a refused Azure DevOps token in the ADO group', async () => {
    const screen = await open(null, [], {}, 'ado');

    const input = field(screen.element, 'ado-token');
    input.value = '   ';
    input.dispatchEvent(new Event('input'));
    press(screen.element, 'ado-save-token');

    screen.http
      .expectOne('/api/settings/ado-token')
      .flush(
        new Blob([JSON.stringify({ code: 'settings.emptyToken', message: 'A token cannot be' })]),
        { status: 400, statusText: 'Bad Request' },
      );

    const error = await settled(screen, () => {
      const found = screen.element.querySelector('[data-testid="ado-settings-error"]');
      expect(found).not.toBeNull();
      return found!;
    });

    expect(error.textContent).toContain('Tokenet må ikke være tomt.');
    expect(screen.element.querySelector('[data-testid="settings-error"]')).toBeNull();
    expect(field(screen.element, 'ado-token').value).toBe('   ');
  });

  it('should name whoever the Azure DevOps token belongs to when the connection is tested', async () => {
    const screen = await open(null, [], {}, 'ado');
    expect(screen.element.querySelector('[data-testid="ado-connection"]')).toBeNull();

    press(screen.element, 'ado-test');
    screen.http
      .expectOne('/api/ado/test')
      .flush(new Blob([JSON.stringify({ displayName: 'Thomas Hjorth Hansen' })]));

    const name = await settled(screen, () => {
      const found = screen.element.querySelector('[data-testid="ado-connection"]');
      expect(found).not.toBeNull();
      return found!;
    });

    expect(name.textContent).toContain('Forbundet som Thomas Hjorth Hansen');
    // The Jira line is a different signal, so one connection cannot be shown as the other's.
    expect(screen.element.querySelector('[data-testid="jira-connection"]')).toBeNull();
  });

  // A user reported that Test connection "said nothing". Two different bugs produce that, and these
  // two tests are one each. First: the call succeeds but the server names nobody, which the screen
  // used to render as the empty sentence "Forbundet som ." - visible, but saying nothing.
  it('should say the server gave no name rather than showing an empty sentence', async () => {
    const screen = await open(null, [], {}, 'ado');

    press(screen.element, 'ado-test');
    screen.http.expectOne('/api/ado/test').flush(new Blob([JSON.stringify({ displayName: '' })]));

    const name = await settled(screen, () => {
      const found = screen.element.querySelector('[data-testid="ado-connection"]');
      expect(found).not.toBeNull();
      return found!;
    });

    expect(name.textContent).toContain('Forbundet, men serveren oplyste ikke noget navn.');
    expect(name.textContent).not.toContain('Forbundet som');
  });

  // And second: the call fails. The message was rendered at the foot of the section, over a hundred
  // lines below the button in a 480px column, so a refused token looked like a dead button. This
  // asserts the error is a sibling of the button's own row rather than merely present somewhere -
  // "present somewhere" was already true while the bug existed.
  // One test over both sections rather than one each: the fix was the same in both places, so a guard
  // that covers one of two identical shapes is the thin spot this repo keeps rediscovering.
  it.each([
    ['jira', 'jira.refused'],
    ['ado', 'ado.refused'],
  ])('should put a failed %s connection beside the button that caused it', async (source, code) => {
    const screen = await open(null, [], {}, source as Section);

    press(screen.element, `${source}-test`);
    screen.http
      .expectOne(`/api/${source}/test`)
      .flush(new Blob([JSON.stringify({ code, message: 'nej' })]), {
        status: 400,
        statusText: 'Bad Request',
      });

    const error = await settled(screen, () => {
      const found = screen.element.querySelector(`[data-testid="${source}-error"]`);
      expect(found).not.toBeNull();
      return found!;
    });

    const button = screen.element.querySelector(`[data-testid="${source}-test"]`)!;
    // The button sits in a row div; the error is that row's next sibling. Measuring the relationship
    // rather than a pixel distance, because a layout change should be free and a move should not.
    expect(button.parentElement!.nextElementSibling).toBe(error);
  });
});
