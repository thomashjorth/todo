import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';

import { ConnectionsStatus } from './connections-status';
import { ParentChannel } from './parent-channel';

/**
 * The header the helper's local API demands. A browser will not send it across origins without a
 * preflight the helper never answers, so only a page on the helper's own origin - this one - gets
 * through (LocalApi.RequiredHeader).
 */
const HELPER_HEADERS = new HttpHeaders({ 'X-Todo-Helper': '1' });

export type SourceName = 'jira' | 'ado';

/**
 * The Connections page's state, owning every call to the helper's local API. Relative URLs, because
 * the page is served by the helper. A token goes in and never comes back: the status says only
 * whether one is held, and no signal here ever holds one.
 */
@Injectable({ providedIn: 'root' })
export class ConnectionsStore {
  private readonly http = inject(HttpClient);
  private readonly parent = inject(ParentChannel);

  /**
   * Counts the reads and saves in flight. A poll sent before a save can answer after it, and its older
   * status would put the old address back into the field - so only the newest answer may write.
   */
  private sequence = 0;

  readonly status = signal<ConnectionsStatus | null>(null);

  /** The hash of a key just made. Shown once, because the key behind it changes with every pairing. */
  readonly keyHash = signal<string | null>(null);

  /** A pairing in flight, so a second click cannot make a key the first answer's hash no longer fits. */
  readonly pairing = signal(false);

  /**
   * Two reasons for the one error line, kept apart because the poll answers every two seconds: a save
   * the helper refused stays said until the next save, and the poll only says whether the helper is
   * there at all.
   */
  private readonly saveFailed = signal(false);
  private readonly unreachable = signal(false);

  readonly failed = computed(() => this.saveFailed() || this.unreachable());

  /**
   * Reads the status again. Tells the app when the tunnel moved, so its settings - whether the helper
   * is online, and the base URLs the hello stored - follow without the user reloading anything.
   */
  async load(): Promise<void> {
    const before = this.status()?.tunnel;
    const answer = await this.call(
      this.http.get<ConnectionsStatus>('/api/connections', { headers: HELPER_HEADERS }),
    );

    this.unreachable.set(answer === 'failed');

    if (answer === 'written' && before !== undefined && before !== this.status()?.tunnel) {
      this.parent.post({ type: 'changed' });
    }
  }

  async saveServer(url: string): Promise<boolean> {
    return this.save(
      this.http.put<ConnectionsStatus>('/api/server', { url }, { headers: HELPER_HEADERS }),
    );
  }

  async pair(): Promise<void> {
    if (this.pairing()) return;

    this.pairing.set(true);
    this.saveFailed.set(false);

    try {
      const response = await firstValueFrom(
        this.http.post<{ keyHash: string }>('/api/server/pair', {}, { headers: HELPER_HEADERS }),
      );
      this.keyHash.set(response.keyHash);
    } catch {
      this.saveFailed.set(true);
      return;
    } finally {
      this.pairing.set(false);
    }

    await this.load();
  }

  /**
   * The base URL as given; the token only when one was typed, because the field is write-only. Says
   * whether the helper took it, so the page empties the token field only then - a refused save would
   * otherwise lose what the user typed.
   */
  async saveSource(source: SourceName, baseUrl: string, token: string): Promise<boolean> {
    return this.save(
      this.http.put<ConnectionsStatus>(
        `/api/connections/${source}`,
        { baseUrl, token: token.trim() === '' ? null : token },
        { headers: HELPER_HEADERS },
      ),
    );
  }

  async clearToken(source: SourceName): Promise<boolean> {
    return this.save(
      this.http.put<ConnectionsStatus>(
        `/api/connections/${source}`,
        { baseUrl: this.status()?.[source].baseUrl ?? null, clearToken: true },
        { headers: HELPER_HEADERS },
      ),
    );
  }

  private async save(request: Observable<ConnectionsStatus>): Promise<boolean> {
    this.saveFailed.set(false);

    const answer = await this.call(request);
    if (answer === 'failed') {
      this.saveFailed.set(true);
      return false;
    }

    this.parent.post({ type: 'changed' });
    return true;
  }

  /** 'written' when this answer is the newest and became the status, 'stale' when a later one won. */
  private async call(
    request: Observable<ConnectionsStatus>,
  ): Promise<'written' | 'stale' | 'failed'> {
    const mine = ++this.sequence;

    try {
      const status = await firstValueFrom(request);
      if (mine !== this.sequence) return 'stale';

      this.status.set(status);
      return 'written';
    } catch {
      return 'failed';
    }
  }
}
