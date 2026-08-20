import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { TranslocoService } from '@jsverse/transloco';
import { API_BASE_URL } from '../api/todo-client';
import { SYSTEM_LANGUAGE } from '../i18n/system-language';
import { translocoTesting } from '../i18n/transloco.testing';
import { SettingsStore } from './settings-store';

function configure(system: string): { store: SettingsStore; http: HttpTestingController } {
  TestBed.configureTestingModule({
    imports: [translocoTesting()],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: API_BASE_URL, useValue: '' },
      { provide: SYSTEM_LANGUAGE, useValue: system },
    ],
  });

  return { store: TestBed.inject(SettingsStore), http: TestBed.inject(HttpTestingController) };
}

/**
 * Every field of the settings response, because the contract makes six of them non-optional and
 * a fixture that leaves one out would hand the store an undefined the types say cannot happen.
 * The type checker cannot catch that here — this shape is not `ISettingsResponse` — so a field
 * added to the contract has to be added by hand.
 * Written as its own shape rather than as `Partial<ISettingsResponse>`: the generated interface
 * spells an absent language `undefined`, and the wire spells it `null`.
 */
interface SettingsJson {
  language?: string | null;
  delegates?: string[];
  jiraBaseUrl?: string | null;
  jiraProjectKey?: string | null;
  jiraWaitingStatuses?: string[];
  jiraIncludeWaiting?: boolean;
  jiraDutyStatuses?: string[];
  jiraOnDuty?: boolean;
  hasJiraToken?: boolean;
}

function settingsJson(overrides: SettingsJson = {}): Blob {
  return new Blob([
    JSON.stringify({
      language: null,
      delegates: [],
      jiraBaseUrl: null,
      jiraProjectKey: null,
      jiraWaitingStatuses: [],
      jiraIncludeWaiting: false,
      jiraDutyStatuses: [],
      jiraOnDuty: false,
      hasJiraToken: false,
      ...overrides,
    }),
  ]);
}

function activeLang(): string {
  return TestBed.inject(TranslocoService).getActiveLang();
}

