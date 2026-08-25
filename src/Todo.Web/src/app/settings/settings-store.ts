import { Injectable, WritableSignal, inject, signal } from '@angular/core';
import { TranslocoService } from '@jsverse/transloco';
import { firstValueFrom } from 'rxjs';
import { apiErrorMessage } from '../api/api-error-message';
import {
  AdoTokenRequest,
  JiraTokenRequest,
  SettingsClient,
  SettingsRequest,
  SettingsResponse,
} from '../api/todo-client';
import { resolveLanguage } from '../i18n/resolve-language';
import { SYSTEM_LANGUAGE } from '../i18n/system-language';

/**
 * The fields a caller wants to change. Everything left out keeps the value the store already
 * holds — it is <em>not</em> left out of the request, because the API is a full replacement.
 */
export interface SettingsChanges {
  language?: string | null;
  delegates?: readonly string[];
  jiraBaseUrl?: string | null;
  jiraProjectKey?: string | null;
  jiraWaitingStatuses?: readonly string[];
  jiraIncludeWaiting?: boolean;
  jiraDutyStatuses?: readonly string[];
  jiraOnDuty?: boolean;
  jiraDoneStatuses?: readonly string[];
  adoBaseUrl?: string | null;
  adoProject?: string | null;
  adoWaitingStates?: readonly string[];
  adoDoneStates?: readonly string[];
  adoIncludeWaiting?: boolean;
  adoWorkItemTypes?: readonly string[];
  adoDefaultDeadlineDays?: number;
}

/**
 * The number of days the contract itself declares, read off the generated request rather than
 * written down a second time: the `= 3` initializer runs only for a `new SettingsRequest()` built
 * without a data object, and that is the only place it can be seen from here. The `?? 3` is a
 * fallback for a contract that lost its default, not a second home for the number.
 *
 * It is needed because omitting the key is how the wire spells "the default", and this is the one
 * field where the cleared value is not the falsy one: `0` means <em>no deadline</em>. A save that
 * never mentioned Azure DevOps must therefore leave the key out, and a deliberate `0` must not.
 */
const defaultDeadlineDays = new SettingsRequest().adoDefaultDeadlineDays ?? 3;

@Injectable({ providedIn: 'root' })
export class SettingsStore {
  private readonly client = inject(SettingsClient);
  private readonly transloco = inject(TranslocoService);
  private readonly system = inject(SYSTEM_LANGUAGE);

  /** The stored choice, where null means "follow the system" rather than "not read yet". */
  readonly language = signal<string | null>(null);

  /**
   * The people tasks are handed to, offered as suggestions when a task moves to WaitingFor. A
   * suggestion list, not a closed set: the who field stays free text, because waiting on somebody
   * unlisted — or on nobody at all — are both valid states. The server trims and dedupes on the way
   * in and keeps the first spelling, so what comes back can differ from what was sent; nothing here
   * normalises, because two places doing it would be two rules that look like one.
   */
  readonly delegates = signal<string[]>([]);

  readonly jiraBaseUrl = signal<string | null>(null);
  readonly jiraProjectKey = signal<string | null>(null);
  readonly jiraWaitingStatuses = signal<string[]>([]);
  readonly jiraIncludeWaiting = signal(false);

  /**
   * The statuses that mean "waiting for the shared duty pool", and whether the rotation is mine
   * right now. Both are read here only to be shown and saved — whether a given row <em>is</em> a
   * duty row is the server's answer, in JiraPreviewRow.isDuty, so the two lists are never compared
   * in the browser.
   */
  readonly jiraDutyStatuses = signal<string[]>([]);
  readonly jiraOnDuty = signal(false);

  /**
   * The statuses that mean an issue is finished. No switch beside it, unlike the two lists above:
   * doneness only ever offers to close a task you already have, so there is nothing to opt into,
   * and an empty list simply means no suggestions.
   */
  readonly jiraDoneStatuses = signal<string[]>([]);

