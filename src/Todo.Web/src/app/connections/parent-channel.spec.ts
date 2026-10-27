import { TestBed } from '@angular/core/testing';

import { PAGE_SEARCH, PARENT_WINDOW, ParentChannel, ParentWindow } from './parent-channel';

function channel(search: string, parent: ParentWindow | null) {
  TestBed.configureTestingModule({
    providers: [
      { provide: PAGE_SEARCH, useValue: search },
      { provide: PARENT_WINDOW, useValue: parent },
    ],
  });

  return TestBed.inject(ParentChannel);
}

describe('ParentChannel', () => {
  it('should post only to the origin the app named, stamped with its sender', () => {
    const parent = { postMessage: vi.fn() };

    channel('?parent=http://nas.test:8080/settings&lang=en', parent).post({ type: 'ready' });

    expect(parent.postMessage).toHaveBeenCalledWith(
      { type: 'ready', source: 'todo-helper' },
      'http://nas.test:8080',
    );
  });

  it('should post nothing when the named parent is not an http origin', () => {
    const parent = { postMessage: vi.fn() };

    const subject = channel('?parent=javascript:alert(1)', parent);
    subject.post({ type: 'changed' });

    expect(parent.postMessage).not.toHaveBeenCalled();
    expect(subject.framed).toBe(false);
  });

  it('should post nothing when the page is opened on its own', () => {
    const subject = channel('?parent=http://nas.test:8080', null);

    expect(() => subject.post({ type: 'ready' })).not.toThrow();
    expect(subject.framed).toBe(false);
  });

  it('should speak the language the app named', () => {
    expect(channel('?parent=http://nas.test&lang=en', null).language).toBe('en');
  });
});
