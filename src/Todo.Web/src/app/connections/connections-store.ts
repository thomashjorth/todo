import { HttpClient, HttpHeaders } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
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

  readonly status = signal<ConnectionsStatus | null>(null);

  /** The hash of a key just made. Shown once, because the key behind it changes with every pairing. */
  readonly keyHash = signal<string | null>(null);

  /** Whether the helper failed to answer the last call. */
  readonly failed = signal(false);

  /**
   * Reads the status again. Tells the app when the tunnel moved, so its settings - whether the helper
   * is online, and the base URLs the hello stored - follow without the user reloading anything.
   */
  async load(): Promise<void> {
    const before = this.status()?.tunnel;

    if (
      await this.call(
        this.http.get<ConnectionsStatus>('/api/connections', { headers: HELPER_HEADERS }),
      )
    ) {
      if (before !== undefined && before !== this.status()?.tunnel) {
        this.parent.post({ type: 'changed' });
      }
    }
  }

  async saveServer(url: string): Promise<void> {
    await this.save(
      this.http.put<ConnectionsStatus>('/api/server', { url }, { headers: HELPER_HEADERS }),
    );
  }

  async pair(): Promise<void> {
    this.failed.set(false);

    try {
      const response = await firstValueFrom(
        this.http.post<{ keyHash: string }>('/api/server/pair', {}, { headers: HELPER_HEADERS }),
      );
      this.keyHash.set(response.keyHash);
    } catch {
      this.failed.set(true);
      return;
    }

    await this.load();
  }

  /** The base URL as given; the token only when one was typed, because the field is write-only. */
  async saveSource(source: SourceName, baseUrl: string, token: string): Promise<void> {
    await this.save(
      this.http.put<ConnectionsStatus>(
        `/api/connections/${source}`,
        { baseUrl, token: token.trim() === '' ? null : token },
        { headers: HELPER_HEADERS },
      ),
    );
  }

  async clearToken(source: SourceName): Promise<void> {
    await this.save(
      this.http.put<ConnectionsStatus>(
        `/api/connections/${source}`,
        { baseUrl: this.status()?.[source].baseUrl ?? null, clearToken: true },
        { headers: HELPER_HEADERS },
      ),
    );
  }

  private async save(request: Observable<ConnectionsStatus>): Promise<void> {
    if (await this.call(request)) {
      this.parent.post({ type: 'changed' });
    }
  }

  private async call(request: Observable<ConnectionsStatus>): Promise<boolean> {
    this.failed.set(false);

    try {
      this.status.set(await firstValueFrom(request));
      return true;
    } catch {
      this.failed.set(true);
      return false;
    }
  }
}
