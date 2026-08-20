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
  jiraOnDuty?: boolean;
  hasJiraToken?: boolean;
}

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
      jiraOnDuty: false,
      hasJiraToken: false,
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

async function open(
  stored: string | null,
  aliases: string[] = [],
  rest: SettingsFixture = {},
): Promise<Screen> {
  const http = TestBed.inject(HttpTestingController);

  const started = TestBed.inject(SettingsStore).start();
  http.expectOne('/api/settings').flush(settingsJson(stored, rest));
  await started;

  const fixture = TestBed.createComponent(Settings);
  const element = fixture.nativeElement as HTMLElement;
  fixture.detectChanges();
  http.expectOne('/api/retro/aliases').flush(new Blob([JSON.stringify({ aliases })]));

  return { fixture, element, http };
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
    const { element } = await open(null);

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
    const { element } = await open('en');

    expect(select(element).value).toBe('en');
    expect(element.querySelector('h2')!.textContent!.trim()).toBe('Settings');
  });

  it('should store a chosen language and translate the page at once', async () => {
    const screen = await open(null);

    choose(screen.element, 'en');

    const saved = screen.http.expectOne('/api/settings');
    expect(saved.request.method).toBe('PUT');
    expect(JSON.parse(saved.request.body)).toEqual({ language: 'en' });
    saved.flush(settingsJson('en'));

    await headingBecomes(screen, 'Settings');
  });

  it('should clear the stored language when the system is chosen again', async () => {
    const screen = await open('en');

    choose(screen.element, 'system');

    const saved = screen.http.expectOne('/api/settings');
    expect(JSON.parse(saved.request.body)).toEqual({});
    saved.flush(settingsJson(null));

    await headingBecomes(screen, 'Indstillinger');
    expect(select(screen.element).value).toBe('system');
  });

  it('should list the aliases with a labelled button for each', async () => {
    const screen = await open(null, ['TH', 'Thomas Hjorth']);

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
    const screen = await open(null, ['TH']);
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
    const screen = await open(null, ['TH']);
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
    const screen = await open(null, ['Thomas']);
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
    const screen = await open(null, [], {
      jiraBaseUrl: 'https://jira.test',
      jiraProjectKey: 'SAAS',
      jiraWaitingStatuses: ['Afventer general'],
      jiraIncludeWaiting: true,
      hasJiraToken: true,
    });

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
    const screen = await open('en', [], { jiraProjectKey: 'SAAS', jiraIncludeWaiting: true });

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
    const screen = await open(null);
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
    const screen = await open(null);

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
      const found = screen.element.querySelector('[data-testid="settings-error"]');
      expect(found).not.toBeNull();
      return found!;
    });

    expect(error.textContent).toContain('Tokenet må ikke være tomt.');
    expect(field(screen.element, 'jira-token').value).toBe('   ');
  });

  it('should name whoever the token belongs to when the connection is tested', async () => {
    const screen = await open(null);
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
    const screen = await open(null);

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
    });
    saved.flush(settingsJson(null, { jiraWaitingStatuses: ['Afventer general'] }));
  });

  it('should show the stored duty statuses and say what having the duty does', async () => {
    const screen = await open(null, [], {
      jiraDutyStatuses: ['Afventer general'],
      jiraOnDuty: true,
    });

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
    const { element } = await open(null, [], {
      jiraWaitingStatuses: ['Afventer kunde'],
      jiraDutyStatuses: ['Afventer general'],
    });

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
    const screen = await open(null);

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
    expect(JSON.parse(saved.request.body)).toEqual({ jiraDutyStatuses: ['Afventer general'] });
    saved.flush(settingsJson(null, { jiraDutyStatuses: ['Afventer general'] }));
  });

  it('should save the duty switch without disturbing the waiting list', async () => {
    const screen = await open(null, [], {
      jiraWaitingStatuses: ['Afventer general'],
      jiraDutyStatuses: ['Afventer general'],
    });

    field(screen.element, 'jira-on-duty').click();

    const saved = screen.http.expectOne('/api/settings');
    expect(JSON.parse(saved.request.body)).toEqual({
      jiraWaitingStatuses: ['Afventer general'],
      jiraDutyStatuses: ['Afventer general'],
      jiraOnDuty: true,
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
});
