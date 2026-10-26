import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { Login } from './login';
import { RELOAD } from './reload';

function open(): { element: HTMLElement; http: HttpTestingController; detect: () => void } {
  TestBed.configureTestingModule({
    imports: [Login, translocoTesting()],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: API_BASE_URL, useValue: '' },
      { provide: RELOAD, useValue: vi.fn() },
    ],
  });

  const fixture = TestBed.createComponent(Login);
  fixture.detectChanges();

  return {
    element: fixture.nativeElement,
    http: TestBed.inject(HttpTestingController),
    detect: () => fixture.detectChanges(),
  };
}

describe('Login', () => {
  // A password field, so the browser hides it and a password manager knows what to fill in.
  it('should ask for the password in a password field', () => {
    const { element } = open();

    const field = element.querySelector<HTMLInputElement>('[data-testid="login-password"]')!;

    expect(field.type).toBe('password');
    expect(field.autocomplete).toBe('current-password');
  });

  it('should send what was typed when the form is submitted', () => {
    const { element, http } = open();

    element.querySelector<HTMLInputElement>('[data-testid="login-password"]')!.value = 'hemmelig';
    element
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));

    expect(JSON.parse(http.expectOne('/api/auth/login').request.body)).toEqual({
      password: 'hemmelig',
    });
  });

  it('should say beneath the field why the password was refused', async () => {
    const { element, http, detect } = open();

    element.querySelector<HTMLInputElement>('[data-testid="login-password"]')!.value = 'forkert';
    element
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    http
      .expectOne('/api/auth/login')
      .flush(new Blob([JSON.stringify({ code: 'auth.wrongPassword', message: 'Wrong.' })]), {
        status: 400,
        statusText: 'Bad Request',
      });

    const error = await vi.waitFor(() => {
      detect();
      const line = element.querySelector('[data-testid="login-error"]');
      expect(line).not.toBeNull();
      return line!;
    });

    expect(error.textContent!.trim()).toBe('Forkert adgangskode.');
    expect(error.getAttribute('role')).toBe('alert');
  });
});
