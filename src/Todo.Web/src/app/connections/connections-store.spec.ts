import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { ConnectionsStatus } from './connections-status';
import { ConnectionsStore } from './connections-store';
import { PAGE_SEARCH, PARENT_WINDOW } from './parent-channel';

/** The helper's answer as the wire spells it; LocalApiTests pins the same names on the C# side. */
function status(overrides: Partial<ConnectionsStatus> = {}): ConnectionsStatus {
  return {
    serverUrl: 'http://nas.test:8080',
    hasKey: true,
    tunnel: 'Connected',
    refusal: null,
    jira: { baseUrl: 'https://jira.test', hasToken: false },
    ado: { baseUrl: null, hasToken: false },
    ollamaUrl: 'http://127.0.0.1:11434',
    ...overrides,
  };
}

function configure() {
  const parent = { postMessage: vi.fn() };

  TestBed.configureTestingModule({
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: PAGE_SEARCH, useValue: '?parent=http://nas.test:8080' },
      { provide: PARENT_WINDOW, useValue: parent },
    ],
  });

  return {
    store: TestBed.inject(ConnectionsStore),
    http: TestBed.inject(HttpTestingController),
    posted: () => parent.postMessage.mock.calls.map(([message]) => message.type as string),
  };
}

describe('ConnectionsStore', () => {
  it('should read the status with the header the helper demands', async () => {
    const { store, http } = configure();

    const loaded = store.load();
    const request = http.expectOne('/api/connections');
    expect(request.request.headers.get('X-Todo-Helper')).toBe('1');
    request.flush(status());
    await loaded;

    expect(store.status()?.tunnel).toBe('Connected');
  });

  it('should send a typed token, keep none, and tell the app something changed', async () => {
    const { store, http, posted } = configure();

    const saved = store.saveSource('jira', 'https://jira.test', 'a-jira-pat');
    const request = http.expectOne('/api/connections/jira');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ baseUrl: 'https://jira.test', token: 'a-jira-pat' });
    request.flush(status({ jira: { baseUrl: 'https://jira.test', hasToken: true } }));
    await saved;

    expect(store.status()?.jira.hasToken).toBe(true);
    expect(JSON.stringify(store.status())).not.toContain('a-jira-pat');
    expect(posted()).toEqual(['changed']);
  });

  // The field is write-only, so an empty one means "keep the token the helper has".
  it('should send no token when none was typed', async () => {
    const { store, http } = configure();

    const saved = store.saveSource('ado', 'https://ado.test/Coll', '  ');
    const request = http.expectOne('/api/connections/ado');
    expect(request.request.body).toEqual({ baseUrl: 'https://ado.test/Coll', token: null });
    request.flush(status());
    await saved;
  });

  it('should show the hash of a new key and read the status again', async () => {
    const { store, http } = configure();

    const paired = store.pair();
    http.expectOne('/api/server/pair').flush({ keyHash: 'pbkdf2-sha256$1$salt$hash' });
    await Promise.resolve();
    http.expectOne('/api/connections').flush(status());
    await paired;

    expect(store.keyHash()).toBe('pbkdf2-sha256$1$salt$hash');
  });

  it('should say the helper failed and tell the app nothing', async () => {
    const { store, http, posted } = configure();

    const saved = store.saveServer('http://nas.test:8080');
    http.expectOne('/api/server').flush(null, { status: 403, statusText: 'Forbidden' });
    await saved;

    expect(store.failed()).toBe(true);
    expect(posted()).toEqual([]);
  });

  // The app's helperOnline follows the tunnel without a reload: the page says so when it moves.
  it('should tell the app when the tunnel moved between two reads, and not on the first', async () => {
    const { store, http, posted } = configure();

    const first = store.load();
    http.expectOne('/api/connections').flush(status({ tunnel: 'Connecting' }));
    await first;
    expect(posted()).toEqual([]);

    const second = store.load();
    http.expectOne('/api/connections').flush(status({ tunnel: 'Connected' }));
    await second;
    expect(posted()).toEqual(['changed']);

    const third = store.load();
    http.expectOne('/api/connections').flush(status({ tunnel: 'Connected' }));
    await third;
    expect(posted()).toEqual(['changed']);
  });
});
