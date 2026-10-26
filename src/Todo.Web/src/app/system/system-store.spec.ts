import { TestBed } from '@angular/core/testing';
import { translocoTesting } from '../i18n/transloco.testing';
import { SystemStore } from './system-store';

function configure(): SystemStore {
  TestBed.configureTestingModule({ imports: [translocoTesting()] });

  return TestBed.inject(SystemStore);
}

describe('SystemStore', () => {
  let open: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    // jsdom's window.open does nothing useful; the spy is both the measurement and the stop.
    open = vi.spyOn(window, 'open').mockReturnValue(null);
  });

  afterEach(() => open.mockRestore());

  it('should open a web link in a new browser context without an opener', () => {
    const store = configure();

    store.openLink('https://example.com/docs');

    expect(open).toHaveBeenCalledWith('https://example.com/docs', '_blank', 'noopener');
    expect(store.error()).toBeNull();
  });

  // Marked turns a bare address in a note into a mailto: link, so refusing the scheme would make an
  // ordinary note look broken.
  it('should open a mailto link', () => {
    const store = configure();

    store.openLink('mailto:someone@example.com');

    expect(open).toHaveBeenCalledWith('mailto:someone@example.com', '_blank', 'noopener');
  });

  // Nothing happens on screen when a link cannot be opened, so the reason has to be said out loud.
  it('should refuse a file link and say why', () => {
    const store = configure();

    store.openLink('file:///C:/Windows');

    expect(open).not.toHaveBeenCalled();
    expect(store.error()).toBe('Kun web- og mail-links kan åbnes.');
  });

  // Angular's sanitizer turns a javascript: link into "unsafe:javascript:…", and that is the form
  // link.href has when the note is clicked. The refusal has to hold for that form, not only the raw.
  it('should refuse a link the sanitizer has marked unsafe', () => {
    const store = configure();

    store.openLink('unsafe:javascript:alert(1)');

    expect(open).not.toHaveBeenCalled();
    expect(store.error()).not.toBeNull();
  });

  it('should refuse text that is not a URL at all', () => {
    const store = configure();

    store.openLink('ikke en adresse');

    expect(open).not.toHaveBeenCalled();
    expect(store.error()).not.toBeNull();
  });

  // The order is the test's teeth: the refusal comes BEFORE the valid link, or a store that never set
  // the error at all would pass.
  it('should clear an earlier refusal when the next link opens', () => {
    const store = configure();

    store.openLink('file:///C:/Windows');
    expect(store.error()).not.toBeNull();

    store.openLink('https://example.com/docs');

    expect(store.error()).toBeNull();
  });

  it('should drop the message once it no longer applies', () => {
    const store = configure();

    store.openLink('file:///C:/Windows');
    store.clearError();

    expect(store.error()).toBeNull();
  });
});
