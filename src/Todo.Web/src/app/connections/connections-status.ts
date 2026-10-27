/** Where the helper's tunnel to the server stands, spelled as the helper's TunnelState enum. */
export type TunnelState = 'NotConfigured' | 'Connecting' | 'Connected' | 'Refused' | 'Disconnected';

/** One source as the helper reports it: never the token, only whether it holds one. */
export interface SourceStatus {
  baseUrl: string | null;
  hasToken: boolean;
}

/**
 * GET /api/connections on the helper, written by hand: the helper's local API is not in the app's
 * contract, because it is not the server's. LocalApiTests pins the names on the raw body.
 */
export interface ConnectionsStatus {
  serverUrl: string | null;
  hasKey: boolean;
  tunnel: TunnelState;
  refusal: string | null;
  jira: SourceStatus;
  ado: SourceStatus;
  ollamaUrl: string;
}
