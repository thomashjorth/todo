import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { SystemStore } from './system-store';

function configure(): { store: SystemStore; http: HttpTestingController } {
  TestBed.configureTestingModule({
    imports: [translocoTesting()],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: API_BASE_URL, useValue: '' },
    ],
  });

  return { store: TestBed.inject(SystemStore), http: TestBed.inject(HttpTestingController) };
}

describe('SystemStore', () => {
  it('should send the link to the endpoint that opens it outside the app', async () => {
    const { store, http } = configure();

    const opened = store.openLink('https://example.com/docs');
    const request = http.expectOne('/api/system/open-link');
    expect(request.request.method).toBe('POST');
    expect(JSON.parse(request.request.body)).toEqual({ url: 'https://example.com/docs' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await opened;

    expect(store.error()).toBeNull();
  });

  // Nothing happens on screen when a link cannot be opened, so the reason has to be said out loud.
  it('should say why a link the app may not open was refused', async () => {
    const { store, http } = configure();

    const opened = store.openLink('mailto:someone@example.com');
    http.expectOne('/api/system/open-link').flush(
      new Blob([
        JSON.stringify({
          code: 'system.unsupportedScheme',
          message: 'Only http and https links can be opened.',
        }),
      ]),
      { status: 400, statusText: 'Bad Request' },
    );
    await opened;

    expect(store.error()).toBe('Kun http- og https-links kan åbnes.');
  });

  it('should drop the message once it no longer applies', async () => {
    const { store, http } = configure();

    const refused = store.openLink('file:///C:/Windows');
    http
      .expectOne('/api/system/open-link')
      .flush(new Blob([JSON.stringify({ code: 'system.unsupportedScheme', message: 'no' })]), {
        status: 400,
        statusText: 'Bad Request',
      });
    await refused;

    store.clearError();

    expect(store.error()).toBeNull();
  });
});
