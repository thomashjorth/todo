import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { AuthStore } from './auth-store';
import { RELOAD } from './reload';

function configure(): {
  store: AuthStore;
  http: HttpTestingController;
  reload: ReturnType<typeof vi.fn>;
} {
  const reload = vi.fn();

  TestBed.configureTestingModule({
    imports: [translocoTesting()],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: API_BASE_URL, useValue: '' },
      { provide: RELOAD, useValue: reload },
    ],
  });

  return { store: TestBed.inject(AuthStore), http: TestBed.inject(HttpTestingController), reload };
}

describe('AuthStore', () => {
  // A whole reload rather than a navigation: the shell asked for the review, the tags and the model
  // before anyone had logged in, got 401s, and does not ask again on its own.
  it('should send the password and reload the app at its root when it is accepted', async () => {
    const { store, http, reload } = configure();

    const login = store.login('hemmelig');
    const request = http.expectOne('/api/auth/login');
    expect(JSON.parse(request.request.body)).toEqual({ password: 'hemmelig' });
    request.flush(null, { status: 204, statusText: 'No Content' });
    await login;

    expect(reload).toHaveBeenCalledWith('/');
    expect(store.error()).toBeNull();
  });

  it('should say the password was wrong and stay where it is', async () => {
    const { store, http, reload } = configure();

    const login = store.login('forkert');
    http
      .expectOne('/api/auth/login')
      .flush(new Blob([JSON.stringify({ code: 'auth.wrongPassword', message: 'Wrong.' })]), {
        status: 400,
        statusText: 'Bad Request',
      });
    await login;

    expect(reload).not.toHaveBeenCalled();
    expect(store.error()).toBe('Forkert adgangskode.');
  });

  // The 429 is declared in the contract, which is what makes the client read its body as an
  // ApiError; without the declaration this would read "Noget gik galt".
  it('should say when there have been too many attempts', async () => {
    const { store, http } = configure();

    const login = store.login('forkert');
    http
      .expectOne('/api/auth/login')
      .flush(new Blob([JSON.stringify({ code: 'auth.tooManyAttempts', message: 'Wait.' })]), {
        status: 429,
        statusText: 'Too Many Requests',
      });
    await login;

    expect(store.error()).toBe('For mange forsøg. Vent et minut, og prøv igen.');
  });
});
