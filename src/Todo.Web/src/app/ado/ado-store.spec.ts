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
  // Required on the contract, and spelled here because nothing else would notice if it went missing:
  // the `as I*PreviewRow` cast in this file's generated-row helper accepts an object without it, so
  // even the compiler-visible fixture is blind to a new required field. Measured when it was added.
  reconciled: boolean;
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
    // The ordinary row came from the fetch. The reconciled ones set it true, and the screen's
    // "showing N of total" line counts on the difference - total is the fetch's number alone.
    reconciled: false,
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
 * ordinary preview says the field is there and empty, not that it is absent. Measured rather than
 * assumed, and the measurement is named because the obvious one does not hold - the C# tests read
 * the field into a `string?` and assert null, which passes on an absent property just as happily.
 * `TaskEndpointsTests.Wire_format_uses_the_names_the_contract_declares` asserts the raw
 * `"reconciliationNotice":null` in the response body, and that is the one this rests on.
 */
interface PreviewJson {
  total: number;
  rows: PreviewRowJson[];
  reconciliationNotice: string | null;
}

function previewJson(body: Omit<PreviewJson, 'reconciliationNotice'> & Partial<PreviewJson>): Blob {
  // The default goes on after the spread rather than before it, so a caller passing an explicit
  // undefined gets the null the host sends rather than deleting the key back out of the body.
  return json({ ...body, reconciliationNotice: body.reconciliationNotice ?? null });
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
    store.reconciliationNotice.set('ado.refused');

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
    // And the notice with them: it answers for a reconciliation that never ran, so a stale one
    // would stand under a fresh refusal explaining something else.
    expect(store.reconciliationNotice()).toBeNull();
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
          row({
            key: '16901',
            state: 'Resolved',
            alreadyImported: true,
            suggestsClosing: true,
            closureReason: 'resolved',
          }),
          // A reason as well, so the missing state is the only thing that can drop this row. Without
          // it the row would fail both predicates at once and this test would pass while measuring
          // the wrong one.
          row({
            key: '17165',
            state: null,
            alreadyImported: true,
            suggestsClosing: true,
            closureReason: 'resolved',
          }),
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

  /**
   * The sibling predicate, and the one that replaced a `?? ClosureReason.Resolved` fallback. A row
   * that suggests a closure without saying why is dropped rather than relabelled: the server's first
   * guard asks whether the state stands in the done list, which an unassigned closure's need not, so
   * a client that guessed "resolved" would have every hand-over skipped and counted in `skipped` -
   * the exact fault the reason field was added to prevent, reintroduced from the sending side.
   *
   * The row that survives carries `unassigned`, not `resolved`: with two resolved rows the assertion
   * could not tell "the reason was read off the row" from "the reason was defaulted", which is how
   * the fallback stayed invisible for as long as it did.
   *
   * Not the only guard on that, and worth saying so rather than leaving it to be discovered: putting
   * the fallback back also fells `should split the selection into the rows it writes, closes and
   * reopens`, whose fixture has carried `unassigned` since task 8. Two tests hold the reason to what
   * the row said, so do not "simplify" the older fixture to `resolved` - that would retire one of
   * them in silence.
   *
   * Unreachable from the server - all four preview-row sites drive the two fields off one expression
   * - so this measures a fixture, deliberately. It is the shape a stale hand-written wire fixture
   * takes, and five of them in this repository had exactly it until the predicate went in.
   */
  it('should drop a closable row that does not say why it is closable', async () => {
    const previewed = store.preview();
    http.expectOne('/api/ado/preview').flush(
      previewJson({
        total: 2,
        rows: [
          row({
            key: '16901',
            state: 'Active',
            alreadyImported: true,
            suggestsClosing: true,
            closureReason: 'unassigned',
          }),
          row({ key: '17165', state: 'Resolved', alreadyImported: true, suggestsClosing: true }),
        ],
      }),
    );
    await previewed;

    expect(store.closable().map((r) => r.key)).toEqual(['16901', '17165']);

    const imported = store.import([], store.closable());
    const request = http.expectOne('/api/ado/import');
    expect(JSON.parse(request.request.body).closures).toEqual([
      { key: '16901', state: 'Active', reason: 'unassigned' },
    ]);
    request.flush(json({ imported: 0, skipped: 0, closed: 1 }));
    await imported;
  });

  /**
   * The third set, and the fixture names one row per set so the three cannot be confused for one
   * another: a row that is only reopenable is what tells `reopenable()` apart from `closable()` and
   * from "every row that was imported before".
   */
  it('should offer only the rows the source is not finished with for reopening', async () => {
    const previewed = store.preview();
    http.expectOne('/api/ado/preview').flush(
      previewJson({
        total: 3,
        rows: [
          row({ key: '15664' }),
          row({ key: '16901', state: 'Resolved', alreadyImported: true, suggestsClosing: true }),
          row({
            key: '17170',
            state: 'Active',
            alreadyImported: true,
            suggestsReopening: true,
            reopenTo: 'open',
          }),
        ],
      }),
    );
    await previewed;

    expect(store.reopenable().map((r) => r.key)).toEqual(['17170']);
    // The other two, because the three sets being disjoint is what lets the screen add their sizes
    // up rather than count one row into two sentences.
    expect(store.selectable().map((r) => r.key)).toEqual(['15664']);
    expect(store.closable().map((r) => r.key)).toEqual(['16901']);
  });

  /**
   * One press, three lists. Written for both sources rather than for this one alone: the two stores
   * are twins, so a divergence in either would read as an adaptation - and until `e01c6f6` no spec
   * under jira/ asserted on a closure body at all, which is how that half stayed unmeasured through
   * two slices.
   *
   * A reopening carries the same three facts a closure does and no fourth: `reopenTo` is deliberately
   * left behind, because the server re-derives it from the state and the user's own lists. Sending it
   * would let the suggestion and the import disagree.
   */
  it('should split the selection into the rows it writes, closes and reopens', async () => {
    const previewed = store.preview();
    http.expectOne('/api/ado/preview').flush(
      previewJson({
        total: 3,
        rows: [
          row({ key: '15664', title: 'Ret rapporten' }),
          row({
            key: '16901',
            state: 'Resolved',
            alreadyImported: true,
            suggestsClosing: true,
            closureReason: 'unassigned',
            doneAt: '2026-08-15T09:00:00Z',
          }),
          row({
            key: '17170',
            state: 'Active',
            alreadyImported: true,
            suggestsReopening: true,
            reopenTo: 'open',
            waitingSince: '2026-08-16T10:00:00Z',
          }),
        ],
      }),
    );
    await previewed;

    const imported = store.import(store.selectable(), store.closable(), store.reopenable());
    const request = http.expectOne('/api/ado/import');
    const body = JSON.parse(request.request.body);

    expect(body.rows.map((r: { key: string }) => r.key)).toEqual(['15664']);
    expect(body.closures).toEqual([
      { key: '16901', state: 'Resolved', reason: 'unassigned', doneAt: '2026-08-15T09:00:00Z' },
    ]);
    expect(body.reopenings).toEqual([
      { key: '17170', state: 'Active', waitingSince: '2026-08-16T10:00:00Z' },
    ]);
    // The decision the server takes again for itself, rather than one the client names.
    expect(Object.keys(body.reopenings[0])).not.toContain('reopenTo');

    request.flush(json({ imported: 1, skipped: 0, closed: 1, reopened: 1 }));
    expect((await imported)?.reopened).toBe(1);
  });

  /**
   * Why the reconciliation had nothing to say, kept as the code the wire carries and offered as a
   * sentence beside it - the same pair a row's `excluded` and `excludedReason` make, so the notice
   * gets no mechanism of its own.
   *
   * The fetch's own rows are the teeth. A notice means the reconciliation was skipped, not that the
   * preview failed, so a store that treated the code as an error would throw the rows away with it.
   */
  it('should say why the reconciliation had nothing to say, and keep the rows it did fetch', async () => {
    const previewed = store.preview();
    http.expectOne('/api/ado/preview').flush(
      previewJson({
        total: 1,
        rows: [row({ key: '15664' })],
        reconciliationNotice: 'ado.refused',
      }),
    );
    await previewed;

    expect(store.reconciliationNotice()).toBe('ado.refused');
    expect(store.reconciliationMessage()).toBe(
      'Azure DevOps afviste tokenet. Kontrollér tokenet under Indstillinger.',
    );
    expect(store.rows().map((r) => r.key)).toEqual(['15664']);
    expect(store.error()).toBeNull();
  });

  // The ordinary preview says the field is there and empty, and no sentence goes with it.
  it('should offer no reconciliation sentence when every key got a row of its own', async () => {
    const previewed = store.preview();
    http.expectOne('/api/ado/preview').flush(previewJson({ total: 1, rows: [row()] }));
    await previewed;

    expect(store.reconciliationNotice()).toBeNull();
    expect(store.reconciliationMessage()).toBeNull();
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
