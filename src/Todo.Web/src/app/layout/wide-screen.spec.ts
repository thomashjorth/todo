import { TestBed } from '@angular/core/testing';
import { WideScreen } from './wide-screen';

/** Only what the service actually calls, so the stub cannot pretend to more than it is. */
interface FakeMedia {
  matches: boolean;
  addEventListener(type: string, handler: (event: { matches: boolean }) => void): void;
}

describe('WideScreen', () => {
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
   */
  it('should report a narrow window where the environment has no matchMedia', () => {
    expect(typeof window.matchMedia).toBe('undefined');

    expect(TestBed.inject(WideScreen).wide()).toBe(false);
  });

  it('should ask for Tailwind xl and start out at what the query already answers', () => {
    const queries: string[] = [];
    const media: FakeMedia = { matches: true, addEventListener: () => {} };
    vi.stubGlobal('matchMedia', (query: string) => {
      queries.push(query);
      return media;
    });

    const wideScreen = TestBed.inject(WideScreen);

    // Pinned rather than incidental: the number is the same breakpoint the `xl:` classes lay the
    // columns out on, and a drift between the two would put the panel in a column that is not there.
    expect(queries).toEqual(['(min-width: 80rem)']);
    expect(wideScreen.wide()).toBe(true);
  });

  it('should follow the window across the breakpoint in both directions', () => {
    let listener: ((event: { matches: boolean }) => void) | undefined;
    const media: FakeMedia = {
      matches: false,
      addEventListener: (_type, handler) => {
        listener = handler;
      },
    };
    vi.stubGlobal('matchMedia', () => media);

    const wideScreen = TestBed.inject(WideScreen);
    expect(wideScreen.wide()).toBe(false);

    listener?.({ matches: true });
    expect(wideScreen.wide()).toBe(true);

    // Both directions, because a listener that only ever widened would pass a one-way assertion.
    listener?.({ matches: false });
    expect(wideScreen.wide()).toBe(false);
  });
});