  /**
   * Whether a token is stored. There is deliberately no signal for the token itself: it is
   * write-only, and one held here would sit in every template's scope and outlive the page.
   */
  readonly hasJiraToken = signal(false);

  /** The Azure DevOps collection URL, which may carry a `%20` for a space in the collection name. */
  readonly adoBaseUrl = signal<string | null>(null);
  readonly adoProject = signal<string | null>(null);

  /**
   * The states that mean "waiting for somebody else". States rather than statuses because that is
   * Azure DevOps' own word for it, and the two systems do not even agree on the values: the same
   * meaning is `Active` on a Bug and `In Progress` on a Test Suite.
   */
  readonly adoWaitingStates = signal<string[]>([]);

  /** The counterpart of jiraDoneStatuses, and its own list: the two systems spell different words. */
  readonly adoDoneStates = signal<string[]>([]);
  readonly adoIncludeWaiting = signal(false);

  /**
   * The work item types the import will take. Never empty as it comes back from the server, which
   * answers the three defaults for an absent row - so the empty list this starts out as only ever
   * means "not read yet".
   */
  readonly adoWorkItemTypes = signal<string[]>([]);

  /**
   * How many days ahead an imported work item gets its deadline, because Azure DevOps has no due
   * date field at all. `0` means no deadline, which is why this is a number rather than a nullable
   * one - and why it cannot be sent through a truthiness check.
   */
  readonly adoDefaultDeadlineDays = signal(defaultDeadlineDays);

  readonly hasAdoToken = signal(false);

  /**
   * The language group's line, and after the Jira group got one of its own that is all it is: the
   * only caller left is `choose`. Kept as the default `put` target rather than renamed, because a
   * future group that forgets to pass its own signal should land somewhere visible.
   */
  readonly error = signal<string | null>(null);

  /**
   * The delegate list's own message, apart from `error` so the settings page can say it beside the
   * list it is about — the same split RetroStore has for the aliases. One signal shown in two places
   * would print every rejection twice.
   */
  readonly delegatesError = signal<string | null>(null);

  /**
   * The Jira group's own message. The group went without one until the groups could fold: `error`
   * is written by every save, so a refused base URL, project key, status list or token landed on
   * the line above the language select — visible, but a screen away from the field that caused it.
   * Once a group can be folded shut that stops being merely wrong and becomes silent, which is the
   * same failure the user reported about Test connection. The token routes answer in here too, as
   * Azure DevOps' do: the field sits in this group.
   */
  readonly jiraError = signal<string | null>(null);

  /**
   * The Azure DevOps group's own message, for the same reason the delegate list has one: `error` is
   * written by every save, so a refused work item type shown there would also appear above the
   * language select the next time a language was chosen. The token routes answer in here too - the
   * token field sits in that group, and that is where its refusal has to land.
   */
  readonly adoError = signal<string | null>(null);

  async start(): Promise<void> {
    try {
      this.read(await firstValueFrom(this.client.getSettings()));
    } catch {
      // An app that cannot read its settings still has to open, in the system language.
    }

    await this.apply();
  }

  /** The language select's one path to the server, so it cannot forget the other thirteen fields. */
  async choose(language: string | null): Promise<void> {
    await this.save({ language });
  }

  /**
   * PUT /api/settings is a full replacement and the backend reads an absent field as "clear", so
   * every field goes with every save — the same reason TaskStore.update builds a `current` object.
   * A field whose value <em>is</em> the cleared one (no URL, no statuses, no delegates, waiting
   * off, off duty) is sent as absent, because that is how the wire spells cleared; language
   * especially, which the API rejects as an empty string but accepts as missing. The two Azure
   * DevOps fields where cleared is <em>not</em> the falsy value are the exceptions, and each says so
   * where it is built.
   */
  async save(changes: SettingsChanges): Promise<void> {
    await this.put(changes, this.error);
  }

  /**
   * The whole list at once, as RetroStore.saveAliases takes the whole alias list: the caller has the
   * rows on screen and knows which one it is adding or dropping. The reply is authoritative — the
   * server trims and dedupes — so the signal is set from it rather than from the argument.
   */
  async saveDelegates(delegates: readonly string[]): Promise<void> {
    await this.put({ delegates }, this.delegatesError);
  }

