import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL, IJiraPreviewRow, JiraPreviewRow } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { JiraStore } from './jira-store';

interface PreviewRowJson {
  key: string;
  title: string;
  url: string;
  note?: string;
  deadline?: string;
  requester?: string;
  // Nullable for the reason AdoStore's fixture spells out: this interface describes the wire, the
  // status became nullable with reconciliation, and a null is serialised rather than omitted. Typed
  // `string` it could not express the row the store's guard drops.
  status?: string | null;
  isWaiting: boolean;
  isDuty: boolean;
  waitingSince?: string;
  alreadyImported: boolean;
  suggestsClosing: boolean;
  closureReason?: string;
  suggestsReopening: boolean;
  reopenTo?: string;
  doneAt?: string;
  excluded?: string;
}

function row(overrides: Partial<PreviewRowJson> = {}): PreviewRowJson {
  return {
    key: 'SAAS-1',
    title: 'En',
    url: 'https://jira.example/browse/SAAS-1',
    status: 'I gang',
    isWaiting: false,
    isDuty: false,
    alreadyImported: false,
    suggestsClosing: false,
    suggestsReopening: false,
    ...overrides,
  };
}

function json(body: unknown): Blob {
  return new Blob([JSON.stringify(body)]);
}

/**
 * See AdoStore's spec for why this cast exists: the generated interface types a nullable field as
 * `string | undefined` while the host sends a real `null`, and saying so once here is what keeps a
 * null-carrying fixture on the `flush` path production uses.
 */
function constructed(overrides: Partial<PreviewRowJson> = {}): JiraPreviewRow {
  return new JiraPreviewRow(row(overrides) as IJiraPreviewRow);
}

