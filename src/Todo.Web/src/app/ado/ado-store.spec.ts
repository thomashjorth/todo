import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AdoPreviewRow, API_BASE_URL, IAdoPreviewRow } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { AdoStore } from './ado-store';

/**
 * The wire shape, not the generated class: this is what the server sends. Written out by hand so a
 * field the store forgot to forward is a visible omission here rather than a compiler error - the
 * whole point is to measure the wire.
 */
interface PreviewRowJson {
  key: string;
  title: string;
  url: string;
  note?: string;
  deadline?: string;
  requester?: string;
  // Nullable, and spelled that way rather than as `string`, because the wire is what this
  // interface describes: reconciliation made both fields nullable, and the host serialises a null
  // instead of omitting the property. A fixture typed `string` cannot express the row the store's
  // own guard exists to drop, which is how that guard would go unmeasured.
  state?: string | null;
  workItemType?: string | null;
  isWaiting: boolean;
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
    key: '15664',
    title: 'En',
    url: 'https://ado.example/Min%20Samling/Saas/_workitems/edit/15664',
    state: 'Active',
    workItemType: 'Bug',
    isWaiting: false,
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
 * The preview *response* as the wire carries it, spelled out for the same reason `PreviewRowJson` is:
 * nothing reconciles these hand-written bodies against the contract, so a field the server started
 * sending is invisible here until somebody writes it down.
 *
 * `reconciliationNotice` is the one that made this necessary. It sits on the response rather than on
 * a row - it is the answer to a question about the whole set of reconciled keys, which no single row
 * can hold - so a fixture that only knew about `rows` and `total` had nowhere to put it, and the
 * reader task 8 builds would have been measured against a body the server no longer sends.
 *
 * Defaulted to null rather than left out, because null is what the host actually serialises: the
 * ordinary preview says the field is there and empty, not that it is absent.
 */
interface PreviewJson {
  total: number;
  rows: PreviewRowJson[];
  reconciliationNotice: string | null;
}

function previewJson(body: Omit<PreviewJson, 'reconciliationNotice'> & Partial<PreviewJson>): Blob {
  return json({ reconciliationNotice: null, ...body });
}

/**
 * The wire fixture handed to the generated constructor, which is the one place the two disagree: the
 * generated interface types a nullable field as `string | undefined`, while the host sends a real
 * `null`. The cast says that once, here, rather than at each call site - and it is why a test that
 * needs a null goes through `flush` instead, taking the path production takes.
 */
function constructed(overrides: Partial<PreviewRowJson> = {}): AdoPreviewRow {
  return new AdoPreviewRow(row(overrides) as IAdoPreviewRow);
}

describe('AdoStore', () => {
  let store: AdoStore;
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

    store = TestBed.inject(AdoStore);
    http = TestBed.inject(HttpTestingController);
  });

  it('should name whoever the token belongs to', async () => {
    const tested = store.testConnection();

    // A POST, not a GET: the contract says a cached GET could answer without asking the server.
    const request = http.expectOne('/api/ado/test');
    expect(request.request.method).toBe('POST');
    request.flush(json({ displayName: 'Thomas Hjorth Hansen' }));
    await tested;

    expect(store.connection()).toBe('Thomas Hjorth Hansen');
    expect(store.error()).toBeNull();
  });

  it('should say that Azure DevOps refused the token rather than showing a name', async () => {
    const tested = store.testConnection();

    http
      .expectOne('/api/ado/test')
      .flush(json({ code: 'ado.refused', message: 'Azure DevOps rejected the token.' }), {
        status: 400,
        statusText: 'Bad Request',
      });
    await tested;

    expect(store.connection()).toBeNull();
    expect(store.error()).toBe(
      'Azure DevOps afviste tokenet. Kontrollér tokenet under Indstillinger.',
    );
  });

  it('should list the state names the work items are in', async () => {
    const loaded = store.loadStates();

    const request = http.expectOne('/api/ado/states');
    expect(request.request.method).toBe('GET');
    request.flush(json({ names: ['Active', 'Blocked'] }));
    await loaded;

    expect(store.states()).toEqual(['Active', 'Blocked']);
  });

  it('should say why the states could not be read', async () => {
    const loaded = store.loadStates();

    http
      .expectOne('/api/ado/states')
      .flush(json({ code: 'ado.projectRequired', message: 'The project is missing.' }), {
        status: 400,
        statusText: 'Bad Request',
      });
    await loaded;

    expect(store.states()).toEqual([]);
    expect(store.error()).toBe('Projektet mangler. Udfyld det under Indstillinger.');
  });

  it('should keep an excluded row visible rather than dropping it', async () => {
    const previewed = store.preview();

    const request = http.expectOne('/api/ado/preview');
    expect(request.request.method).toBe('POST');
    // No body at all: the preview takes nothing, because everything it needs is a setting.
    expect(request.request.body).toBeNull();
    request.flush(
      previewJson({
        total: 2,
        rows: [
          row({ key: '15664' }),
          row({
            key: '16901',
            title: 'To',
            state: 'Blocked',
            isWaiting: true,
            excluded: 'ado.excludedWaiting',
          }),
        ],
      }),
    );
    await previewed;

    expect(store.rows().length).toBe(2);
    expect(store.total()).toBe(2);
    // Selectable rows are the ones import will actually write.
    expect(store.selectable().map((r) => r.key)).toEqual(['15664']);
  });

