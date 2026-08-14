import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { API_BASE_URL } from '../api/todo-client';
import { SYSTEM_LANGUAGE } from '../i18n/system-language';
import { translocoTesting } from '../i18n/transloco.testing';
import { Settings } from './settings';
import { SettingsStore } from './settings-store';

interface Screen {
  fixture: ComponentFixture<Settings>;
  element: HTMLElement;
  http: HttpTestingController;
}

// The generated client requests responseType 'blob' and decodes it with FileReader,
// so a flushed response only reaches the template after a later microtask.
function settled<T>(screen: Screen, read: () => T): Promise<T> {
  return vi.waitFor(() => {
    screen.fixture.detectChanges();
    return read();
  });
}

async function open(stored: string | null, aliases: string[] = []): Promise<Screen> {
  const http = TestBed.inject(HttpTestingController);

  const started = TestBed.inject(SettingsStore).start();
  http.expectOne('/api/settings').flush(new Blob([JSON.stringify({ language: stored })]));
  await started;

  const fixture = TestBed.createComponent(Settings);
  const element = fixture.nativeElement as HTMLElement;
  fixture.detectChanges();
  http.expectOne('/api/retro/aliases').flush(new Blob([JSON.stringify({ aliases })]));

  return { fixture, element, http };
}

function headingBecomes(screen: Screen, text: string): Promise<void> {
  return settled(screen, () =>
    expect(screen.element.querySelector('h2')!.textContent!.trim()).toBe(text),
  );
}

function select(element: HTMLElement): HTMLSelectElement {
  return element.querySelector<HTMLSelectElement>('[data-testid="language-select"]')!;
}

function choose(element: HTMLElement, value: string): void {
  const control = select(element);
  control.value = value;
  control.dispatchEvent(new Event('change'));
}

describe('Settings', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Settings, translocoTesting()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: API_BASE_URL, useValue: '' },
        { provide: SYSTEM_LANGUAGE, useValue: 'da-DK' },
      ],
    }).compileComponents();
  });

  it('should offer the system alongside both languages, and label the choice', async () => {
    const { element } = await open(null);

    const label = element.querySelector<HTMLLabelElement>('label[for="settings-language"]')!;
    expect(label.textContent!.trim()).toBe('Sprog');
    expect(label.htmlFor).toBe(select(element).id);

    const options = [...select(element).options];
    expect(options.map((o) => o.value)).toEqual(['system', 'da', 'en']);
    expect(options.map((o) => o.textContent!.trim())).toEqual([
      'Følg systemet',
      'Dansk',
      'Engelsk',
    ]);
    expect(select(element).value).toBe('system');
  });

  it('should show the stored language as the one in force', async () => {
    const { element } = await open('en');

    expect(select(element).value).toBe('en');
    expect(element.querySelector('h2')!.textContent!.trim()).toBe('Settings');
  });

  it('should store a chosen language and translate the page at once', async () => {
    const screen = await open(null);

    choose(screen.element, 'en');

    const saved = screen.http.expectOne('/api/settings');
    expect(saved.request.method).toBe('PUT');
    expect(JSON.parse(saved.request.body)).toEqual({ language: 'en' });
    saved.flush(new Blob([JSON.stringify({ language: 'en' })]));

    await headingBecomes(screen, 'Settings');
  });

  it('should clear the stored language when the system is chosen again', async () => {
    const screen = await open('en');

    choose(screen.element, 'system');

    const saved = screen.http.expectOne('/api/settings');
    expect(JSON.parse(saved.request.body)).toEqual({});
    saved.flush(new Blob([JSON.stringify({ language: null })]));

    await headingBecomes(screen, 'Indstillinger');
    expect(select(screen.element).value).toBe('system');
  });

  it('should list the aliases with a labelled button for each', async () => {
    const screen = await open(null, ['TH', 'Thomas Hjorth']);

    const rows = await settled(screen, () => {
      const found = screen.element.querySelectorAll('[data-testid="alias-row"]');
      expect(found).toHaveLength(2);
      return found;
    });

    const remove = rows[1].querySelector<HTMLButtonElement>('[data-testid="remove-alias"]')!;
    expect(remove.getAttribute('aria-label')).toBe('Fjern Thomas Hjorth');

    remove.click();

    const saved = screen.http.expectOne('/api/retro/aliases');
    expect(saved.request.method).toBe('PUT');
    expect(JSON.parse(saved.request.body)).toEqual({ aliases: ['TH'] });
  });

  it('should add an alias on Enter', async () => {
    const screen = await open(null, ['TH']);
    await settled(screen, () =>
      expect(screen.element.querySelectorAll('[data-testid="alias-row"]')).toHaveLength(1),
    );

    const input = screen.element.querySelector<HTMLInputElement>('[data-testid="alias-input"]')!;
    input.value = '  Mette Kirkegaard  ';
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));

    const saved = screen.http.expectOne('/api/retro/aliases');
    expect(JSON.parse(saved.request.body)).toEqual({ aliases: ['TH', 'Mette Kirkegaard'] });
    expect(input.value).toBe('');
  });

  it('should send no alias request for a blank or repeated name', async () => {
    const screen = await open(null, ['TH']);
    await settled(screen, () =>
      expect(screen.element.querySelectorAll('[data-testid="alias-row"]')).toHaveLength(1),
    );

    const input = screen.element.querySelector<HTMLInputElement>('[data-testid="alias-input"]')!;
    for (const value of ['   ', 'TH']) {
      input.value = value;
      input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));
    }

    screen.http.verify();
    expect(input.value).toBe('TH');
  });

  it('should show the reason the server rejected an alias list', async () => {
    const screen = await open(null, ['Thomas']);
    await settled(screen, () =>
      expect(screen.element.querySelectorAll('[data-testid="alias-row"]')).toHaveLength(1),
    );

    const input = screen.element.querySelector<HTMLInputElement>('[data-testid="alias-input"]')!;
    input.value = 'thomas';
    input.dispatchEvent(new KeyboardEvent('keyup', { key: 'Enter' }));

    screen.http
      .expectOne('/api/retro/aliases')
      .flush(
        new Blob([JSON.stringify({ code: 'retro.duplicateAlias', message: 'Duplicate alias.' })]),
        { status: 400, statusText: 'Bad Request' },
      );

    const error = await settled(screen, () => {
      const found = screen.element.querySelector('[data-testid="alias-error"]');
      expect(found).not.toBeNull();
      return found!;
    });

    expect(error.textContent).toContain('Det samme navn står på listen mere end én gang.');
  });
});
