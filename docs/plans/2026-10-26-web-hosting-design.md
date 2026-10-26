# Hosting the app on the NAS - design

Decided 2026-10-26, approved by the user section by section. The wish: *"Hvis denne app skal kunne
virke som web hvad skal vi så gøre? Den vil blive hostet på min nas synology 224+, så ai funktioner
vil kun virke på pc som client"*, followed by *"så snart der bliver pushed så auto deployes der"*,
*"api keys skal ikke ligge på min nas"* and *"Som slutbruger skal man se en samlet applikation"*.

## 1. The constraints that shaped it

- **The NAS cannot run the model.** A DS224+ is a Celeron J4125 with 2 GB of RAM; `gemma4:e4b` does
  not fit.
- **Only the PC reaches Jira and ADO.** Both sit behind the corporate VPN (the user's answer). So AI
  and the imports share one problem: something only the PC can reach.
- **The browser cannot call Jira or ADO itself.** Neither sends CORS headers for a foreign origin.
- **The NAS cannot call the PC.** A managed laptop on a VPN does not accept inbound connections, and a
  forward proxy on the PC would hand everyone on the LAN a way into the corporate network.
- **Jira and ADO tokens never live on the NAS**, not even in transit while being typed.
- **One application for the user.** The helper has no window and no page of its own.

Every connection therefore starts from the PC: out to Jira, ADO and Ollama, and home to the NAS.

## 2. Architecture: the helper is a tunnel for outbound calls

- **All logic, the database and the tests stay on the NAS.** `/api/jira/preview` alternates between
  Jira and the database five times (fetch, imported keys, role from settings, status change per row,
  reconciliation), and ADO likewise. Splitting the endpoints between two processes would make every
  preview a conversation and put the logic in two places. Rejected for that reason.
- **The helper opens a WebSocket out to the NAS** and keeps it open, reconnecting with backoff.
- **On the NAS, the three typed clients** - `JiraTaskSource`, `AdoTaskSource`, `OllamaClient` - get
  an `HttpMessageHandler` that serialises the request (method, URL, headers, body) over the tunnel
  with a correlation id and awaits the answer. Their timeouts are unchanged.
- **The helper performs the call and sets `Authorization` itself.** The NAS sends every request
  without credentials.
- **The helper forwards only to an allowlist built from its own settings:** the Jira base URL, the ADO
  collection URL and `http://127.0.0.1:11434`. Anything else is refused. The NAS cannot set these
  URLs; if it could, it could move the allowlist.
- **No helper connected** gives the error code `helper.offline` with a text in both language files.
  AI uses the state that already exists for "the model does not answer".
- **The handshake carries a protocol version.** A mismatch is refused with a code the app shows as
  "Hjælperen skal opdateres".
- A side effect worth having: when the PC is on, AI and the imports also work from the phone.

## 3. Who owns what

- **The helper owns the connection:** Jira base URL and token, ADO collection URL and token. The
  tokens are stored with Windows DPAPI.
- **The NAS owns what is fetched:** project key, status lists, duty, work item types, ADO project, day
  count and the rest of today's settings page.
- **On connect the helper tells the NAS its base URLs** (never the tokens), so "Åbn sagen" links can
  still be built. Today's `jira.baseUrl` and `ado.baseUrl` settings are replaced by these.
- `PUT`/`DELETE /api/settings/jira-token` and `ado-token` leave the contract. A migration deletes the
  two stored token rows.

## 4. The helper's UI is a microfrontend in an iframe

- **The helper contributes one group in the settings accordion, "Forbindelser":** Jira URL and token,
  ADO URL and token, Ollama's status, the tunnel's status.
- **It is embedded as an iframe** from `http://127.0.0.1:<fixed port>/connections`. The iframe is
  another origin, so the NAS's JavaScript cannot read what is typed there, and the token goes from
  the field straight to the helper. Native Federation was rejected: the helper's code would run under
  the NAS's origin, where the NAS's own code can read the token.
- **The helper's UI ships inside the helper's exe.** It must never be fetched from the NAS - that
  would put NAS code on the token's origin again.
- **The two talk through `postMessage`, with the origin checked both ways:** ready, height, language,
  status changed. Dark mode follows by itself through `prefers-color-scheme`. Built from the same
  Tailwind setup and language files in this repository, so it looks the same.
- **No "ready" within a few seconds** (the phone, or the helper not running): the app shows
  "Forbindelserne indstilles fra den PC hvor hjælperen kører."
- The Alt shortcuts do not work while focus is inside the iframe.

## 5. Login

- **One user, on the NAS:** a long-lived cookie and a password hash in an environment variable of the
  container. No user table, no registration.
- **The helper authenticates with its own key**, generated on the NAS and pasted into the helper once.
  The NAS stores only its hash. The tunnel refuses everything without it; otherwise anyone on the LAN
  could pose as the helper and receive the calls.
- `/api/health` stays open, for the deploy probe and the helper's version check.
- **Reachable only on the LAN or through Tailscale.** No port opened in the router.

## 6. Projects, container and deploy

- **The web version is 2.0.0.** The desktop app it replaces is tagged `v1.16.0`, the number
  `/api/health` reports for it, with a GitHub Release (the user's choice 2026-10-26).
- **`Todo.Host` becomes a plain web server** for linux-x64: no Photino, no `ShellLinkLauncher`, no
  `WinExe`. It binds `http://0.0.0.0:8080` in the container.
- **`Todo.Helper` is a new Windows project:** the tunnel client, the "Forbindelser" microfrontend, the
  DPAPI store, the allowlist. It runs in the background without a window.
- **A multi-stage `Dockerfile`:** Node builds Angular, `dotnet publish` builds the server, the result
  sits on `mcr.microsoft.com/dotnet/aspnet:10.0` as a non-root user.
- **`docker-compose.yml` in the repository**, imported as a project in Container Manager:
  `/volume1/docker/todo` mounted as `/data`, `TZ=Europe/Copenhagen`, `Data__Path=/data/todo.db`, the
  password hash and the helper key's hash.
- **`TZ` is load-bearing.** `SystemClock.Today` is `DateTime.Now`, and a container runs in UTC by
  default: between 00 and 02 "today" would be yesterday, and overdue and deferred would be wrong.
- **On push to `main`:** the existing Windows `test` job runs unchanged. A new `image` job
  (`needs: test`) on ubuntu builds the image and **tries it before pushing**: the container is started
  against a temporary database, and `/api/health`, `/` and `/scalar/` must answer 200 - the grip
  `Publish.cmd` uses. Then it pushes `ghcr.io/thomashjorth/todo:latest` and `:<sha>`.
- **The NAS pulls by itself.** The repository is public, so the image is too, and the NAS needs no
  GitHub credentials. Watchtower in the same compose project checks every five minutes and restarts
  only the todo container. If Watchtower turns out to be unmaintained, a DSM scheduled task running
  `docker compose pull && docker compose up -d` does the same. Rollback is pinning `:<sha>`.
- The app already backs up before every migration. The volume goes into Hyper Backup.
- **The helper is built on push into a GitHub Release** and does not update itself (see "Out of
  scope"). The protocol version in section 2 is what makes a stale helper visible.

## 7. What the web version changes in the app

- **Photino goes.** The window is the browser's "Install as app" in Edge or Chrome: a window of its
  own, no address bar, an icon in the taskbar. Full installation needs HTTPS, which Tailscale can
  issue for the NAS; without it "Create shortcut → Open as window" still works.
- **Links open in the browser.** `/api/system/open-link`, `ShellLinkLauncher` and
  `ErrorCodes.SystemUnsupportedScheme` leave. The seven call sites stay buttons and call
  `window.open(url, '_blank', 'noopener')` through `SystemStore`, which keeps the scheme check (http,
  https, mailto) in one place. A button rather than an anchor, because the app window has no address
  bar and no back button either, so a navigation inside it is still one-way. The per-row open errors
  on the Jira and ADO previews go: their URLs are built from base URLs that only accept http and
  https, so the branch could no longer be reached. The note's error line stays.
- **The Alt shortcuts stay.** What the browser's app window takes for itself is measured there, as
  `Alt+1`-`9` was measured in Photino.
- **The data moves once:** checkpoint, then copy `todo.db` to the volume.

## 8. Tests

- **Api:** the tunnel handler against a fake helper - offline, version mismatch, a call carried there
  and back - and that the NAS never sends an `Authorization` header out.
- **A new `Todo.Helper.Tests`:** the allowlist refuses a URL outside it, the header is set per target,
  the tokens are stored with DPAPI.
- **E2E:** the iframe handshake and the fallback text. Most important: **a guard that the typed token
  never appears in any request to the NAS host**, in the manner of `ApiDocsJourneyTests`. It is seen
  to fail by routing the token field through the host.
- **`RunningHost` logs in with a test password**, so every E2E runs the login path rather than having
  it switched off.
- **The existing Jira and ADO tests keep their fakes.** In-process, the typed clients use the direct
  handler; the tunnel handler has its own tests.
- **Contract:** the token routes and `open-link` leave; the settings response gains the helper's
  status and base URLs.

## 9. To be measured, not assumed

- Chrome's Local Network Access when a page from the NAS embeds `127.0.0.1`: whether it asks, and
  whether the iframe needs `allow="local-network-access"`.
- Which Alt combinations the browser's app window takes.
- The owner of `/volume1/docker/todo` against the container's non-root user.
- Watchtower's maintenance status.

## Out of scope

- The helper updating itself from GitHub Releases.
- Access from outside the LAN and Tailscale.
- More than one user.
