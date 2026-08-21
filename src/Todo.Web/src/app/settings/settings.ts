import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { AdoStore } from '../ado/ado-store';
import { JiraStore } from '../jira/jira-store';
import { RetroStore } from '../retro/retro-store';
import { SettingsSection } from './settings-section';
import { SettingsStore } from './settings-store';

/** Anything the browser's number field can hand over that is not a whole number of days. */
const wholeNumber = /^\d+$/;

const languageOptions = ['system', 'da', 'en'] as const;

/** The five groups. Only used as a type here — the page names them one by one in the template. */
type SettingsSectionName = 'language' | 'delegate' | 'jira' | 'ado' | 'retro';

@Component({
  selector: 'app-settings',
  imports: [SettingsSection, TranslocoPipe],
  templateUrl: './settings.html',
  // Skallens loft er hævet på xl, så to spalter har plads på opgavelisten. Den her skærm er
  // ikke to spalter, og en formular strakt over 1440 px er ulæselig — så den sætter loftet igen.
  host: {
    class: 'block xl:max-w-2xl',
  },
})
export class Settings {
  protected readonly settings = inject(SettingsStore);
  protected readonly retro = inject(RetroStore);
  protected readonly jira = inject(JiraStore);
  protected readonly ado = inject(AdoStore);

  protected readonly options = languageOptions;

  /**
   * The token being typed. It lives here rather than in the store on purpose: a store is a
   * singleton, so a token left in one would survive navigating away from this page.
   */
  protected readonly token = signal('');

  /** The Azure DevOps token being typed, its own field for the same reason and never mixed in. */
  protected readonly adoToken = signal('');

  /**
   * Which group is unfolded, where `null` — nothing open — is both the state the page arrives in
   * and a state a click can return to, as an accordion's own convention has it.
   *
   * It lives here rather than in a setting on the server, and the choice has a price: navigating to
   * an import screen and back folds the page up again. A stored one would cost a field on the
   * contract, a row in `Setting` and a round trip for something the user did not ask for, so the
   * fold is treated as a view state. To undo the choice, move this into `SettingsStore`.
   */
  protected readonly openSection = signal<SettingsSectionName | null>(null);

  // Following the system is the absence of a stored language, so it needs a name of its own here.
  protected readonly choice = computed(() => this.settings.language() ?? 'system');

  /** The statuses that can be ticked as "I am waiting for it". */
  protected readonly statusOptions = computed(() =>
    this.optionsFor(this.settings.jiraWaitingStatuses()),
  );

  /**
   * The same fetched list, offered a second time for the duty pool. There is deliberately no second
   * call: GET /api/jira/statuses answers with every status in the project, and both questions are
   * asked of that one answer.
   */
  protected readonly dutyStatusOptions = computed(() =>
    this.optionsFor(this.settings.jiraDutyStatuses()),
  );

  /**
   * The Azure DevOps states to pick from: what the server answered, plus anything already chosen it
   * did not mention. The second half matters more here than it does for Jira, because the list comes
   * off the user's own work items rather than off a project definition - so a state nothing is in
   * today is missing from the answer, and without this it could never be unticked again.
   */
  protected readonly stateOptions = computed(() => {
    const fetched = this.ado.states();
    const chosen = this.settings.adoWaitingStates();

    return [...fetched, ...chosen.filter((name) => !fetched.includes(name))];
  });

  constructor() {
    void this.retro.loadAliases();
  }

  protected isOpen(section: SettingsSectionName): boolean {
    return this.openSection() === section;
  }

  /**
   * One open group at most, and clicking the open one closes it. Focus deliberately stays on the
   * heading button rather than moving into the panel: that is the accordion convention — the user
   * tabs on from where they are — and moving it would leave nothing to Shift+Tab back to when the
   * click was a mistake. The shortcut lesson does not apply here: this <em>is</em> the element's
   * own activation, not a key standing in for one.
   */
  protected toggleSection(section: SettingsSectionName): void {
    this.openSection.update((current) => (current === section ? null : section));
  }

  protected languageKey(option: string): string {
    return `settings.languages.${option}`;
  }

  protected choose(option: string): void {
    void this.settings.choose(option === 'system' ? null : option);
  }

  /**
   * The same shape as addAlias, and the repeat check is deliberately exact: the server dedupes
   * case-insensitively and answers 400, so a name that differs only in case is refused with a reason
   * rather than dropped here in silence.
   */
  protected addDelegate(input: HTMLInputElement): void {
    const name = input.value.trim();
    if (!name || this.settings.delegates().includes(name)) {
      return;
    }

    input.value = '';
    void this.settings.saveDelegates([...this.settings.delegates(), name]);
  }

  protected removeDelegate(name: string): void {
    void this.settings.saveDelegates(this.settings.delegates().filter((d) => d !== name));
  }

  protected saveBaseUrl(value: string): void {
    void this.settings.saveJira({ jiraBaseUrl: value });
  }

