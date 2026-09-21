# 23. Keyboard shortcuts activate, in two layers, from a registry

Date: 2026-08-17
Status: Accepted. The second layer and the row digits were added 2026-08-24

## Context

The app stands open beside the work all day, and reaching for the mouse to tick something off
breaks whatever you were doing. Working without the mouse is the point, not a fallback.

Two other things pull the same way. The design already requires that every action be reachable
from the keyboard, so a shortcut layer is how that commitment becomes concrete rather than
aspirational. And a web app in a native window that ignores the platform's conventions feels
wrong — a Windows app shows its `Alt` letters on its buttons.

## Decision

**Holding `Alt` reveals the labels**, and a shortcut **performs the element's activation
action** rather than only moving focus. That is the Windows convention and HTML's own
`accesskey` behaviour: a text field takes focus because it has nothing else to do, a checkbox
toggles, a button is clicked, a link is followed. Focus alone would mean pressing `Alt+O` and
*then* `Enter`.

A programmatic `click()` does not move focus, so an activating shortcut calls `focus()` too —
Windows moves the focus ring as much as it acts. The directive takes
`appShortcutAction="focus" | "activate"`.

**Two layers**, because the letters ran out. Navigation claims `O G I J A S N K V M` and the
panel's fields claim `I D S O N T V U L` — **five letters appear in both**, and each is the
mnemonic one on both sides. One layer would have meant giving five of them an arbitrary
letter. The registry key is layer plus key, built by
`shortcutKey`, so `Alt+O` and `Alt+Shift+O` are two entries; `shortcutLabel` derives
`aria-keyshortcuts` from the **same two fields**, so what a screen reader announces cannot
drift from what works.

- `alt` — navigation and the list's controls, plus `Alt+1`–`9` selecting the nth selectable row
- `alt-shift` — the detail panel's nine fields, and only on the task list

The registry is **last-writer-wins**, so letters and digits must stay globally unique.

Registration happens in an `effect` with `onCleanup`, not in `ngOnInit`/`ngOnDestroy`. Rows
track by id, so a search or a status change renumbers 1–9 **without** destroying any row;
with lifecycle hooks the same component instance would keep answering its old digit forever,
silently, because the badge reads its input and shows the new number.

## Alternatives rejected

**Focus-only shortcuts.** Two keystrokes for every action, and unlike anything else on
Windows.

**One layer with more modifiers or chords.** `Ctrl+Alt` is AltGr on a Danish keyboard, so a
global `Alt` listener must check `!ctrlKey && !metaKey` or the user cannot type `@`, `£` or
`$`.

**Letters chosen for mnemonics alone.** `Alt+D`, `Alt+E`, `Alt+F`, `Alt+Home` and the arrow
keys are taken by Chrome. They are free in the Photino window, but a shortcut that works in
the app and not in the browser gets debugged from the wrong end.

## Consequences

- Shortcut letters are a **global namespace** with no compiler and no type to hold it. A
  collision is invisible to unit tests, because registration is a plain map write and the
  visible label is written in the template rather than derived. A guard reads
  `aria-keyshortcuts` off the rendered screen and requires the letters to be distinct.
- `unregister` takes the callback with it and deletes only if the stored one is still the
  same, because two rows swapping numbers each run their own cleanup and the order between
  them is not guaranteed.
- `preventDefault()` is called only when the key was actually handled, so unhandled
  combinations still reach the browser and the operating system.
- The labels are shown on **`Alt` alone**, including the field layer's. Requiring `Alt+Shift`
  just to *see* them would expose the user to Windows' keyboard-layout switch on every
  release — a fault that surfaces weeks later as "the app changes my keyboard".
- One shortcut deliberately only focuses: deletion has neither confirmation nor undo, so the
  second keypress **is** the confirmation.
