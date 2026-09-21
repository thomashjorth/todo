# 19. Publishing is two files, and the app does not start with Windows

Date: 2026-08-20
Status: Accepted. Autostart was built in slice 16 and removed entirely on 2026-08-25

## Context

The app is installed by copying it somewhere, not by an installer. That means what is
published has to be small enough to move by hand and complete enough to run on a machine with
no .NET installed.

## Decision

`Publish.cmd` publishes self-contained into `publish\`, which git ignores. The result is
**two files**: the executable, with the Angular build embedded in it, and `icon.ico` — which
cannot be embedded, because Photino takes its icon as a path. See ADR 1.

The script then **runs what it produced**: headless on a free port against a temporary
database, requiring 200 from the frontend, the health endpoint and the documentation page. It
refuses to overwrite an executable that is running, and names the process rather than killing
it — the user usually has the real app open.

**The app does not start with Windows.** Autostart existed for five days: a setting read from
the registry rather than from the database, with its own contract field, endpoints, error
codes and twelve tests. It was removed because it was not used. The app is opened when it is
wanted.

## Alternatives rejected

**An installer.** Machinery for one machine.

**Framework-dependent deployment.** Smaller output, and a runtime version to keep in step on
a machine where nothing else needs .NET.

**Keeping autostart.** Six layers of code and twelve tests for a checkbox nobody ticked. It
would also have made sense only alongside a tray icon and background sync, neither of which
exists — without them it just opens a window that was not asked for.

## Consequences

- The publish verifies the artefact rather than the sources, so packaging faults — content
  root, icon path, missing files — fail at publish rather than on first run.
- The frontend is inside the executable, so a published app cannot be patched by dropping a
  file next to it.
- There is no registry footprint at all. Everything the app knows is in `%APPDATA%`.
- Removing autostart also removed the worked example for the `[checked]` rebinding trap, which
  is now described in ADR 9 without a place in the code to point at.