  /**
   * The same one path as `save`, answering into the Jira group's own line. Every field still goes
   * with it - this is which line the refusal lands on, not which fields are sent.
   */
  async saveJira(changes: SettingsChanges): Promise<void> {
    await this.put(changes, this.jiraError);
  }

  /**
   * The same one path as `save`, answering into the Azure DevOps group's own line. Every field still
   * goes with it - this is which line the refusal lands on, not which fields are sent.
   */
  async saveAdo(changes: SettingsChanges): Promise<void> {
    await this.put(changes, this.adoError);
  }

  /**
   * Answers into whichever line the caller shows, because the two error signals are two places on
   * the page rather than two kinds of failure.
   */
  private async put(changes: SettingsChanges, into: WritableSignal<string | null>): Promise<void> {
    const next: SettingsChanges = {
      language: this.language(),
      delegates: this.delegates(),
      jiraBaseUrl: this.jiraBaseUrl(),
      jiraProjectKey: this.jiraProjectKey(),
      jiraWaitingStatuses: this.jiraWaitingStatuses(),
      jiraIncludeWaiting: this.jiraIncludeWaiting(),
      jiraDutyStatuses: this.jiraDutyStatuses(),
      jiraOnDuty: this.jiraOnDuty(),
      jiraDoneStatuses: this.jiraDoneStatuses(),
      adoBaseUrl: this.adoBaseUrl(),
      adoProject: this.adoProject(),
      adoWaitingStates: this.adoWaitingStates(),
      adoDoneStates: this.adoDoneStates(),
      adoIncludeWaiting: this.adoIncludeWaiting(),
      adoWorkItemTypes: this.adoWorkItemTypes(),
      adoDefaultDeadlineDays: this.adoDefaultDeadlineDays(),
      ...changes,
    };

    const delegates = [...(next.delegates ?? [])];
    const waiting = [...(next.jiraWaitingStatuses ?? [])];
    const duty = [...(next.jiraDutyStatuses ?? [])];
    const states = [...(next.adoWaitingStates ?? [])];
    const doneStatuses = [...(next.jiraDoneStatuses ?? [])];
    const doneStates = [...(next.adoDoneStates ?? [])];
    const types = [...(next.adoWorkItemTypes ?? [])];
    const request = new SettingsRequest({
      language: blank(next.language),
      delegates: delegates.length === 0 ? undefined : delegates,
      jiraBaseUrl: blank(next.jiraBaseUrl),
      jiraProjectKey: blank(next.jiraProjectKey),
      jiraWaitingStatuses: waiting.length === 0 ? undefined : waiting,
      jiraIncludeWaiting: next.jiraIncludeWaiting === true ? true : undefined,
      jiraDutyStatuses: duty.length === 0 ? undefined : duty,
      jiraOnDuty: next.jiraOnDuty === true ? true : undefined,
      // Empty is how the wire spells cleared, the same as the two lists above and unlike
      // adoWorkItemTypes, where absent restores a default.
      jiraDoneStatuses: doneStatuses.length === 0 ? undefined : doneStatuses,
      adoBaseUrl: blank(next.adoBaseUrl),
      adoProject: blank(next.adoProject),
      adoWaitingStates: states.length === 0 ? undefined : states,
      adoDoneStates: doneStates.length === 0 ? undefined : doneStates,
      adoIncludeWaiting: next.adoIncludeWaiting === true ? true : undefined,
      // The one list where an empty one is not how the wire spells cleared: absent restores the
      // three default types, and a present empty list is refused with ado.workItemTypesRequired. So
      // emptiness is only sent when the caller asked for it - taking the last type off the list is
      // answered, while a save that never mentioned the types (before the first read, when the
      // signal is still empty) cannot be refused by them.
      adoWorkItemTypes: types.length === 0 && !('adoWorkItemTypes' in changes) ? undefined : types,
      // Never through a truthiness check: 0 is a value here, and the falsy one. Omitted at the
      // default, because absent is how the wire spells "the default" on this field.
      adoDefaultDeadlineDays:
        next.adoDefaultDeadlineDays === defaultDeadlineDays
          ? undefined
          : next.adoDefaultDeadlineDays,
    });

    into.set(null);
    try {
      this.read(await firstValueFrom(this.client.updateSettings(request)));
      await this.apply();
    } catch (error) {
      into.set(apiErrorMessage(this.transloco, error));
    }
  }

