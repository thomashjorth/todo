import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { AdoStore } from '../ado/ado-store';
import { JiraStore } from '../jira/jira-store';
import { RetroStore } from '../retro/retro-store';
import { HelperFrame } from './helper-frame';
import { SettingsSection } from './settings-section';
import { TagStore } from '../tags/tag-store';
import { tagChip, tagColors, tagColourKeys } from '../tags/tag-colors';
import { TagRef } from '../api/todo-client';
import { SettingsStore } from './settings-store';

/** Anything the browser's number field can hand over that is not a whole number of days. */
const wholeNumber = /^\d+$/;

const languageOptions = ['system', 'da', 'en'] as const;

/** The groups. Only used as a type here — the page names them one by one in the template. */
type SettingsSectionName =
  'language' | 'delegate' | 'tags' | 'connections' | 'jira' | 'ado' | 'retro' | 'review';

/** Sunday first, the way DayOfWeek numbers them, so the value is the index and not a lookup. */
const weekdayNames = [
  'sunday',
  'monday',
  'tuesday',
  'wednesday',
  'thursday',
  'friday',
  'saturday',
] as const;

@Component({
  selector: 'app-settings',
  imports: [HelperFrame, SettingsSection, TranslocoPipe],
  templateUrl: './settings.html',
  // The shell's ceiling is raised on xl so the task list can be two columns. This screen is not two
  // columns, and a form stretched over 1440 px is unreadable - so it puts the ceiling back.
  host: {
    class: 'block xl:max-w-2xl',
  },
})
export class Settings {
  protected readonly tagChip = tagChip;

  protected readonly settings = inject(SettingsStore);
  protected readonly tagStore = inject(TagStore);
  protected readonly retro = inject(RetroStore);
  protected readonly jira = inject(JiraStore);
  protected readonly ado = inject(AdoStore);

  protected readonly options = languageOptions;

  protected readonly weekdays = [0, 1, 2, 3, 4, 5, 6];

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

  /** The same fetched list a third time, for the statuses that mean the issue is finished. */
  protected readonly doneStatusOptions = computed(() =>
    this.optionsFor(this.settings.jiraDoneStatuses()),
  );

  /**
   * The Azure DevOps states to pick from: what the server answered, plus anything already chosen it
   * did not mention. The second half matters more here than it does for Jira, because the list comes
   * off the user's own work items rather than off a project definition - so a state nothing is in
   * today is missing from the answer, and without this it could never be unticked again.
   */
  protected readonly stateOptions = computed(() => {
    const fetched = this.ado.states();
    // Both chosen lists, deduplicated: one fetched answer serves both pickers, and a state the
    // answer did not mention has to stay tickable whichever list put it there. A plain concat
    // would show a state twice when it sits in both lists and in neither fetched answer.
    const chosen = [...this.settings.adoWaitingStates(), ...this.settings.adoDoneStates()];
    const extra = new Set(chosen.filter((name) => !fetched.includes(name)));

    return [...fetched, ...extra];
  });

  constructor() {
    void this.retro.loadAliases();
  }

  protected weekdayKey(day: number): string {
    return `settings.review.days.${weekdayNames[day]}`;
  }

  protected chooseWeekday(value: string): void {
    this.settings.saveReview({ reviewWeekday: Number(value) }).catch(() => {});
  }

  protected chooseTime(value: string): void {
    // An empty field is what a cleared time input reads as, and the server would refuse it. The
    // stored value stays instead - clearing the box is not a way to say anything.
    if (value) {
      this.settings.saveReview({ reviewTimeOfDay: value }).catch(() => {});
    }
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

  /** The field is emptied only once the tag exists, so a refused name stays to be corrected. */
  protected addTag(input: HTMLInputElement): void {
    const name = input.value.trim();
    if (!name) {
      return;
    }

    void this.tagStore.add(name).then((added) => {
      if (added) {
        input.value = '';
      }
    });
  }

  protected removeTag(id: number): void {
    if (this.editingTag() === id) {
      this.editingTag.set(null);
    }
    void this.tagStore.remove(id);
  }

  /** The tag whose editing is open under the chips: one at a time, none a valid state. */
  protected readonly editingTag = signal<number | null>(null);

  protected readonly editedTag = computed(() =>
    this.tagStore.tags().find((tag) => tag.id === this.editingTag()),
  );

  /** The palette as the picker shows it: one row per round, the rainbow along each. */
  protected readonly colourRows = [0, 1, 2].map((round) =>
    tagColors.slice(round * 7, round * 7 + 7).map((_, i) => round * 7 + i),
  );

  protected toggleTagEdit(id: number): void {
    this.editingTag.update((open) => (open === id ? null : id));
  }

  /**
   * On Enter or blur, like every field. An empty name puts the old one back, like a task's title; a
   * refused one stays in the field to be corrected, with the store's error beside it.
   */
  protected saveTagName(tag: TagRef, field: HTMLInputElement): void {
    const trimmed = field.value.trim();
    field.value = trimmed || tag.name;

    if (trimmed && trimmed !== tag.name) {
      void this.tagStore.update(tag.id, trimmed, tag.color);
    }
  }

  protected setTagColour(tag: TagRef, slot: number): void {
    if (slot !== tag.color) {
      void this.tagStore.update(tag.id, tag.name, slot);
    }
  }

  protected readonly colourKeys = tagColourKeys;

  protected removeDelegate(name: string): void {
    void this.settings.saveDelegates(this.settings.delegates().filter((d) => d !== name));
  }

  protected saveProjectKey(value: string): void {
    void this.settings.saveJira({ jiraProjectKey: value });
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

  protected isDoneStatus(status: string): boolean {
    return this.settings.jiraDoneStatuses().includes(status);
  }

  protected toggleDoneStatus(status: string, done: boolean): void {
    const current = this.settings.jiraDoneStatuses();
    if (done === current.includes(status)) {
      return;
    }

    void this.settings.saveJira({
      jiraDoneStatuses: done ? [...current, status] : current.filter((n) => n !== status),
    });
  }

  protected saveAdoProject(value: string): void {
    void this.settings.saveAdo({ adoProject: value });
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

  protected isDoneState(state: string): boolean {
    return this.settings.adoDoneStates().includes(state);
  }

  protected toggleDoneState(state: string, done: boolean): void {
    const current = this.settings.adoDoneStates();
    if (done === current.includes(state)) {
      return;
    }

    void this.settings.saveAdo({
      adoDoneStates: done ? [...current, state] : current.filter((n) => n !== state),
    });
  }

  protected setAdoIncludeWaiting(include: boolean): void {
    void this.settings.saveAdo({ adoIncludeWaiting: include });
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
