import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { unauthorizedInterceptor } from './unauthorized.interceptor';

function configure(): {
  http: HttpClient;
  backend: HttpTestingController;
  navigate: ReturnType<typeof vi.spyOn>;
} {
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      provideHttpClient(withInterceptors([unauthorizedInterceptor])),
      provideHttpClientTesting(),
    ],
  });

  const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

  return {
    http: TestBed.inject(HttpClient),
    backend: TestBed.inject(HttpTestingController),
    navigate,
  };
}

async function answer(status: number, statusText: string): Promise<void> {
  const http = TestBed.inject(HttpClient);
  const backend = TestBed.inject(HttpTestingController);
  const call = firstValueFrom(http.get('/api/tasks')).catch(() => {});
  backend.expectOne('/api/tasks').flush(null, { status, statusText });
  await call;
}

describe('unauthorizedInterceptor', () => {
  afterEach(() => window.history.replaceState({}, '', '/'));

  it('should send a 401 to the login page', async () => {
    const { navigate } = configure();

    await answer(401, 'Unauthorized');

    expect(navigate).toHaveBeenCalledWith('/login');
  });

  it('should leave any other failure to the screen that asked', async () => {
    const { navigate } = configure();

    await answer(400, 'Bad Request');

    expect(navigate).not.toHaveBeenCalled();
  });

  // The address bar rather than the router: the initializer and the shell's own loads run before
  // the router's first navigation, when router.url still says "/" on a page that is /login.
  it('should not send a 401 to the login page when the page already is the login page', async () => {
    window.history.replaceState({}, '', '/login');
    const { navigate } = configure();

    await answer(401, 'Unauthorized');

    expect(navigate).not.toHaveBeenCalled();
  });

  it('should pass the failure on to the caller', async () => {
    const { http, backend } = configure();

    const call = firstValueFrom(http.get('/api/tasks'));
    backend.expectOne('/api/tasks').flush(null, { status: 401, statusText: 'Unauthorized' });

    await expect(call).rejects.toMatchObject({ status: 401 });
  });
});