  /**
   * Answers whether the token was stored, because the caller has to clear its own field and the
   * store cannot do it: the value never comes in here.
   */
  async setToken(token: string): Promise<boolean> {
    this.jiraError.set(null);
    try {
      this.read(await firstValueFrom(this.client.setJiraToken(new JiraTokenRequest({ token }))));
      return true;
    } catch (error) {
      this.jiraError.set(apiErrorMessage(this.transloco, error));
      return false;
    }
  }

  async clearToken(): Promise<void> {
    this.jiraError.set(null);
    try {
      this.read(await firstValueFrom(this.client.clearJiraToken()));
    } catch (error) {
      this.jiraError.set(apiErrorMessage(this.transloco, error));
    }
  }

  /**
   * The Azure DevOps token, on its own route for the same reason Jira's is: PUT /api/settings is a
   * full replacement that reads an absent field as "clear", so a token on it would be wiped by every
   * other change. Written out again rather than shared with the Jira pair - the two call different
   * generated methods, and a shared one would take the route as a parameter.
   */
  async setAdoToken(token: string): Promise<boolean> {
    this.adoError.set(null);
    try {
      this.read(await firstValueFrom(this.client.setAdoToken(new AdoTokenRequest({ token }))));
      return true;
    } catch (error) {
      this.adoError.set(apiErrorMessage(this.transloco, error));
      return false;
    }
  }

  async clearAdoToken(): Promise<void> {
    this.adoError.set(null);
    try {
      this.read(await firstValueFrom(this.client.clearAdoToken()));
    } catch (error) {
      this.adoError.set(apiErrorMessage(this.transloco, error));
    }
  }

  /** All six routes answer with the whole settings shape, so all six are read the same way. */
  private read(response: SettingsResponse): void {
    this.language.set(response.language ?? null);
    this.delegates.set(response.delegates);
    this.jiraBaseUrl.set(response.jiraBaseUrl ?? null);
    this.jiraProjectKey.set(response.jiraProjectKey ?? null);
    this.jiraWaitingStatuses.set(response.jiraWaitingStatuses);
    this.jiraIncludeWaiting.set(response.jiraIncludeWaiting);
    this.jiraDutyStatuses.set(response.jiraDutyStatuses);
    this.jiraDoneStatuses.set(response.jiraDoneStatuses);
    this.jiraOnDuty.set(response.jiraOnDuty);
    this.hasJiraToken.set(response.hasJiraToken);
    this.adoBaseUrl.set(response.adoBaseUrl ?? null);
    this.adoProject.set(response.adoProject ?? null);
    this.adoWaitingStates.set(response.adoWaitingStates);
    this.adoDoneStates.set(response.adoDoneStates);
    this.adoIncludeWaiting.set(response.adoIncludeWaiting);
    this.adoWorkItemTypes.set(response.adoWorkItemTypes);
    this.adoDefaultDeadlineDays.set(response.adoDefaultDeadlineDays);
    this.hasAdoToken.set(response.hasAdoToken);
  }

  private async apply(): Promise<void> {
    const language = resolveLanguage(this.language(), this.system);

    this.transloco.setActiveLang(language);
    // A screen reader picks its voice from the document, not from Transloco.
    document.documentElement.lang = language;

    await firstValueFrom(this.transloco.load(language));
  }
}

function blank(value: string | null | undefined): string | undefined {
  return value === null || value === undefined || value.trim() === '' ? undefined : value;
}
