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
 * Every field of the settings response, because the contract makes eleven of them non-optional and
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
  adoBaseUrl?: string | null;
  adoProject?: string | null;
  adoWaitingStates?: string[];
  adoIncludeWaiting?: boolean;
  adoWorkItemTypes?: string[];
  adoDefaultDeadlineDays?: number;
  hasAdoToken?: boolean;
  autostart?: boolean;
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
      adoBaseUrl: null,
      adoProject: null,
      adoWaitingStates: [],
      adoIncludeWaiting: false,
      // Never empty on the wire: the read layer answers the three defaults for an absent row, and
      // PUT refuses an empty list rather than storing one, so `[]` is a shape the server cannot send.
      adoWorkItemTypes: ['Bug', 'User Story', 'Task'],
      adoDefaultDeadlineDays: 3,
      hasAdoToken: false,
      autostart: false,
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
    // fourteen fields, exactly as TaskStore.update has to — slice 9 lost a stored DeferUntil to
    // this. Fourteen and not fifteen: neither token is a field on this route, because a full
    // replacement would wipe it on every other change.
    // Every pair is in here rather than in tests of their own: two tests each asserting half of the
    // request would both pass while the other half was dropped.
    const { store, http } = configure('da-DK');

    store.delegates.set(['Mette Kirkegaard']);
    store.jiraBaseUrl.set('https://jira.test');
    store.jiraProjectKey.set('SAAS');
    store.jiraWaitingStatuses.set(['Afventer general']);
    store.jiraIncludeWaiting.set(true);
    store.jiraDutyStatuses.set(['Afventer general', 'Afventer 2nd level']);
    store.jiraOnDuty.set(true);
    store.adoBaseUrl.set('https://ado.test/Min%20Samling');
    store.adoProject.set('Saas');
    store.adoWaitingStates.set(['Blocked', 'PO Review']);
    store.adoIncludeWaiting.set(true);
    store.adoWorkItemTypes.set(['Bug']);
    // Seven rather than three, because three is the contract's default and the wire spells the
    // default as an absent key — a field left at its default could not show that it was carried.
    store.adoDefaultDeadlineDays.set(7);

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
      adoBaseUrl: 'https://ado.test/Min%20Samling',
      adoProject: 'Saas',
      adoWaitingStates: ['Blocked', 'PO Review'],
      adoIncludeWaiting: true,
      adoWorkItemTypes: ['Bug'],
      adoDefaultDeadlineDays: 7,
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
        adoBaseUrl: 'https://ado.test/Min%20Samling',
        adoProject: 'Saas',
        adoWaitingStates: ['Blocked', 'PO Review'],
        adoIncludeWaiting: true,
        adoWorkItemTypes: ['Bug'],
        adoDefaultDeadlineDays: 7,
      }),
    );
    await saved;

    expect(store.jiraProjectKey()).toBe('SAAS');
    expect(store.delegates()).toEqual(['Mette Kirkegaard']);
    expect(store.jiraDutyStatuses()).toEqual(['Afventer general', 'Afventer 2nd level']);
    expect(store.jiraOnDuty()).toBe(true);
    expect(store.adoProject()).toBe('Saas');
    expect(store.adoWaitingStates()).toEqual(['Blocked', 'PO Review']);
    expect(store.adoWorkItemTypes()).toEqual(['Bug']);
    expect(store.adoDefaultDeadlineDays()).toBe(7);
  });

  it('should read every Azure DevOps setting the server answers with', async () => {
    const { store, http } = configure('da-DK');

    const started = store.start();
    http.expectOne('/api/settings').flush(
      settingsJson({
        adoBaseUrl: 'https://ado.test/Min%20Samling',
        adoProject: 'Saas',
        adoWaitingStates: ['Blocked'],
        adoIncludeWaiting: true,
        adoWorkItemTypes: ['Bug', 'Task'],
        adoDefaultDeadlineDays: 0,
        hasAdoToken: true,
      }),
    );
    await started;

    expect(store.adoBaseUrl()).toBe('https://ado.test/Min%20Samling');
    expect(store.adoProject()).toBe('Saas');
    expect(store.adoWaitingStates()).toEqual(['Blocked']);
    expect(store.adoIncludeWaiting()).toBe(true);
    expect(store.adoWorkItemTypes()).toEqual(['Bug', 'Task']);
    expect(store.adoDefaultDeadlineDays()).toBe(0);
    expect(store.hasAdoToken()).toBe(true);
  });

  // The one field where the cleared value is not the falsy one: 0 means "no deadline", and any
  // truthiness check on the way out would drop it and let the server bind its default of 3.
  it('should send a deliberate zero rather than dropping it', async () => {
    const { store, http } = configure('da-DK');
    store.adoDefaultDeadlineDays.set(0);

    const saved = store.saveAdo({ adoDefaultDeadlineDays: 0 });

    const request = http.expectOne('/api/settings');
    expect(JSON.parse(request.request.body)).toEqual({ adoDefaultDeadlineDays: 0 });
    request.flush(settingsJson({ adoDefaultDeadlineDays: 0 }));
    await saved;

    expect(store.adoDefaultDeadlineDays()).toBe(0);
  });

  // And the other half of that rule: the default is spelled as an absent key, so a save that leaves
  // the number where it is cannot be read as having asked for anything.
  it('should leave the day count out when it is the default', async () => {
    const { store, http } = configure('da-DK');
    store.adoDefaultDeadlineDays.set(3);

    const saved = store.save({ language: 'en' });

    const request = http.expectOne('/api/settings');
    expect(Object.keys(JSON.parse(request.request.body))).toEqual(['language']);
    request.flush(settingsJson({ language: 'en' }));
    await saved;
  });

  // Emptying the type list has to reach the server, which refuses it — the alternative is that the
  // three defaults come back without anybody saying so, which looks like the app undid the click.
  it('should send an emptied work item type list so the server can refuse it', async () => {
    const { store, http } = configure('da-DK');
    store.adoWorkItemTypes.set(['Bug']);

    const saved = store.saveAdo({ adoWorkItemTypes: [] });

    const request = http.expectOne('/api/settings');
    expect(JSON.parse(request.request.body)).toEqual({ adoWorkItemTypes: [] });
    request.flush(
      new Blob([
        JSON.stringify({
          code: 'ado.workItemTypesRequired',
          message: 'At least one work item type is required.',
        }),
      ]),
      { status: 400, statusText: 'Bad Request' },
    );
    await saved;

    expect(store.adoError()).toBe('Vælg mindst én sagstype. En tom liste betyder ikke alle typer.');
    expect(store.error()).toBeNull();
    expect(store.adoWorkItemTypes()).toEqual(['Bug']);
  });

  // The other side of the same rule: before the first read the list is empty because nothing has
  // been read, not because anybody emptied it, so a language change must not be refused by it.
  it('should carry no opinion about the types when it has read none', async () => {
    const { store, http } = configure('da-DK');

    const chosen = store.choose('en');

    const request = http.expectOne('/api/settings');
    expect(Object.keys(JSON.parse(request.request.body))).toEqual(['language']);
    request.flush(settingsJson({ language: 'en' }));
    await chosen;
  });

  // A failed save of a Jira setting answers in the Jira group's line, not in the language group's.
  // A 500 rather than a coded refusal, and that is not laziness: PUT /api/settings validates four
  // things — the language, the delegate list, the ADO work item types and the ADO day count — and
  // not one Jira field, so a transport failure is the only way this path fails today. The Jira
  // group's coded refusals come from its token route, which is tested below.
  it('should say a Jira setting could not be saved without touching the settings error', async () => {
    const { store, http } = configure('da-DK');

    const saved = store.saveJira({ jiraBaseUrl: 'https://jira.test' });
    http
      .expectOne('/api/settings')
      .flush(new Blob(['boom']), { status: 500, statusText: 'Server Error' });
    await saved;

    expect(store.jiraError()).toBe('Noget gik galt. Prøv igen.');
    expect(store.error()).toBeNull();
    expect(store.adoError()).toBeNull();
  });

  it('should say why Azure DevOps refused a setting without touching the settings error', async () => {
    const { store, http } = configure('da-DK');

    const saved = store.saveAdo({ adoDefaultDeadlineDays: 400 });
    http.expectOne('/api/settings').flush(
      new Blob([
        JSON.stringify({
          code: 'ado.defaultDeadlineDaysInvalid',
          message: 'A deadline of 400 days ahead is outside 0-365.',
        }),
      ]),
      { status: 400, statusText: 'Bad Request' },
    );
    await saved;

    expect(store.adoError()).toBe(
      'Antallet af dage skal være mellem 0 og 365. Nul betyder ingen deadline.',
    );
    expect(store.error()).toBeNull();
    expect(store.delegatesError()).toBeNull();
  });

  // Its own route for the same reason the tokens have one, and this pair is the guard on that: a
  // field on PUT /api/settings would be read as "clear" by every other save.
  it('should turn autostart on through its own route', async () => {
    const { store, http } = configure('da-DK');

    const saved = store.setAutostart(true);

    const request = http.expectOne('/api/settings/autostart');
    expect(request.request.method).toBe('PUT');
    // No body. The verb says it, so there is nothing to get wrong in a payload.
    expect(request.request.body).toBeNull();
    request.flush(settingsJson({ autostart: true }));

    await saved;

    expect(store.autostart()).toBe(true);
  });

  it('should turn autostart off through the same route with DELETE', async () => {
    const { store, http } = configure('da-DK');
    store.autostart.set(true);

    const saved = store.setAutostart(false);

    const request = http.expectOne('/api/settings/autostart');
    expect(request.request.method).toBe('DELETE');
    request.flush(settingsJson({ autostart: false }));

    await saved;

    expect(store.autostart()).toBe(false);
  });

  // The server's answer wins over what was asked for. On a machine whose registry refuses, the
  // switch has to end up showing what actually happened rather than what the click intended.
  it('should keep autostart off and say why when the registry refuses', async () => {
    const { store, http } = configure('da-DK');

    const saved = store.setAutostart(true);

    http.expectOne('/api/settings/autostart').flush(
      new Blob([
        JSON.stringify({
          code: 'autostart.failed',
          message: 'Group policy says no.',
        }),
      ]),
      { status: 400, statusText: 'Bad Request' },
    );

    await saved;

    expect(store.autostart()).toBe(false);
    expect(store.autostartError()).toBe(
      'Autostart kunne ikke ændres. Kontrollér om Windows tillader det på maskinen.',
    );
    // Its own line, so a refused registry does not print above the language picker - which since
    // the accordion could be a section the user is not looking at.
    expect(store.error()).toBeNull();
  });

  it('should store an Azure DevOps token on its own route and report that there is one', async () => {
    const { store, http } = configure('da-DK');

    const saved = store.setAdoToken('et-personligt-adgangstoken');

    const request = http.expectOne('/api/settings/ado-token');
    expect(request.request.method).toBe('PUT');
    expect(JSON.parse(request.request.body)).toEqual({ token: 'et-personligt-adgangstoken' });
    request.flush(settingsJson({ hasAdoToken: true }));

    expect(await saved).toBe(true);
    expect(store.hasAdoToken()).toBe(true);
    // Its own route, so the Jira token cannot have been touched on the way.
    expect(store.hasJiraToken()).toBe(false);
  });

  it('should forget a stored Azure DevOps token', async () => {
    const { store, http } = configure('da-DK');
    store.hasAdoToken.set(true);

    const cleared = store.clearAdoToken();
    const request = http.expectOne('/api/settings/ado-token');
    expect(request.request.method).toBe('DELETE');
    request.flush(settingsJson({ hasAdoToken: false }));
    await cleared;

    expect(store.hasAdoToken()).toBe(false);
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
    // The Jira group's own line, as Azure DevOps' token answers on the ADO group's: `error` is the
    // language group's, and a message there is invisible once that group is folded shut.
    expect(store.jiraError()).toBe('Tokenet må ikke være tomt.');
    expect(store.error()).toBeNull();
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

  it('should never hold either token in a signal', async () => {
    // Both tokens are write-only: they go out through their own route and come back only as
    // hasJiraToken and hasAdoToken. A signal holding one would put it in a component's template
    // scope, and it would survive navigating away from the settings page.
    const { store, http } = configure('da-DK');
    const secret = 'det-her-maa-ikke-blive-liggende';
    const adoSecret = 'og-det-her-heller-ikke';

    const saved = store.setToken(secret);
    http.expectOne('/api/settings/jira-token').flush(settingsJson({ hasJiraToken: true }));
    await saved;

    const savedAdo = store.setAdoToken(adoSecret);
    http.expectOne('/api/settings/ado-token').flush(settingsJson({ hasAdoToken: true }));
    await savedAdo;

    // Names first, so a field called `jiraToken` is caught even before anything is written to it.
    // In declaration order, which is the order Object.keys answers in.
    const own = Object.keys(store);
    expect(own.filter((key) => /token/i.test(key))).toEqual(['hasJiraToken', 'hasAdoToken']);

    // And then the values, because the name guard on its own is dodged by calling the field
    // something else. Methods live on the prototype, so the only own properties that are
    // functions here are the signals — calling them has no side effect.
    const holding = Object.entries(store as unknown as Record<string, unknown>)
      .filter(([, value]) => typeof value === 'function')
      .filter(([, value]) => {
        const held = JSON.stringify((value as () => unknown)() ?? null);
        return held.includes(secret) || held.includes(adoSecret);
      })
      .map(([key]) => key);

    expect(holding).toEqual([]);
  });
});
