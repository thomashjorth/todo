import { TestBed } from '@angular/core/testing';
import { ReducedMotion } from './reduced-motion';

/** Only what the service actually calls, so the stub cannot pretend to more than it is. */
interface FakeMedia {
  matches: boolean;
  addEventListener(type: string, handler: (event: { matches: boolean }) => void): void;
}

describe('ReducedMotion', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({}).compileComponents();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  /**
   * The guard, and the first assertion is what gives it teeth: without it the test would pass
   * because jsdom answered `matches: false`, which is a different thing entirely and would leave
   * the guard itself unmeasured. Measured against jsdom 28.1.0, where `matchMedia` is `undefined`.
   *
   * False is the right answer for a missing query rather than a safe default: every Vitest that
   * touches `TaskStore.load` runs here, and a service that claimed reduced motion would switch the
   * transition off in the one environment that cannot report the setting either way.
   */
  it('should report full motion where the environment has no matchMedia', () => {
    expect(typeof window.matchMedia).toBe('undefined');

    expect(TestBed.inject(ReducedMotion).reduce()).toBe(false);
  });

  it('should ask for prefers-reduced-motion and start out at what the query already answers', () => {
    const queries: string[] = [];
    const media: FakeMedia = { matches: true, addEventListener: () => {} };
    vi.stubGlobal('matchMedia', (query: string) => {
      queries.push(query);
      return media;
    });

    const reducedMotion = TestBed.inject(ReducedMotion);

    // Pinned rather than incidental: `reduce` is the one value that means the user asked for less
    // motion, and `no-preference` is a different query that would answer the opposite.
    expect(queries).toEqual(['(prefers-reduced-motion: reduce)']);
    expect(reducedMotion.reduce()).toBe(true);
  });

  it('should follow the setting in both directions while the app is running', () => {
    let listener: ((event: { matches: boolean }) => void) | undefined;
    const media: FakeMedia = {
      matches: false,
      addEventListener: (_type, handler) => {
        listener = handler;
      },
    };
    vi.stubGlobal('matchMedia', () => media);

    const reducedMotion = TestBed.inject(ReducedMotion);
    expect(reducedMotion.reduce()).toBe(false);

    listener?.({ matches: true });
    expect(reducedMotion.reduce()).toBe(true);

    // Both directions, because a listener that only ever switched motion off would pass a one-way
    // assertion - and Windows lets the setting go back.
    listener?.({ matches: false });
    expect(reducedMotion.reduce()).toBe(false);
  });
});
