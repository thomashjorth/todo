import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_BASE_URL } from '../api/todo-client';
import { SYSTEM_LANGUAGE } from '../i18n/system-language';
import { translocoTesting } from '../i18n/transloco.testing';
import { HELPER_READY_TIMEOUT_MS, HelperFrame } from './helper-frame';
import { SettingsStore } from './settings-store';

const helperOrigin = 'http://helper.test:47600';

function render(timeoutMs = 60_000) {
  TestBed.configureTestingModule({
    imports: [HelperFrame, translocoTesting()],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: API_BASE_URL, useValue: '' },
      { provide: SYSTEM_LANGUAGE, useValue: 'da-DK' },
      { provide: HELPER_READY_TIMEOUT_MS, useValue: timeoutMs },
    ],
  });

  TestBed.inject(SettingsStore).helperPageUrl.set(`${helperOrigin}/connections`);

  const fixture = TestBed.createComponent(HelperFrame);
  fixture.detectChanges();
  const element = fixture.nativeElement as HTMLElement;
  const frame = element.querySelector('[data-testid="helper-frame"]') as HTMLIFrameElement;

  return { fixture, element, frame, http: TestBed.inject(HttpTestingController) };
}

/** A message as the browser hands it over: with the sender's origin and window. */
function post(data: unknown, origin: string, source: Window | null): void {
  window.dispatchEvent(new MessageEvent('message', { data, origin, source }));
}

describe('HelperFrame', () => {
  it('should frame the page with this origin and the language, hidden until it is ready', () => {
    const { frame } = render();

    const src = new URL(frame.src);
    expect(src.origin + src.pathname).toBe(`${helperOrigin}/connections`);
    expect(src.searchParams.get('parent')).toBe(window.location.origin);
    expect(src.searchParams.get('lang')).toBe('da');
    expect(frame.classList).toContain('hidden');
  });

  it('should show the page when it says it is ready and take its height', () => {
    const { fixture, frame } = render();

    post({ source: 'todo-helper', type: 'ready' }, helperOrigin, frame.contentWindow);
    post(
      { source: 'todo-helper', type: 'height', height: 612.4 },
      helperOrigin,
      frame.contentWindow,
    );
    fixture.detectChanges();

    expect(frame.classList).not.toContain('hidden');
    expect(frame.getAttribute('height')).toBe('613');
  });

  // Two checks, each needed: the origin is the helper's, and the window is this frame's.
  it('should ignore a message from another origin, or from another window on the right one', () => {
    const { fixture, frame } = render();

    post({ source: 'todo-helper', type: 'ready' }, 'http://evil.test', frame.contentWindow);
    post({ source: 'todo-helper', type: 'ready' }, helperOrigin, window);
    fixture.detectChanges();

    expect(frame.classList).toContain('hidden');
  });

  it('should read the settings again when the page says its connections changed', () => {
    const { frame, http } = render();

    post({ source: 'todo-helper', type: 'changed' }, helperOrigin, frame.contentWindow);

    http.expectOne('/api/settings');
  });

  it('should say where connections are set up when the page never answers', async () => {
    const { fixture, element } = render(1);

    await new Promise((resolve) => setTimeout(resolve, 10));
    fixture.detectChanges();

    expect(
      element.querySelector('[data-testid="helper-frame-fallback"]')?.textContent?.trim(),
    ).toBe(
      'Forbindelserne indstilles fra den PC, hvor hjælperen kører. Kører den her, så start Helper.cmd. ' +
        'Kører den allerede, kender den måske serveren under en anden adresse end den du bruger nu: ' +
        'åbn dens side og ret adressen.',
    );
  });
});