  protected saveProjectKey(value: string): void {
    void this.settings.saveJira({ jiraProjectKey: value });
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

    void this.settings.saveJira({
      jiraWaitingStatuses: waiting ? [...current, status] : current.filter((n) => n !== status),
    });
  }

  protected setIncludeWaiting(include: boolean): void {
    void this.settings.saveJira({ jiraIncludeWaiting: include });
  }

  protected isDutyStatus(status: string): boolean {
    return this.settings.jiraDutyStatuses().includes(status);
  }

  protected toggleDutyStatus(status: string, duty: boolean): void {
    const current = this.settings.jiraDutyStatuses();
    if (duty === current.includes(status)) {
      return;
    }

    void this.settings.saveJira({
      jiraDutyStatuses: duty ? [...current, status] : current.filter((n) => n !== status),
    });
  }

  protected setOnDuty(onDuty: boolean): void {
    void this.settings.saveJira({ jiraOnDuty: onDuty });
  }

  protected saveAdoBaseUrl(value: string): void {
    void this.settings.saveAdo({ adoBaseUrl: value });
  }

  protected saveAdoProject(value: string): void {
    void this.settings.saveAdo({ adoProject: value });
  }

  /** The blank check is the server's, as it is for Jira's token: the answer teaches more. */
  protected async storeAdoToken(): Promise<void> {
    if (await this.settings.setAdoToken(this.adoToken())) {
      this.adoToken.set('');
    }
  }

  protected clearAdoToken(): void {
    void this.settings.clearAdoToken();
  }

  protected isWaitingState(state: string): boolean {
    return this.settings.adoWaitingStates().includes(state);
  }

  protected toggleWaitingState(state: string, waiting: boolean): void {
    const current = this.settings.adoWaitingStates();
    if (waiting === current.includes(state)) {
      return;
    }

    void this.settings.saveAdo({
      adoWaitingStates: waiting ? [...current, state] : current.filter((n) => n !== state),
    });
  }

  protected setAdoIncludeWaiting(include: boolean): void {
    void this.settings.saveAdo({ adoIncludeWaiting: include });
  }

  /**
   * Not through `saveAdo` or any other `put`: autostart has its own route, because PUT
   * /api/settings is a full replacement that would read the absent field as "clear". The checkbox
   * is bound to the signal rather than to its own state, so a registry that refuses puts the tick
   * back where it was.
   */
  protected setAutostart(input: HTMLInputElement): void {
    void this.settings.setAutostart(input.checked).then(() => {
      // Written back from the signal, and this is the bug an E2E journey found rather than a
      // flourish. The browser ticks the box itself on click, and `[checked]` only re-applies when
      // the signal *changes* - so on a machine whose registry refuses, the signal stays false, the
      // binding sees nothing to do, and the tick stays on while nothing was registered. The switch
      // has to end up showing what the machine says, not what the click intended.
      input.checked = this.settings.autostart();
    });
  }

  /**
   * The same shape as addDelegate, and the repeat check is exact for a different reason: the server
   * compares work item types ordinally, because Azure DevOps keeps two names apart that differ only
   * in case. Folding them here would merge two types the system does not.
   */
  protected addWorkItemType(input: HTMLInputElement): void {
    const type = input.value.trim();
    if (!type || this.settings.adoWorkItemTypes().includes(type)) {
      return;
    }

    input.value = '';
    void this.settings.saveAdo({
      adoWorkItemTypes: [...this.settings.adoWorkItemTypes(), type],
    });
  }

  /**
   * Removing the last type is deliberately allowed through to the server, which refuses it with
   * ado.workItemTypesRequired. A guard here would either restore the three defaults behind the
   * user's back or leave a button that silently does nothing.
   */
  protected removeWorkItemType(type: string): void {
    void this.settings.saveAdo({
      adoWorkItemTypes: this.settings.adoWorkItemTypes().filter((t) => t !== type),
    });
  }

  /**
   * Anything but a whole number is dropped rather than sent. The field is a number input, so the only
   * value a user can realistically get in here that is not a number is an empty one - and an empty
   * field is not a number of days, while `Number('')` is `0`, which on this setting means "no
   * deadline". Out of range is another matter and does go: the server refuses 400 days with
   * ado.defaultDeadlineDaysInvalid, and being told beats being silently clamped.
   */
  protected saveDeadlineDays(value: string): void {
    const days = value.trim();
    if (!wholeNumber.test(days)) {
      return;
    }

    void this.settings.saveAdo({ adoDefaultDeadlineDays: Number(days) });
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

  /**
   * What Jira answered, plus anything already chosen that Jira did not mention. Without the second
   * half a stored status could not be unticked until the connection worked again.
   */
  private optionsFor(chosen: readonly string[]): string[] {
    const fetched = this.jira.statuses();

    return [...fetched, ...chosen.filter((name) => !fetched.includes(name))];
  }
}
