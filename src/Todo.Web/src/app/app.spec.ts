import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { TranslocoService } from '@jsverse/transloco';
import { API_BASE_URL } from './api/todo-client';
import { App } from './app';
import { routes } from './app.routes';
import { translocoTesting } from './i18n/transloco.testing';

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
    expect(compiled.querySelector('h1')?.textContent).toContain('Todo');
  });

  it('should show the navigation in Danish and follow a change of language', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    const text = (id: string) =>
      compiled.querySelector(`[data-testid="${id}"]`)!.textContent!.trim();

    expect(text('nav-tasks')).toBe('Opgaver');
    expect(text('nav-import')).toBe('Retro-import');

    TestBed.inject(TranslocoService).setActiveLang('en');
    fixture.detectChanges();

    expect(text('nav-tasks')).toBe('Tasks');
    expect(text('nav-import')).toBe('Retro import');
  });

  it('should link to both screens and mark the current one for a screen reader', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;
    const tasks = () => compiled.querySelector('[data-testid="nav-tasks"]')!;
    const retro = () => compiled.querySelector('[data-testid="nav-import"]')!;

    // RouterLinkActive applies aria-current in a microtask of its own, so the
    // attribute lands a change detection cycle after the navigation resolves.
    const current = () =>
      vi.waitFor(() => {
        fixture.detectChanges();
        const marked = [tasks(), retro()].filter((a) => a.getAttribute('aria-current') === 'page');
        expect(marked).toHaveLength(1);
        return marked[0].getAttribute('data-testid');
      });

    await TestBed.inject(Router).navigate(['/']);

    expect(tasks().getAttribute('href')).toBe('/');
    expect(retro().getAttribute('href')).toBe('/import');
    expect(await current()).toBe('nav-tasks');

    await TestBed.inject(Router).navigate(['/import']);

    expect(await current()).toBe('nav-import');
  });

  it('should report the API as unavailable when the call fails', async () => {
    const fixture = TestBed.createComponent(App);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/health')
      .flush(new Blob(['boom']), { status: 500, statusText: 'Server Error' });

    expect(await healthText(fixture)).toContain('API: unavailable');
  });
});