describe('SettingsStore', () => {
  it('should show the app in the stored language', async () => {
    const { store, http } = configure('da-DK');

    const started = store.start();
    http.expectOne('/api/settings').flush(settingsJson({ language: 'en' }));
    await started;

    expect(store.language()).toBe('en');
    expect(activeLang()).toBe('en');
    expect(document.documentElement.lang).toBe('en');
  });

  it('should follow the system language when nothing is stored', async () => {
    const { store, http } = configure('da-DK');

    const started = store.start();
    http.expectOne('/api/settings').flush(settingsJson({ language: null }));
    await started;

    expect(store.language()).toBeNull();
    expect(activeLang()).toBe('da');
    expect(document.documentElement.lang).toBe('da');
  });

  // An app that refuses to open because it could not read a setting is worse than a translated one.
  it('should start in the system language when the settings cannot be read', async () => {
    const { store, http } = configure('en-GB');

    const started = store.start();
    http
      .expectOne('/api/settings')
      .flush(new Blob(['boom']), { status: 500, statusText: 'Server Error' });
    await started;

    expect(store.language()).toBeNull();
    expect(activeLang()).toBe('en');
  });

  it('should store a chosen language and switch to it at once', async () => {
    const { store, http } = configure('da-DK');

    const chosen = store.choose('en');
    const request = http.expectOne('/api/settings');
    expect(request.request.method).toBe('PUT');
    expect(JSON.parse(request.request.body)).toEqual({ language: 'en' });
    request.flush(settingsJson({ language: 'en' }));
    await chosen;

    expect(store.language()).toBe('en');
    expect(activeLang()).toBe('en');
  });

  // The API only accepts 'da', 'en' or nothing, so a browser locale must never reach it.
  it('should send no language at all for following the system', async () => {
    const { store, http } = configure('da-DK');

    const chosen = store.choose(null);
    const request = http.expectOne('/api/settings');
    expect(JSON.parse(request.request.body)).toEqual({});
    request.flush(settingsJson({ language: null }));
    await chosen;

    expect(store.language()).toBeNull();
    expect(activeLang()).toBe('da');
  });

  it('should show the reason the server rejected a language', async () => {
    const { store, http } = configure('da-DK');

    const chosen = store.choose('en');
    http.expectOne('/api/settings').flush(
      new Blob([
        JSON.stringify({
          code: 'settings.unknownLanguage',
          message: "'en' is not a supported language.",
        }),
      ]),
      { status: 400, statusText: 'Bad Request' },
    );
    await chosen;

    expect(store.error()).toBe('Sproget understøttes ikke.');
    expect(activeLang()).toBe('da');
  });

  it('should read every Jira setting the server answers with', async () => {
    const { store, http } = configure('da-DK');

    const started = store.start();
    http.expectOne('/api/settings').flush(
      settingsJson({
        jiraBaseUrl: 'https://jira.test',
        jiraProjectKey: 'SAAS',
        jiraWaitingStatuses: ['Afventer general', 'Afventer kunde'],
        jiraIncludeWaiting: true,
        jiraDutyStatuses: ['Afventer general'],
        jiraOnDuty: true,
        hasJiraToken: true,
      }),
    );
    await started;

    expect(store.jiraBaseUrl()).toBe('https://jira.test');
    expect(store.jiraProjectKey()).toBe('SAAS');
    expect(store.jiraWaitingStatuses()).toEqual(['Afventer general', 'Afventer kunde']);
    expect(store.jiraIncludeWaiting()).toBe(true);
    expect(store.jiraDutyStatuses()).toEqual(['Afventer general']);
    expect(store.jiraOnDuty()).toBe(true);
    expect(store.hasJiraToken()).toBe(true);
  });

  it('should keep every setting in the request so saving one does not clear another', async () => {
    // The backend reads an absent field as "clear". SettingsStore.save must therefore carry all
    // eight fields, exactly as TaskStore.update has to — slice 9 lost a stored DeferUntil to this.
    // The duty pair and the delegates are in here rather than in tests of their own: two tests each
    // asserting half of the request would both pass while the other half was dropped.
    const { store, http } = configure('da-DK');

    store.delegates.set(['Mette Kirkegaard']);
    store.jiraBaseUrl.set('https://jira.test');
    store.jiraProjectKey.set('SAAS');
    store.jiraWaitingStatuses.set(['Afventer general']);
    store.jiraIncludeWaiting.set(true);
    store.jiraDutyStatuses.set(['Afventer general', 'Afventer 2nd level']);
    store.jiraOnDuty.set(true);

    const saved = store.save({ language: 'en' });

    const request = http.expectOne('/api/settings');
    expect(request.request.method).toBe('PUT');
    expect(JSON.parse(request.request.body)).toEqual({
      language: 'en',
      delegates: ['Mette Kirkegaard'],
      jiraBaseUrl: 'https://jira.test',
      jiraProjectKey: 'SAAS',
      jiraWaitingStatuses: ['Afventer general'],
      jiraIncludeWaiting: true,
      jiraDutyStatuses: ['Afventer general', 'Afventer 2nd level'],
      jiraOnDuty: true,
    });
    request.flush(
      settingsJson({
        language: 'en',
        delegates: ['Mette Kirkegaard'],
        jiraBaseUrl: 'https://jira.test',
        jiraProjectKey: 'SAAS',
        jiraWaitingStatuses: ['Afventer general'],
        jiraIncludeWaiting: true,
        jiraDutyStatuses: ['Afventer general', 'Afventer 2nd level'],
        jiraOnDuty: true,
      }),
    );
    await saved;

    expect(store.jiraProjectKey()).toBe('SAAS');
    expect(store.delegates()).toEqual(['Mette Kirkegaard']);
    expect(store.jiraDutyStatuses()).toEqual(['Afventer general', 'Afventer 2nd level']);
    expect(store.jiraOnDuty()).toBe(true);
  });

  it('should send the whole delegate list and take the server list back', async () => {
    const { store, http } = configure('da-DK');
    store.delegates.set(['Mette Kirkegaard']);

    const saved = store.saveDelegates(['Mette Kirkegaard', 'Flemming']);

    const request = http.expectOne('/api/settings');
    expect(request.request.method).toBe('PUT');
    expect(JSON.parse(request.request.body)).toEqual({
      delegates: ['Mette Kirkegaard', 'Flemming'],
    });
    // The server trims and dedupes, so the reply is the authority on what the list now is — here
    // it answers with a spelling the request did not carry.
    request.flush(settingsJson({ delegates: ['Mette Kirkegaard', 'Flemming Sørensen'] }));
    await saved;

    expect(store.delegates()).toEqual(['Mette Kirkegaard', 'Flemming Sørensen']);
    expect(store.delegatesError()).toBeNull();
  });

  // Two lines on the page, so two signals: one shown beside the language select and one beside the
  // delegate list. Sharing `error` would print a refused name in both places at once.
  it('should say why a delegate was refused without touching the settings error', async () => {
    const { store, http } = configure('da-DK');
    store.delegates.set(['Mette']);

    const saved = store.saveDelegates(['Mette', 'mette']);
    http.expectOne('/api/settings').flush(
      new Blob([
        JSON.stringify({
          code: 'settings.duplicateDelegate',
          message: 'Duplicate delegate.',
        }),
      ]),
      { status: 400, statusText: 'Bad Request' },
    );
    await saved;

    expect(store.delegatesError()).toBe('Den samme person står på listen mere end én gang.');
    expect(store.error()).toBeNull();
    expect(store.delegates()).toEqual(['Mette']);
  });

  // The same guard from the other side: a language chosen through the select must not take the
  // Jira settings with it, so `choose` has to be the same one path as `save`.
  it('should keep the Jira settings when only the language changes', async () => {
    const { store, http } = configure('da-DK');

    store.jiraBaseUrl.set('https://jira.test');
    store.jiraProjectKey.set('SAAS');

    const chosen = store.choose('en');

    const request = http.expectOne('/api/settings');
    expect(JSON.parse(request.request.body)).toEqual({
      language: 'en',
      jiraBaseUrl: 'https://jira.test',
      jiraProjectKey: 'SAAS',
    });
    request.flush(
      settingsJson({ language: 'en', jiraBaseUrl: 'https://jira.test', jiraProjectKey: 'SAAS' }),
    );
    await chosen;
  });

  it('should store a token and report that there is one, without echoing it', async () => {
    const { store, http } = configure('da-DK');

    const saved = store.setToken('  et-personligt-adgangstoken  ');

    const request = http.expectOne('/api/settings/jira-token');
    expect(request.request.method).toBe('PUT');
    expect(JSON.parse(request.request.body)).toEqual({ token: '  et-personligt-adgangstoken  ' });
    request.flush(settingsJson({ hasJiraToken: true }));

    expect(await saved).toBe(true);
    expect(store.hasJiraToken()).toBe(true);
  });

  it('should say why a blank token was refused, and report the failure to its caller', async () => {
    const { store, http } = configure('da-DK');

    const saved = store.setToken('   ');
    http
      .expectOne('/api/settings/jira-token')
      .flush(
        new Blob([JSON.stringify({ code: 'settings.emptyToken', message: 'A token cannot be' })]),
        { status: 400, statusText: 'Bad Request' },
      );

    expect(await saved).toBe(false);
    expect(store.error()).toBe('Tokenet må ikke være tomt.');
    expect(store.hasJiraToken()).toBe(false);
  });

  it('should forget a stored token', async () => {
    const { store, http } = configure('da-DK');
    store.hasJiraToken.set(true);

    const cleared = store.clearToken();
    const request = http.expectOne('/api/settings/jira-token');
    expect(request.request.method).toBe('DELETE');
    request.flush(settingsJson({ hasJiraToken: false }));
    await cleared;

    expect(store.hasJiraToken()).toBe(false);
  });

  it('should never hold the token in a signal', async () => {
    // The token is write-only: it goes out through setJiraToken and comes back only as
    // hasJiraToken. A signal holding it would put it in a component's template scope, and it
    // would survive navigating away from the settings page.
    const { store, http } = configure('da-DK');
    const secret = 'det-her-maa-ikke-blive-liggende';

    const saved = store.setToken(secret);
    http.expectOne('/api/settings/jira-token').flush(settingsJson({ hasJiraToken: true }));
    await saved;

    // Names first, so a field called `jiraToken` is caught even before anything is written to it.
    const own = Object.keys(store);
    expect(own.filter((key) => /token/i.test(key))).toEqual(['hasJiraToken']);

    // And then the values, because the name guard on its own is dodged by calling the field
    // something else. Methods live on the prototype, so the only own properties that are
    // functions here are the signals — calling them has no side effect.
    const holding = Object.entries(store as unknown as Record<string, unknown>)
      .filter(([, value]) => typeof value === 'function')
      .filter(([, value]) => JSON.stringify((value as () => unknown)() ?? null).includes(secret))
      .map(([key]) => key);

    expect(holding).toEqual([]);
  });
});
