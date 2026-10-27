import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { translocoTesting } from '../i18n/transloco.testing';
import { CONNECTIONS_POLL_MS, ConnectionsRoot } from './connections-root';
import { ConnectionsStatus } from './connections-status';
import { PAGE_SEARCH, PARENT_WINDOW } from './parent-channel';

const connected: ConnectionsStatus = {
  serverUrl: 'http://nas.test:8080',
  hasKey: true,
  tunnel: 'Connected',
  refusal: null,
  jira: { baseUrl: 'https://jira.test', hasToken: true },
  ado: { baseUrl: null, hasToken: false },
  ollamaUrl: 'http://127.0.0.1:11434',
};

async function render(search = '?parent=http://nas.test:8080&lang=da') {
  const parent = { postMessage: vi.fn() };

  TestBed.configureTestingModule({
    imports: [ConnectionsRoot, translocoTesting()],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: PAGE_SEARCH, useValue: search },
      { provide: PARENT_WINDOW, useValue: parent },
      { provide: CONNECTIONS_POLL_MS, useValue: null },
    ],
  });

  const fixture = TestBed.createComponent(ConnectionsRoot);
  const http = TestBed.inject(HttpTestingController);
  fixture.detectChanges();
  http.expectOne('/api/connections').flush(connected);
  await fixture.whenStable();
  fixture.detectChanges();

  return { fixture, http, parent, element: fixture.nativeElement as HTMLElement };
}

function byTestId<T extends HTMLElement>(element: HTMLElement, testid: string): T {
  return element.querySelector(`[data-testid="${testid}"]`) as T;
}

describe('ConnectionsRoot', () => {
  it('should tell the app it is ready once the status is read, and show it', async () => {
    const { parent, element } = await render();

    // After the read's promise settles, which is a turn after the render the status shows in.
    await vi.waitFor(() =>
      expect(parent.postMessage).toHaveBeenCalledWith(
        { type: 'ready', source: 'todo-helper' },
        'http://nas.test:8080',
      ),
    );
    expect(byTestId(element, 'connections-tunnel').textContent?.trim()).toBe(
      'Forbundet til serveren.',
    );
    expect(byTestId(element, 'connections-jira-token-state').textContent?.trim()).toBe(
      'Hjælperen har et token.',
    );
    expect(byTestId(element, 'connections-ado-clear')).toBeNull();
  });

  // Inside the settings group the group's own heading names it; on its own the page needs one.
  it('should leave the heading to the settings page when framed', async () => {
    const { element } = await render();

    expect(element.querySelector('h1')).toBeNull();
  });

  it('should send a typed token and empty the field once the helper has it', async () => {
    const { fixture, http, element } = await render();

    const token = byTestId<HTMLInputElement>(element, 'connections-ado-token');
    token.value = 'an-ado-pat';
    token.dispatchEvent(new Event('change'));

    const request = http.expectOne('/api/connections/ado');
    expect(request.request.body).toEqual({ baseUrl: '', token: 'an-ado-pat' });
    request.flush(connected);
    await fixture.whenStable();

    expect(token.value).toBe('');
  });

  // A refused save must not lose what the user typed: the field keeps it to try again.
  it('should keep the typed token when the helper refuses the save', async () => {
    const { fixture, http, element } = await render();

    const token = byTestId<HTMLInputElement>(element, 'connections-ado-token');
    token.value = 'an-ado-pat';
    token.dispatchEvent(new Event('change'));

    http.expectOne('/api/connections/ado').flush(null, { status: 500, statusText: 'Error' });
    await fixture.whenStable();

    expect(token.value).toBe('an-ado-pat');
  });

  // The address is saved on leaving its field, and the token is typed while that save is out: the
  // answer must not empty a token it never carried.
  it('should keep a token typed while the address was being saved', async () => {
    const { fixture, http, element } = await render();

    const baseUrl = byTestId<HTMLInputElement>(element, 'connections-ado-base-url');
    baseUrl.value = 'https://ado.example';
    baseUrl.dispatchEvent(new Event('change'));
    const request = http.expectOne('/api/connections/ado');

    const token = byTestId<HTMLInputElement>(element, 'connections-ado-token');
    token.value = 'an-ado-pat';
    request.flush(connected);
    await fixture.whenStable();

    expect(token.value).toBe('an-ado-pat');
  });
});
