import { Component, computed, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { RetroStore } from '../retro/retro-store';
import { SettingsStore } from './settings-store';

const languageOptions = ['system', 'da', 'en'] as const;

@Component({
  selector: 'app-settings',
  imports: [TranslocoPipe],
  templateUrl: './settings.html',
})
export class Settings {
  protected readonly settings = inject(SettingsStore);
  protected readonly retro = inject(RetroStore);

  protected readonly options = languageOptions;

  // Following the system is the absence of a stored language, so it needs a name of its own here.
  protected readonly choice = computed(() => this.settings.language() ?? 'system');

  constructor() {
    void this.retro.loadAliases();
  }

  protected languageKey(option: string): string {
    return `settings.languages.${option}`;
  }

  protected choose(option: string): void {
    void this.settings.choose(option === 'system' ? null : option);
  }

  protected addAlias(input: HTMLInputElement): void {
    const alias = input.value.trim();
    if (!alias || this.retro.aliases().includes(alias)) {
      return;
    }

    input.value = '';
    void this.retro.saveAliases([...this.retro.aliases(), alias]);
  }

  protected removeAlias(alias: string): void {
    void this.retro.saveAliases(this.retro.aliases().filter((a) => a !== alias));
  }
}
