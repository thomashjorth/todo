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

function activeLang(): string {
  return TestBed.inject(TranslocoService).getActiveLang();
}

describe('SettingsStore', () => {
  it('should show the app in the stored language', async () => {
    const { store, http } = configure('da-DK');

    const started = store.start();
    http.expectOne('/api/settings').flush(new Blob([JSON.stringify({ language: 'en' })]));
    await started;

    expect(store.language()).toBe('en');
    expect(activeLang()).toBe('en');
    expect(document.documentElement.lang).toBe('en');
  });

  it('should follow the system language when nothing is stored', async () => {
    const { store, http } = configure('da-DK');

    const started = store.start();
    http.expectOne('/api/settings').flush(new Blob([JSON.stringify({ language: null })]));
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
    request.flush(new Blob([JSON.stringify({ language: 'en' })]));
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
    request.flush(new Blob([JSON.stringify({ language: null })]));
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
});