describe('JiraStore', () => {
  let store: JiraStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [translocoTesting()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '' },
      ],
    });

    store = TestBed.inject(JiraStore);
    http = TestBed.inject(HttpTestingController);
  });

  it('should name whoever the token belongs to', async () => {
    const tested = store.testConnection();

    // A POST, not a GET: the contract says a cached GET could answer without asking Jira at all.
    const request = http.expectOne('/api/jira/test');
    expect(request.request.method).toBe('POST');
    request.flush(json({ displayName: 'Thomas Hjorth Hansen' }));
    await tested;

    expect(store.connection()).toBe('Thomas Hjorth Hansen');
    expect(store.error()).toBeNull();
  });

  it('should say that Jira refused the token rather than showing a name', async () => {
    const tested = store.testConnection();

    http
      .expectOne('/api/jira/test')
      .flush(json({ code: 'jira.refused', message: 'Jira rejected the token.' }), {
        status: 400,
        statusText: 'Bad Request',
      });
    await tested;

    expect(store.connection()).toBeNull();
    expect(store.error()).toBe('Jira afviste tokenet. Kontrollér tokenet under Indstillinger.');
  });

  it('should list the status names the project uses', async () => {
    const loaded = store.loadStatuses();

    const request = http.expectOne('/api/jira/statuses');
    expect(request.request.method).toBe('GET');
    request.flush(json({ names: ['I gang', 'Afventer general'] }));
    await loaded;

    expect(store.statuses()).toEqual(['I gang', 'Afventer general']);
  });

  it('should say why the statuses could not be read', async () => {
    const loaded = store.loadStatuses();

    http
      .expectOne('/api/jira/statuses')
      .flush(json({ code: 'jira.notConfigured', message: 'Jira is not configured.' }), {
        status: 400,
        statusText: 'Bad Request',
      });
    await loaded;

    expect(store.statuses()).toEqual([]);
    expect(store.error()).toBe(
      'Jira er ikke sat op. Udfyld basisURL, projektnøgle og token under Indstillinger.',
    );
  });

  it('should keep an excluded row visible rather than dropping it', async () => {
    const previewed = store.preview();

    const request = http.expectOne('/api/jira/preview');
    expect(request.request.method).toBe('POST');
    request.flush(
      json({
        total: 2,
        rows: [
          row({ key: 'SAAS-1', title: 'En' }),
          row({
            key: 'SAAS-2',
            title: 'To',
            status: 'Afventer general',
            isWaiting: true,
            excluded: 'jira.excludedWaiting',
          }),
        ],
      }),
    );
    await previewed;

    expect(store.rows().length).toBe(2);
    expect(store.total()).toBe(2);
    // Selectable rows are the ones import will actually write.
    expect(store.selectable().map((r) => r.key)).toEqual(['SAAS-1']);
  });

  it('should leave a row that is imported already out of the selectable ones', async () => {
    const previewed = store.preview();

    http.expectOne('/api/jira/preview').flush(
      json({
        total: 2,
        rows: [row({ key: 'SAAS-1' }), row({ key: 'SAAS-2', title: 'To', alreadyImported: true })],
      }),
    );
    await previewed;

    expect(store.rows().length).toBe(2);
    expect(store.selectable().map((r) => r.key)).toEqual(['SAAS-1']);
  });

  it('should empty the list when the preview fails, so no stale rows can be imported', async () => {
    store.rows.set([constructed({ key: 'SAAS-9' })]);
    store.total.set(1);

    const previewed = store.preview();
    http
      .expectOne('/api/jira/preview')
      .flush(json({ code: 'jira.unreachable', message: 'Jira could not be reached.' }), {
        status: 400,
        statusText: 'Bad Request',
      });
    await previewed;

    expect(store.rows()).toEqual([]);
    expect(store.total()).toBe(0);
    expect(store.error()).toBe('Jira kunne ikke nås. Kontrollér basisURL og netværket.');
  });

  it('should send every field the import needs, and answer with the receipt', async () => {
    const previewed = store.preview();
    http.expectOne('/api/jira/preview').flush(
      json({
        total: 1,
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
        ],
      }),
    );
    await previewed;

    const imported = store.import(store.selectable());
    const request = http.expectOne('/api/jira/import');
    expect(request.request.method).toBe('POST');
    expect(JSON.parse(request.request.body)).toEqual({
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
    request.flush(json({ imported: 1, skipped: 0 }));

    const receipt = await imported;
    expect(receipt?.imported).toBe(1);
    expect(receipt?.skipped).toBe(0);
  });

  // isWaiting is deliberately not on the wire: the server looks the status up in the user's
  // waiting list, because that setting lives on the server.
  it('should not send its own opinion about whether a row is waiting', async () => {
    const imported = store.import([
      constructed({ key: 'SAAS-1', isWaiting: true, status: 'Afventer general' }),
    ]);

    const request = http.expectOne('/api/jira/import');
    expect(Object.keys(JSON.parse(request.request.body).rows[0])).not.toContain('isWaiting');
    request.flush(json({ imported: 1, skipped: 0 }));
    await imported;
  });

  /**
   * The twin of AdoStore's, and it is here because until now no spec under jira/ asserted on the
   * closures this store puts on the wire at all - so its guard and its reason fallback were both
   * unmeasured while the ADO ones were measured. See the ADO spec for why the fixture goes through
   * flush rather than the generated constructor, and why there are two closures rather than one.
   */
  it('should drop a closable row whose status the source never answered with', async () => {
    const previewed = store.preview();
    http.expectOne('/api/jira/preview').flush(
      json({
        total: 2,
        rows: [
          row({ key: 'SAAS-9', status: 'Løst', alreadyImported: true, suggestsClosing: true }),
          row({ key: 'SAAS-10', status: null, alreadyImported: true, suggestsClosing: true }),
        ],
      }),
    );
    await previewed;

    expect(store.closable().map((r) => r.key)).toEqual(['SAAS-9', 'SAAS-10']);

    const imported = store.import([], store.closable());
    const request = http.expectOne('/api/jira/import');
    expect(JSON.parse(request.request.body).closures).toEqual([
      { key: 'SAAS-9', status: 'Løst', reason: 'resolved' },
    ]);
    request.flush(json({ imported: 0, skipped: 0, closed: 1 }));
    await imported;
  });

  it('should send no request at all for an empty selection', async () => {
    expect(await store.import([])).toBeUndefined();

    http.verify();
  });

  // The buttons are disabled while a call is running, which is what makes the missing sequence
  // counter safe; see the comment in the store.
  it('should be busy only while a call is running', async () => {
    expect(store.busy()).toBe(false);

    const previewed = store.preview();
    expect(store.busy()).toBe(true);

    http.expectOne('/api/jira/preview').flush(json({ total: 0, rows: [] }));
    await previewed;

    expect(store.busy()).toBe(false);
  });

  it('should stop being busy when the call fails', async () => {
    const tested = store.testConnection();

    http.expectOne('/api/jira/test').flush(json({ code: 'jira.unreachable', message: 'nope' }), {
      status: 400,
      statusText: 'Bad Request',
    });
    await tested;

    expect(store.busy()).toBe(false);
  });
});
