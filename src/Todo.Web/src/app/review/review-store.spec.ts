import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { beforeEach, describe, expect, it } from 'vitest';
import { API_BASE_URL } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { ReviewStore } from './review-store';

/**
 * The wire shape, written by hand rather than built from the generated class, because it is the
 * <em>wire</em> this measures. That makes it a seventh fixture with no compiler over it, alongside
 * the six CLAUDE.md counts: a field added to ReviewStatusResponse has to be added here too, or this
 * stays green on a shape the server no longer sends.
 */
interface ReviewStatusJson {
  due: boolean;
  lastCompletedAt?: string | null;
  nextDueAt: string;
}

function body(json: ReviewStatusJson): Blob {
  return new Blob([JSON.stringify(json)]);
}

describe('ReviewStore', () => {
  let store: ReviewStore;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [translocoTesting()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '' },
      ],
    });
    store = TestBed.inject(ReviewStore);
  });

  // False rather than true, so the marker cannot flash on before the first answer lands. An app
  // that accused you of being behind for half a second on every start would be worse than one that
  // took half a second to say so.
  it('should start with nothing owed', () => {
    expect(store.due()).toBe(false);
    expect(store.lastCompletedAt()).toBeNull();
  });

  it('should read the due state the API answers', async () => {
    const loaded = store.load();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/review')
      .flush(body({ due: true, lastCompletedAt: null, nextDueAt: '2026-08-28T13:00:00Z' }));
    await loaded;

    expect(store.due()).toBe(true);
    expect(store.nextDueAt()).toBe('2026-08-28T13:00:00Z');
  });

  it('should clear the debt from the completion reply rather than guessing', async () => {
    const done = store.complete();
    const request = TestBed.inject(HttpTestingController).expectOne('/api/review/complete');
    expect(request.request.method).toBe('POST');
    request.flush(
      body({
        due: false,
        lastCompletedAt: '2026-08-26T09:00:00Z',
        nextDueAt: '2026-09-04T13:00:00Z',
      }),
    );
    await done;

    expect(store.due()).toBe(false);
    expect(store.lastCompletedAt()).toBe('2026-08-26T09:00:00Z');
  });

  // The marker is an affordance, not the app: a review state that cannot be read leaves it off
  // rather than putting an error message where a nav link should be.
  it('should stay quiet when the state cannot be read', async () => {
    const loaded = store.load();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/review')
      .flush(new Blob(['{}']), { status: 500, statusText: 'Server Error' });
    await loaded;

    expect(store.due()).toBe(false);
    expect(store.error()).toBeNull();
  });

  // Completing is a thing the user pressed, so a refusal has to be said out loud - unlike the read
  // above, which nobody asked for.
  it('should report a refused completion', async () => {
    const done = store.complete();
    TestBed.inject(HttpTestingController)
      .expectOne('/api/review/complete')
      .flush(new Blob(['{}']), { status: 500, statusText: 'Server Error' });
    await done;

    expect(store.error()).not.toBeNull();
  });
});
