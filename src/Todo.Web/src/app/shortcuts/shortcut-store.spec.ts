import { ShortcutStore } from './shortcut-store';

describe('ShortcutStore', () => {
  it('should activate a registered target and report that it handled the key', () => {
    const store = new ShortcutStore();
    let activated = 0;
    store.register('n', () => activated++);

    expect(store.activate('n')).toBe(true);
    expect(activated).toBe(1);
  });

  it('should report an unregistered key as unhandled so the event is not swallowed', () => {
    const store = new ShortcutStore();

    expect(store.activate('q')).toBe(false);
  });

  it('should match the key case-insensitively, because Alt+Shift reports an upper-case key', () => {
    const store = new ShortcutStore();
    let activated = 0;
    store.register('n', () => activated++);

    expect(store.activate('N')).toBe(true);
    expect(activated).toBe(1);
  });

  it('should stop activating a target that has been unregistered', () => {
    const store = new ShortcutStore();
    store.register('n', () => {
      throw new Error('should not run');
    });
    store.unregister('n');

    expect(store.activate('n')).toBe(false);
  });

  // To rækker der bytter numre: den nye registrering på '3' må ikke blive slettet af den gamle
  // indehavers oprydning. Uden vagten er nøglen væk, og Alt+3 gør ingenting.
  it('should keep a registration a newer caller has taken over', () => {
    const store = new ShortcutStore();
    const old = () => {
      throw new Error('should not run');
    };
    let activated = 0;
    const current = () => activated++;

    store.register('alt+3', old);
    store.register('alt+3', current);
    store.unregister('alt+3', old);

    expect(store.activate('alt+3')).toBe(true);
    expect(activated).toBe(1);
  });

  // The labels on the buttons are only shown while Alt is down, so they follow this signal.
  it('should start with Alt released and follow every change', () => {
    const store = new ShortcutStore();

    expect(store.altHeld()).toBe(false);

    store.setAltHeld(true);
    expect(store.altHeld()).toBe(true);

    store.setAltHeld(false);
    expect(store.altHeld()).toBe(false);
  });
});
