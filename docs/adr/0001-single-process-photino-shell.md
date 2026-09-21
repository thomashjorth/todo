# 1. One process: a Photino window over ASP.NET Core

Date: 2026-08-13
Status: Accepted

## Context

The app is a personal todo list for one person on one Windows machine. It has to show a
window, serve a UI, reach Jira and Azure DevOps over HTTP, and keep a local database.
Angular was a given — the UI was going to be web — which took WPF, WinForms and every other
native toolkit out of the running before a shell was ever considered.

The author also wanted to learn the stack. A personal app is where that is cheap.

## Decision

One process. `Todo.Host` is an ASP.NET Core application whose Kestrel listens on loopback
only, on a random port. `Photino.NET` opens a native window against that port, and Angular
is served from `wwwroot` as static files. The project is a `WinExe` rather than an `Exe`, so
Windows does not open a console beside the window.

Photino is an independent open-source project, not a Microsoft one. The Microsoft piece is
**WebView2** underneath it, which ships with Windows 11 — that is what keeps the executable
small and quick to start.

## Alternatives rejected

**Electron.** Two ecosystems and two build chains in an app maintained by one person, plus a
bundled Chromium in every release. C# all the way down was the point.

**MAUI Blazor Hybrid, WPF, WinForms.** Angular was the intended UI, so a native toolkit was
never a candidate.

**A separate server process.** Something else to start, stop, find a port for and keep
alive, for a UI that only ever talks to itself.

## Consequences

- WebView2 is a hard runtime requirement. It is present on Windows 11 by default, so in
  practice there is nothing to install — but the app cannot run without it.
- The window is the process: there is no app left running once the host stops, and no host
  to reach once the window closes.
- Nothing is reachable from the network, and no fixed port can collide with another tool.
  The port is not knowable in advance, so whoever needs it reads it from the started host.
- Photino takes its icon as a path rather than as an embedded resource, which is why a
  publish is two files rather than one. See ADR 19.
