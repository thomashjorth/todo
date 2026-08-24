import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { API_BASE_URL } from './api/todo-client';
import { App } from './app';
import { routes } from './app.routes';
import { translocoTesting } from './i18n/transloco.testing';
import { shortcutKey } from './shortcuts/shortcut-key';
import { ShortcutStore } from './shortcuts/shortcut-store';

// The generated client requests responseType 'blob' and decodes it with FileReader,
// so a flushed response only reaches the template after a later microtask.
function healthText(fixture: ComponentFixture<App>): Promise<string> {
  return vi.waitFor(() => {
    fixture.detectChanges();
    const el = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="health"]');
    expect(el).not.toBeNull();
    return el!.textContent ?? '';
  });
}

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App, translocoTesting()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter(routes),
        { provide: API_BASE_URL, useValue: '' },
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('should render the health status returned by the API', async () => {
    const fixture = TestBed.createComponent(App);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/health')
      .flush(new Blob([JSON.stringify({ status: 'ok', version: '1.2.3' })]));

    expect(await healthText(fixture)).toContain('API: ok (v1.2.3)');
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Mandalorian ToDo');
  });

  it('should show the navigation in Danish and follow a change of language', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    const text = (id: string) =>
      compiled.querySelector(`[data-testid="${id}"]`)!.textContent!.trim();

    expect(text('nav-tasks')).toBe('Opgaver');
    expect(text('nav-import')).toBe('Retro-import');
    expect(text('nav-jira')).toBe('Jira-import');
    expect(text('nav-settings')).toBe('Indstillinger');

    TestBed.inject(TranslocoService).setActiveLang('en');
    fixture.detectChanges();

    expect(text('nav-tasks')).toBe('Tasks');
    expect(text('nav-import')).toBe('Retro import');
    expect(text('nav-jira')).toBe('Jira import');
    expect(text('nav-settings')).toBe('Settings');
  });

  it('should link to every screen and mark the current one for a screen reader', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    const link = (id: string) => compiled.querySelector(`[data-testid="${id}"]`)!;
    const links = () => ['nav-tasks', 'nav-import', 'nav-jira', 'nav-settings'].map(link);

    // RouterLinkActive applies aria-current in a microtask of its own, so the
    // attribute lands a change detection cycle after the navigation resolves.
    const current = () =>
      vi.waitFor(() => {
        fixture.detectChanges();
        const marked = links().filter((a) => a.getAttribute('aria-current') === 'page');
        expect(marked).toHaveLength(1);
        return marked[0].getAttribute('data-testid');
      });

    await TestBed.inject(Router).navigate(['/']);

    expect(links().map((a) => a.getAttribute('href'))).toEqual([
      '/',
      '/import',
      '/jira',
      '/settings',
    ]);
    expect(await current()).toBe('nav-tasks');

    await TestBed.inject(Router).navigate(['/import']);

    expect(await current()).toBe('nav-import');

    await TestBed.inject(Router).navigate(['/jira']);

    expect(await current()).toBe('nav-jira');

    await TestBed.inject(Router).navigate(['/settings']);

    expect(await current()).toBe('nav-settings');
  });

  it('should look up Alt and Alt+Shift in layers of their own, and leave AltGr alone', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const store = TestBed.inject(ShortcutStore);
    const activate = vi.fn();
    store.register(shortcutKey('alt', 'k'), activate);

    const press = (init: KeyboardEventInit) => {
      const event = new KeyboardEvent('keydown', { ...init, cancelable: true });
      document.dispatchEvent(event);
      return event;
    };

    const alt = press({ key: 'k', altKey: true });

    expect(activate).toHaveBeenCalledTimes(1);
    expect(alt.defaultPrevented).toBe(true);

    // Shift is a layer of its own, so the field layer must not reach the navigation layer's entry.
    const altShift = press({ key: 'K', altKey: true, shiftKey: true });

    expect(activate).toHaveBeenCalledTimes(1);
    expect(altShift.defaultPrevented).toBe(false);

    // Ctrl+Alt is AltGr on a Danish keyboard: it has to reach the browser so @, £ and $ can be typed.
    const altGr = press({ key: 'k', altKey: true, ctrlKey: true });

    expect(activate).toHaveBeenCalledTimes(1);
    expect(altGr.defaultPrevented).toBe(false);
  });

  it('should report the API as unavailable when the call fails', async () => {
    const fixture = TestBed.createComponent(App);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/health')
      .flush(new Blob(['boom']), { status: 500, statusText: 'Server Error' });

    expect(await healthText(fixture)).toContain('API: ikke tilgængelig');
  });
});