  it('should leave a row that is imported already out of the selectable ones', async () => {
    const previewed = store.preview();

    http.expectOne('/api/ado/preview').flush(
      previewJson({
        total: 2,
        rows: [row({ key: '15664' }), row({ key: '16901', title: 'To', alreadyImported: true })],
      }),
    );
    await previewed;

    expect(store.rows().length).toBe(2);
    expect(store.selectable().map((r) => r.key)).toEqual(['15664']);
  });

  it('should empty the list when the preview fails, so no stale rows can be imported', async () => {
    store.rows.set([constructed({ key: '17170' })]);
    store.total.set(1);

    const previewed = store.preview();
    http
      .expectOne('/api/ado/preview')
      .flush(json({ code: 'ado.unreachable', message: 'Azure DevOps could not be reached.' }), {
        status: 400,
        statusText: 'Bad Request',
      });
    await previewed;

    expect(store.rows()).toEqual([]);
    expect(store.total()).toBe(0);
    expect(store.error()).toBe(
      'Azure DevOps kunne ikke nås. Kontrollér samlingens URL og netværket.',
    );
  });

  it('should send every field the import needs, and answer with the receipt', async () => {
    const previewed = store.preview();
    http.expectOne('/api/ado/preview').flush(
      previewJson({
        total: 1,
        rows: [
          row({
            key: '15664',
            title: 'Ret rapporten',
            note: '<div>Se bilaget<br>og svar</div>',
            deadline: '2026-08-23',
            requester: 'Mette Kirkegaard',
            state: 'Blocked',
            workItemType: 'User Story',
            isWaiting: true,
            waitingSince: '2026-08-14T08:00:00Z',
          }),
        ],
      }),
    );
    await previewed;

    const imported = store.import(store.selectable());
    const request = http.expectOne('/api/ado/import');
    expect(request.request.method).toBe('POST');
    expect(JSON.parse(request.request.body)).toEqual({
      rows: [
        {
          key: '15664',
          title: 'Ret rapporten',
          note: '<div>Se bilaget<br>og svar</div>',
          requester: 'Mette Kirkegaard',
          state: 'Blocked',
          workItemType: 'User Story',
          waitingSince: '2026-08-14T08:00:00Z',
        },
      ],
    });
    request.flush(json({ imported: 1, skipped: 0 }));

    const receipt = await imported;
    expect(receipt?.imported).toBe(1);
    expect(receipt?.skipped).toBe(0);
  });

  /**
   * The two fields the row is shown with and never sends back. `isWaiting` is a decision the server
   * takes from the state and a setting that lives on the server; the deadline is arithmetic on the
   * server's own clock, and `AdoImportRow` has no field for it at all - so the assertion is that the
   * keys are absent, which is the only way a field that does not exist on the type can be measured.
   */
  it('should send neither its own opinion about waiting nor the proposed deadline', async () => {
    const imported = store.import([
      constructed({ key: '15664', state: 'Blocked', isWaiting: true, deadline: '2026-08-23' }),
    ]);

    const request = http.expectOne('/api/ado/import');
    const keys = Object.keys(JSON.parse(request.request.body).rows[0]);
    expect(keys).not.toContain('isWaiting');
    expect(keys).not.toContain('deadline');
    // And the facts those two decisions are taken from do go.
    expect(keys).toContain('state');
    expect(keys).toContain('workItemType');
    request.flush(json({ imported: 1, skipped: 0 }));
    await imported;
  });

  /**
   * A body the server should not send: a row that suggests closing while naming no state. The guard
   * exists to make that harmless rather than to trust it away, so this measures it.
   *
   * It goes through preview and flush rather than `new AdoPreviewRow(...)` on purpose. The generated
   * type admits only `string | undefined`, so a null could not be constructed through it at all -
   * while the wire really does carry one, because the host serialises nulls rather than omitting
   * them. This is the path production takes: JSON, then the client's own `init`.
   *
   * Two closures rather than one, and that is the assertion's teeth: the answered row has to survive
   * the same filter that drops the other. With one row the list would be empty either way, and the
   * test would pass against a guard that dropped everything.
   */
  it('should drop a closable row whose state the source never answered with', async () => {
    const previewed = store.preview();
    http.expectOne('/api/ado/preview').flush(
      previewJson({
        total: 2,
        rows: [
          row({ key: '16901', state: 'Resolved', alreadyImported: true, suggestsClosing: true }),
          row({ key: '17165', state: null, alreadyImported: true, suggestsClosing: true }),
        ],
      }),
    );
    await previewed;

    // Both are closable as far as the store is concerned - the flag is what it filters on.
    expect(store.closable().map((r) => r.key)).toEqual(['16901', '17165']);

    const imported = store.import([], store.closable());
    const request = http.expectOne('/api/ado/import');
    expect(JSON.parse(request.request.body).closures).toEqual([
      { key: '16901', state: 'Resolved', reason: 'resolved' },
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

    http.expectOne('/api/ado/preview').flush(previewJson({ total: 0, rows: [] }));
    await previewed;

    expect(store.busy()).toBe(false);
  });

  it('should stop being busy when the call fails', async () => {
    const tested = store.testConnection();

    http.expectOne('/api/ado/test').flush(json({ code: 'ado.unreachable', message: 'nope' }), {
      status: 400,
      statusText: 'Bad Request',
    });
    await tested;

    expect(store.busy()).toBe(false);
  });
});
