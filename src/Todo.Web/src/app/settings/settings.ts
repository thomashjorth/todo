import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { JiraStore } from '../jira/jira-store';
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
  protected readonly jira = inject(JiraStore);

  protected readonly options = languageOptions;

  /**
   * The token being typed. It lives here rather than in the store on purpose: a store is a
   * singleton, so a token left in one would survive navigating away from this page.
   */
  protected readonly token = signal('');

  // Following the system is the absence of a stored language, so it needs a name of its own here.
  protected readonly choice = computed(() => this.settings.language() ?? 'system');

  /**
   * The statuses that can be ticked: what Jira answered, plus any already on the waiting list.
   * Without the second half a stored status could not be unticked until the connection worked.
   */
  protected readonly statusOptions = computed(() => {
    const chosen = this.settings.jiraWaitingStatuses();

    return [
      ...this.jira.statuses(),
      ...chosen.filter((name) => !this.jira.statuses().includes(name)),
    ];
  });

  constructor() {
    void this.retro.loadAliases();
  }

  protected languageKey(option: string): string {
    return `settings.languages.${option}`;
  }

  protected choose(option: string): void {
    void this.settings.choose(option === 'system' ? null : option);
  }

  protected saveBaseUrl(value: string): void {
    void this.settings.save({ jiraBaseUrl: value });
  }

  protected saveProjectKey(value: string): void {
    void this.settings.save({ jiraProjectKey: value });
  }

  /**
   * The blank check is the server's, not this method's: it is the only validation there is, and a
   * button that quietly does nothing teaches the user less than the answer does.
   */
  protected async storeToken(): Promise<void> {
    if (await this.settings.setToken(this.token())) {
      this.token.set('');
    }
  }

  protected clearToken(): void {
    void this.settings.clearToken();
  }

  protected isWaiting(status: string): boolean {
    return this.settings.jiraWaitingStatuses().includes(status);
  }

  protected toggleWaiting(status: string, waiting: boolean): void {
    const current = this.settings.jiraWaitingStatuses();
    if (waiting === current.includes(status)) {
      return;
    }

    void this.settings.save({
      jiraWaitingStatuses: waiting ? [...current, status] : current.filter((n) => n !== status),
    });
  }

  protected setIncludeWaiting(include: boolean): void {
    void this.settings.save({ jiraIncludeWaiting: include });
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
