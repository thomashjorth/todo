import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { API_BASE_URL } from './api/todo-client';
import { App } from './app';

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
      imports: [App],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
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

  it('should report the API as unavailable when the call fails', async () => {
    const fixture = TestBed.createComponent(App);
    TestBed.inject(HttpTestingController)
      .expectOne('/api/health')
      .flush(new Blob(['boom']), { status: 500, statusText: 'Server Error' });

    expect(await healthText(fixture)).toContain('API: unavailable');
  });
});
