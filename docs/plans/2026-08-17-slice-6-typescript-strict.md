# Skive 6 — TypeScript strict mode

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** En tastefejl i en template-binding skal bryde bygningen — også inde i rækketemplaten, hvor den i dag ikke gør.

**Architecture:** To dele, og rækkefølgen er ikke til forhandling. Først slås `strict` og `strictTemplates` til; det er gratis i dag og lægger gulvet. Derefter udtrækkes `#taskRow` til en rigtig børnekomponent med et typet `input()`, for **det** er det, der faktisk fanger tastefejlen. Flagene alene gør det ikke, og målingen nedenfor viser hvorfor.

**Tech Stack:** TypeScript 5.9 · Angular 22 signals (`input()`, `output()`, `@let`) · Transloco · Tailwind 4.3.3 · Vitest · Playwright 1.62.0

## Hvorfor — og en rettelse af premisset

`docs/HANDOFF.md` og designdokumentets afsnit 9 siger begge, at en tastefejl (`task.titel`) gav et grønt build, **fordi** frontenden kører uden `strict`. Det er målt efter, og det holder ikke. Konklusionen — at der skal gøres noget — holder; begrundelsen skal skiftes ud, ellers slås to flag til og problemet står tilbage uændret.

Målt 2026-08-17 med `ngc --noEmit` mod repoets egen `tsconfig.app.json`:

| Måling | Resultat |
| --- | --- |
| `strict: true` på app-kilderne (20 filer, inkl. den 1992-linjers genererede klient) | **0 fejl** |
| `strict: true` på spec-kilderne (13 filer) | **0 fejl** |
| `strictTemplates: true` på alle fire templates | **0 fejl** |
| `task.titel` i den **typede** række (`task-list.html:98`), flag **slået fra** | **fanget** — `error TS2551: Property 'titel' does not exist on type 'TodoTask'. Did you mean 'title'?` |
| `task.titel` inde i `#taskRow` (`task-list.html:140`), flag **slået til** | **ikke fanget** |

Læs de to sidste rækker sammen. Det typede sted var aldrig ubeskyttet — Angular tjekker det allerede uden `strict`. Og det utypede sted bliver **ikke** beskyttet af `strictTemplates`. `<ng-template #taskRow let-task>` giver `task` typen `any`, og `[ngTemplateOutletContext]` afstemmes ikke mod templatens kontekst. Linje 126–329 — cirka to tredjedele af hele UI'et, og alt det interessante: detaljepanelet, statusvælgeren, underopgaverne, noteeditoren — ligger i det hul.

Så: flagene er gratis at slå til og værd at have som gulv, men de er ikke leverancen. **Børnekomponenten er leverancen.**

## Beslutninger

| Emne | Valg |
| --- | --- |
| Flag | `strict: true` i `compilerOptions`, `strictTemplates: true` i `angularCompilerOptions`. Begge i `tsconfig.json`, så app og spec arver dem. |
| Rækkefølge | Flag først, i egen commit. Så er den senere diff udelukkende udtrækket. |
| Vælger på `TaskRow` | Attributvælger `li[appTaskRow]`, **ikke** `app-task-row`. |
| Rækkens tilstand | `expandedId` og `editingNote` bliver i forælderen. Børnekomponenten får dem som `boolean`-inputs og melder tilbage med outputs. |
| Kald til storen | Børnekomponenten injicerer `TaskStore` selv og gemmer sine egne felter. Kun "hvilken række er åben" går gennem forælderen. |
| Fokus-effekten | Flytter med til barnet, hvor `viewChild('note')` hører hjemme: hver række har sin egen textarea. |
| Den færdige rækkes `<li>` | Uændret og inline i `task-list.html`. Den er en anden, simplere række, og den er allerede typet. |

**Attributvælgeren er ikke en detalje.** Et `<app-task-row>`-element mellem `<ul>` og `<li>` ville lægge et fremmed element ind i listen, så `divide-y divide-gray-100` ikke længere rammer søskende-`<li>`'er, og strukturen bliver ugyldig HTML. Med `selector: 'li[appTaskRow]'` **er** værtselementet rækkens `<li>`, og DOM'en er bit for bit den samme som i dag. `data-testid="task-row"` flytter til en host-binding, så `TaskListScreen.Rows` bliver ved at finde rækken.

## Fælden, der venter i udtrækket

Når konteksten først er typet, holder `@if` **ikke** et signal-kald indsnævret. Målt med en kasseret probe-komponent:

```html
@if (item().waitingDays != null) {
  <span>{{ key(item().waitingDays) }}</span>   <!-- error TS2345 -->
}
```

> `error TS2345: Argument of type 'number | undefined' is not assignable to parameter of type 'number'.`

`item()` er et funktionskald, og TypeScript kan ikke bære en indsnævring hen over to kald. Rettelsen er `@let`, som giver en stabil lokal:

```html
@let waitingDays = task().waitingDays;
@if (waitingDays != null) {
  <span data-testid="waiting-days">…</span>
}
```

**Brug `@let` + `!= null` — ikke `@if (…; as …)`.** `as` binder på *sandhed*, og `waitingDays` kan være `0`: en ventetid på under et døgn ville forsvinde fra skærmen. `task-list.spec.ts:521` fastslår `'0 dage'` og er vagten mod netop det, så fejlen bliver fanget — men kun hvis testen køres.

## Bevidst uden for skive 6

Kontrasten, det synlige fokus, `dark:`-modparterne og tastaturgenvejene. Det er skive 7's arbejde, og det er en anden slags gennemgang. Udtrækket her flytter markup uændret, så farverne bliver præcis lige så mangelfulde som før — det er med vilje, så diffen kan læses.

Også udenfor: `noUnusedLocals`, `noUncheckedIndexedAccess` og `strictStandalone`. De er ikke det, `ng new` sætter, og de hører til en selvstændig beslutning.

---

## Task 1: Slå flagene til

**Files:**
- Modify: `src/Todo.Web/tsconfig.json`

**Step 1: Flagene**

`compilerOptions` får `"strict": true` som første linje, og `angularCompilerOptions` får `"strictTemplates": true`:

```json
  "compilerOptions": {
    "strict": true,
    "noImplicitOverride": true,
    "noPropertyAccessFromIndexSignature": true,
    "noImplicitReturns": true,
    "noFallthroughCasesInSwitch": true,
    "skipLibCheck": true,
    "isolatedModules": true,
    "resolveJsonModule": true,
    "experimentalDecorators": true,
    "importHelpers": true,
    "target": "ES2022",
    "module": "preserve"
  },
  "angularCompilerOptions": {
    "enableI18nLegacyMessageIdFormat": false,
    "strictInjectionParameters": true,
    "strictInputAccessModifiers": true,
    "strictTemplates": true
  },
```

**Step 2: Byg og se at der ikke sker noget**

Fra repo-roden:

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
```

Forventet: bygningen lykkes, ingen fejl. **Det er ikke et tegn på at flagene ikke virker** — det er målingen fra afsnittet ovenfor, der gentager sig. Ser du fejl her, er der sket noget i mellemtiden, som ikke stod i planen; rapportér dem frem for at rette dem i tavshed.

**Step 3: Kør Vitest**

```
npm.cmd run test --prefix src\Todo.Web -- --watch=false
```

Forventet: **133 beståede**, ingen fejl. Et andet tal betyder, at en test er tabt eller duplikeret.

**Step 4: Commit**

```
git add src/Todo.Web/tsconfig.json
git commit -m "🔒 Slå TypeScript strict mode og strictTemplates til"
```

---

## Task 2: Se hullet med dine egne øjne

Ingen commit i denne opgave. Formålet er, at den næste ændring bliver lavet af en, der har set hvorfor.

**Step 1: Bryd bindingen inde i `#taskRow`**

I `src/Todo.Web/src/app/tasks/task-list.html` linje 140, `task.title` → `task.titel`:

```html
          <span class="block text-sm break-words text-gray-900">{{ task.titel }}</span>
```

**Step 2: Byg**

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
```

Forventet: **bygningen lykkes.** Med `strict` og `strictTemplates` slået til. Det er hullet.

**Step 3: Bryd også den typede binding**

Linje 98 i samme fil, inde i den færdige sektion, `task.title` → `task.titel`.

**Step 4: Byg igen**

Forventet: **fejl** — og kun én, om linje 98:

```
src/app/tasks/task-list.html:98:20 - error TS2551: Property 'titel' does not exist on type 'TodoTask'. Did you mean 'title'?
```

Der står intet om linje 140. To identiske tastefejl, én fanget. Rapportér begge outputs.

**Step 5: Rul tilbage**

```
git checkout -- src/Todo.Web/src/app/tasks/task-list.html
git status --porcelain
```

Forventet: intet output fra `git status`.

---

## Task 3: Udtræk `TaskRow`

**Files:**
- Create: `src/Todo.Web/src/app/tasks/task-row.ts`
- Create: `src/Todo.Web/src/app/tasks/task-row.html`
- Modify: `src/Todo.Web/src/app/tasks/task-list.ts`
- Modify: `src/Todo.Web/src/app/tasks/task-list.html`

**Step 1: Komponenten**

`task-row.ts`:

```ts
import { Component, ElementRef, effect, inject, input, output, viewChild } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { DeadlineBucket, TodoStatus, TodoSubTask, TodoTask } from '../api/todo-client';
import { DeadlineDate } from '../i18n/deadline-date';
import { pluralKey } from '../i18n/plural-key';
import { renderMarkdown } from '../markdown/render-markdown';
import { SystemStore } from '../system/system-store';
import { TaskChanges, TaskStore, subTaskProgress } from './task-store';

const statusOptions: readonly TodoStatus[] = [
  TodoStatus.Open,
  TodoStatus.InProgress,
  TodoStatus.WaitingFor,
  TodoStatus.Someday,
  TodoStatus.Done,
];

@Component({
  // Attributvælger, ikke et eget element: en <app-task-row> mellem <ul> og <li> ville skubbe
  // et fremmed element ind i listen, så divide-y ikke længere rammer søskende-rækker.
  // data-testid er derfor en host-binding — TaskListScreen.Rows finder rækken på den.
  selector: 'li[appTaskRow]',
  imports: [DeadlineDate, TranslocoPipe],
  templateUrl: './task-row.html',
  host: {
    'data-testid': 'task-row',
    class: 'py-2',
  },
})
export class TaskRow {
  readonly task = input.required<TodoTask>();
  readonly expanded = input(false);
  readonly editingNote = input(false);

  readonly toggled = output<void>();
  readonly noteEditStarted = output<void>();
  readonly noteEditStopped = output<void>();
  readonly removed = output<void>();

  protected readonly system = inject(SystemStore);
  protected readonly overdue = DeadlineBucket.Overdue;
  protected readonly done = TodoStatus.Done;
  protected readonly waitingFor = TodoStatus.WaitingFor;
  protected readonly statusOptions = statusOptions;
  protected readonly progress = subTaskProgress;

  private readonly store = inject(TaskStore);
  private readonly noteEditor = viewChild<ElementRef<HTMLTextAreaElement>>('note');

  constructor() {
    // Uden denne lader klikket, der åbnede editoren, caret'en stå uden for den, og brugeren
    // skal klikke en gang mere for at skrive.
    effect(() => this.noteEditor()?.nativeElement.focus());
  }

  protected statusKey(status: TodoStatus): string {
    return `tasks.statuses.${status}`;
  }

  protected waitingDaysKey(days: number): string {
    return pluralKey(days, 'tasks.waitingDays');
  }

  protected rendered(task: TodoTask): string {
    return renderMarkdown(task.note);
  }

  protected save(changes: TaskChanges): void {
    this.store.update(this.task(), changes).catch(() => {});
  }

  protected saveStatus(status: string): void {
    this.save({ status: status as TodoStatus });
  }

  protected setDone(isDone: boolean): void {
    this.save({ status: isDone ? TodoStatus.Done : TodoStatus.Open });
  }

  protected stopEditingNote(note: string): void {
    this.save({ note: this.text(note) });
    this.noteEditStopped.emit();
  }

  protected clickNote(event: MouseEvent): void {
    const link = (event.target as HTMLElement).closest('a');

    if (link) {
      // At følge linket i stedet ville erstatte hele appen: vinduet har ingen vej tilbage.
      event.preventDefault();
      this.system.openLink(link.href).catch(() => {});
      return;
    }

    this.noteEditStarted.emit();
  }

  // Parameteren heder ikke `input`: navnet er optaget af input() fra @angular/core, og
  // skyggen ville først bide den dag metoden får brug for den.
  protected createSubTask(field: HTMLInputElement): void {
    const title = field.value;
    if (!title.trim()) {
      return;
    }

    field.value = '';
    this.store.addSubTask(this.task().id, title).catch(() => {});
  }

  protected setSubTaskDone(subTask: TodoSubTask, isDone: boolean): void {
    this.store.setSubTaskDone(this.task().id, subTask, isDone).catch(() => {});
  }

  protected removeSubTask(subTask: TodoSubTask): void {
    this.store.removeSubTask(this.task().id, subTask.id).catch(() => {});
  }

  protected text(value: string): string | undefined {
    return value.trim() || undefined;
  }
}
```

**Step 2: Templaten**

`task-row.html` er linje 128–328 af den gamle `task-list.html` med `<li>`-indpakningen fjernet — værten er `<li>`'et — og fire mekaniske udskiftninger: `task` → `task()`, `expandedId() === task.id` → `expanded()`, `editingNote() === task.id` → `editingNote()`, og de handlere, der ikke længere behøver at få rækken med som argument.

```html
<div class="flex items-start gap-2">
  <input
    #complete
    data-testid="complete-toggle"
    type="checkbox"
    class="mt-1 shrink-0"
    [checked]="task().status === done"
    (click)="$event.stopPropagation()"
    (change)="setDone(complete.checked)"
  />
  <div class="min-w-0 flex-1">
    <button type="button" class="w-full text-left" (click)="toggled.emit()">
      <span class="block text-sm break-words text-gray-900">{{ task().title }}</span>
      @if (task().deadline) {
        <span
          class="block text-xs"
          [class]="task().bucket === overdue ? 'text-red-600 dark:text-red-400' : 'text-gray-500'"
          >{{ 'tasks.deadlineValue' | transloco: { value: task().deadline | deadlineDate } }}</span
        >
      }
      @if (task().requester) {
        <span class="block text-xs text-gray-500">{{
          'tasks.requesterValue' | transloco: { value: task().requester }
        }}</span>
      }
      @if (task().subTasks.length > 0) {
        <span data-testid="subtask-progress" class="block text-xs text-gray-500">{{
          progress(task())
        }}</span>
      }
    </button>
    <!-- Uden for knappen med vilje: teksten ville ellers indgå i rækkens tilgængelige navn,
         som E2E-skærmen matcher i sin helhed. -->
    @if (task().status === waitingFor) {
      <p class="flex flex-wrap gap-x-2 text-xs text-gray-600 dark:text-gray-400">
        @if (task().waitingOn) {
          <span>{{ 'tasks.waitingOnValue' | transloco: { value: task().waitingOn } }}</span>
        }
        <!-- @let, ikke `as`: waitingDays kan være 0, og `as` binder på sandhed. -->
        @let waitingDays = task().waitingDays;
        @if (waitingDays != null) {
          <span data-testid="waiting-days">{{
            waitingDaysKey(waitingDays) | transloco: { count: waitingDays }
          }}</span>
        }
      </p>
    }
  </div>
</div>
@if (expanded()) {
  <div data-testid="task-detail" class="mt-2 space-y-2 rounded bg-gray-50 p-2">
    <label class="block">
      <span class="block text-xs text-gray-500">{{ 'tasks.deadline' | transloco }}</span>
      <input
        #deadline
        type="date"
        class="w-full rounded border border-gray-300 px-2 py-1 text-sm"
        [value]="task().deadline ?? ''"
        (blur)="save({ deadline: text(deadline.value) })"
        (keyup.enter)="save({ deadline: text(deadline.value) })"
      />
    </label>
    <label class="block">
      <span class="block text-xs text-gray-500">{{ 'tasks.requester' | transloco }}</span>
      <input
        #requester
        type="text"
        class="w-full rounded border border-gray-300 px-2 py-1 text-sm"
        [value]="task().requester ?? ''"
        (blur)="save({ requester: text(requester.value) })"
        (keyup.enter)="save({ requester: text(requester.value) })"
      />
    </label>
    <div class="space-y-1">
      <div class="flex items-center justify-between gap-2">
        <span class="block text-xs text-gray-500">{{ 'tasks.note' | transloco }}</span>
        @if (!editingNote()) {
          <button
            type="button"
            data-testid="note-edit"
            class="shrink-0 text-xs text-blue-700 underline dark:text-blue-300"
            (click)="noteEditStarted.emit()"
          >
            {{ 'tasks.editNote' | transloco }}
          </button>
        }
      </div>
      @if (editingNote()) {
        <textarea
          #note
          data-testid="note-editor"
          rows="6"
          class="field-sizing-content max-h-96 w-full rounded border border-gray-300 px-2 py-1 text-sm"
          [value]="task().note ?? ''"
          (blur)="stopEditingNote(note.value)"
          (keydown.escape)="stopEditingNote(note.value)"
        ></textarea>
      } @else if (task().note?.trim()) {
        <div
          data-testid="note-rendered"
          class="prose prose-sm dark:prose-invert max-w-none [&_pre]:overflow-x-auto [&_table]:block [&_table]:overflow-x-auto"
          [innerHTML]="rendered(task())"
          (click)="clickNote($event)"
        ></div>
      } @else {
        <p
          data-testid="note-rendered"
          class="text-sm text-gray-500 italic"
          (click)="clickNote($event)"
        >
          {{ 'tasks.noteEmpty' | transloco }}
        </p>
      }
      @if (system.error(); as message) {
        <p data-testid="note-link-error" role="alert" class="text-sm text-red-700 dark:text-red-300">
          {{ message }}
        </p>
      }
    </div>
    <label class="block">
      <span class="block text-xs text-gray-500">{{ 'tasks.status' | transloco }}</span>
      <select
        #status
        class="w-full rounded border border-gray-300 px-2 py-1 text-sm"
        (change)="saveStatus(status.value)"
      >
        @for (option of statusOptions; track option) {
          <option [value]="option" [selected]="option === task().status">
            {{ statusKey(option) | transloco }}
          </option>
        }
      </select>
    </label>
    @if (task().status === waitingFor) {
      <label class="block">
        <span class="block text-xs text-gray-500 dark:text-gray-400">{{
          'tasks.waitingOn' | transloco
        }}</span>
        <input
          #waitingOn
          data-testid="waiting-on-input"
          type="text"
          class="w-full rounded border border-gray-300 px-2 py-1 text-sm dark:border-gray-600 dark:text-gray-100"
          [value]="task().waitingOn ?? ''"
          (blur)="save({ waitingOn: text(waitingOn.value) })"
          (keyup.enter)="save({ waitingOn: text(waitingOn.value) })"
        />
      </label>
    }
    <div class="space-y-1">
      <span class="block text-xs text-gray-500">{{ 'tasks.subTasks' | transloco }}</span>
      @for (subTask of task().subTasks; track subTask.id) {
        <div data-testid="subtask-row" class="flex items-start gap-2">
          <input
            #subTaskDone
            type="checkbox"
            class="mt-1 shrink-0"
            [checked]="subTask.isDone"
            (click)="$event.stopPropagation()"
            (change)="setSubTaskDone(subTask, subTaskDone.checked)"
          />
          <span
            class="min-w-0 flex-1 text-sm break-words"
            [class]="subTask.isDone ? 'text-gray-400 line-through' : 'text-gray-900'"
            >{{ subTask.title }}</span
          >
          <button
            type="button"
            data-testid="delete-subtask"
            [attr.aria-label]="'tasks.deleteSubTask' | transloco"
            class="shrink-0 text-xs text-red-600"
            (click)="removeSubTask(subTask)"
          >
            ✕
          </button>
        </div>
      }
      <input
        #newSubTask
        data-testid="new-subtask-input"
        type="text"
        [placeholder]="'tasks.newSubTask' | transloco"
        class="w-full rounded border border-gray-300 px-2 py-1 text-sm"
        (keyup.enter)="createSubTask(newSubTask)"
      />
    </div>
    <button
      type="button"
      data-testid="delete-task"
      class="text-xs text-red-600 underline"
      (click)="removed.emit()"
    >
      {{ 'tasks.deleteTask' | transloco }}
    </button>
  </div>
}
```

**Step 3: Slank forælderen**

`task-list.ts` mister alt det, barnet nu ejer. Bemærk at `done` og `setDone` **bliver** — den færdige række bruger dem stadig — mens `text`, `save` og `saveStatus` forsvinder helt, fordi barnet gemmer noten selv:

```ts
import { Component, computed, inject, signal } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { DeadlineBucket, TodoStatus, TodoTask } from '../api/todo-client';
import { SystemStore } from '../system/system-store';
import { TaskRow } from './task-row';
import { TaskStore } from './task-store';

@Component({
  selector: 'app-task-list',
  imports: [TaskRow, TranslocoPipe],
  templateUrl: './task-list.html',
})
export class TaskList {
  protected readonly store = inject(TaskStore);
  protected readonly overdue = DeadlineBucket.Overdue;
  protected readonly done = TodoStatus.Done;
  protected readonly expandedId = signal<string | null>(null);
  protected readonly editingNote = signal<string | null>(null);
  protected readonly completed = computed(() =>
    this.store.showCompleted() ? this.store.completedTasks() : [],
  );
  protected readonly someday = computed(() =>
    this.store.showSomeday() ? this.store.somedayTasks() : [],
  );

  private readonly system = inject(SystemStore);

  constructor() {
    // Et fejlet load behøver ingen besked af sig selv: health-linjen melder allerede API'et nede.
    this.store.load().catch(() => {});
  }

  // API'ets enum-værdier er nøglens blade, så en ny bucket kræver ingen mapning her.
  protected sectionKey(bucket: DeadlineBucket): string {
    return `tasks.sections.${bucket}`;
  }

  protected create(field: HTMLInputElement): void {
    const title = field.value;
    if (!title.trim()) {
      return;
    }

    field.value = '';
    this.store.add(title).catch(() => {});
  }

  protected toggle(task: TodoTask): void {
    this.system.clearError();
    this.editingNote.set(null);
    this.expandedId.update((id) => (id === task.id ? null : task.id));
  }

  protected editNote(task: TodoTask): void {
    this.editingNote.set(task.id);
  }

  protected stopEditingNote(): void {
    this.editingNote.set(null);
  }

  protected setDone(task: TodoTask, isDone: boolean): void {
    this.store.update(task, { status: isDone ? TodoStatus.Done : TodoStatus.Open }).catch(() => {});
  }

  protected setShowCompleted(value: boolean): void {
    this.store.setShowCompleted(value).catch(() => {});
  }

  protected setShowSomeday(value: boolean): void {
    this.store.setShowSomeday(value).catch(() => {});
  }

  protected remove(task: TodoTask): void {
    this.editingNote.set(null);
    this.expandedId.set(null);
    this.store.remove(task.id).catch(() => {});
  }
}
```

**Step 4: Skift de tre `ng-container`-blokke ud**

I `task-list.html` erstattes hver af de tre `<ng-container [ngTemplateOutlet]="taskRow" …/>` (i deadline-sektionerne, i "venter på" og i Someday) med samme blok, og hele `<ng-template #taskRow>` til sidst i filen slettes:

```html
        @for (task of section.tasks; track task.id) {
          <li
            appTaskRow
            [task]="task"
            [expanded]="expandedId() === task.id"
            [editingNote]="editingNote() === task.id"
            (toggled)="toggle(task)"
            (noteEditStarted)="editNote(task)"
            (noteEditStopped)="stopEditingNote()"
            (removed)="remove(task)"
          ></li>
        }
```

De to andre steder itererer over `store.waitingTasks()` og `someday()`; kun `@for`-linjen er forskellig. Tre næsten identiske blokke er prisen for en typet kontekst, og det er hele pointen med skiven — læg dem **ikke** tilbage i en delt `ng-template`.

Resten af `task-list.html` — inputfeltet, de to kontakter, sektionsoverskrifterne og den færdige rækkes inline-`<li>` — er uændret.

**Step 5: Byg**

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
```

Forventet: lykkes. Får du `TS2345` om `number | undefined`, har du glemt `@let` fra Step 2.

**Step 6: Kør Vitest**

```
npm.cmd run test --prefix src\Todo.Web -- --watch=false
```

Forventet: **133 beståede.** Alle 29 tests i `task-list.spec.ts` går gennem forælderen og skal bestå **uændret** — de er regressionsnettet under udtrækket. Skal en af dem rettes for at blive grøn, er DOM'en eller adfærden ændret; stop og rapportér frem for at tilpasse testen.

**Step 7: Formatér kun de filer du har rørt**

Aldrig hele repoet — arbejdskopien er CRLF, og en fuld kørsel omskriver 3810 linjer genereret klientkode:

```
npm.cmd exec --prefix src\Todo.Web -- prettier --write src/app/tasks/task-row.ts src/app/tasks/task-row.html src/app/tasks/task-list.ts src/app/tasks/task-list.html
```

**Step 8: Commit**

```
git add src/Todo.Web/src/app/tasks
git commit -m "♻️ Udtræk rækken til en typet børnekomponent"
```

---

## Task 4: Se vagten fejle

Nu skal den tastefejl, der var grøn i Task 2, være rød. Ingen commit.

**Step 1: Samme tastefejl, nyt sted**

I `src/Todo.Web/src/app/tasks/task-row.html`, titel-linjen i knappen:

```html
      <span class="block text-sm break-words text-gray-900">{{ task().titel }}</span>
```

**Step 2: Byg**

```
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
```

Forventet: **fejl**, med `TodoTask` nævnt ved navn:

```
src/app/tasks/task-row.html:… - error TS2551: Property 'titel' does not exist on type 'TodoTask'. Did you mean 'title'?
```

Rapportér fejlteksten. Uden den er skiven ikke bevist.

**Step 3: Prøv også en fejl, der kun `strictTemplates` fanger**

Sæt titel-linjen tilbage, og giv i stedet inputtet en forkert type i `task-list.html`:

```html
            [expanded]="expandedId()"
```

`expandedId()` er `string | null`, inputtet er `boolean`. Forventet: fejl om `string | null` mod `boolean`. Det er den halvdel, Task 1's flag leverede — uden `strictTemplates` ville den passere.

**Step 4: Rul tilbage**

```
git checkout -- src/Todo.Web/src/app/tasks
git status --porcelain
```

Forventet: intet output.

---

## Task 5: E2E og dokumentation

**Files:**
- Modify: `CLAUDE.md`, `docs/HANDOFF.md`, `docs/plans/2026-08-13-todo-app-design.md`

**Step 1: Kør E2E**

```
dotnet test tests/Todo.E2E/Todo.E2E.csproj
```

Forventet: **7 beståede.** Disse tests er den egentlige prøve på, at DOM'en ikke flyttede sig: `RowTitled` matcher rækkeknappens fulde tilgængelige navn, og `Rows` finder rækken på `data-testid`, som nu kommer fra en host-binding. Fejler en af dem, er attributvælgeren eller host-bindingen forkert — ikke testen.

**Step 2: Kør resten**

```
dotnet test Todo.sln
```

Forventet: **33 Todo.Core.Tests, 106 Todo.Api.Tests, 7 Todo.E2E.** Uændret — skiven rører ikke backend.

**Step 3: Rettelse af premisset i `docs/HANDOFF.md`**

Punkt 1 under "Anbefalet rækkefølge" begrunder strict mode med, at tastefejlen gav et grønt build fordi `strict` manglede. Det er målt forkert. Erstat begrundelsen med den faktiske: den typede del af templaten var altid tjekket, `#taskRow` var ikke, og `strictTemplates` dækkede ikke hullet — børnekomponenten gjorde. Flyt skiven til "Færdigt"-tabellen.

**Step 4: Samme rettelse i designdokumentet**

Afsnit 9, "Ønsket, men ikke placeret endnu": punktet **TypeScript i strict mode** bærer samme forkerte begrundelse. Ret den, og flyt punktet ind i den nummererede liste som skive 6. **Det gør tilgængelighed til skive 7 og `long` som id til skive 8** — omnummerér dem begge, og ret krydsreferencerne i afsnit 10 og i `docs/HANDOFF.md`.

**Step 5: Testtal i `CLAUDE.md`**

Afsnittet "Testtal" siger "Efter skive 5". Skift til "Efter skive 6" med de tal, du faktisk målte. Er Vitest-tallet stadig 133, så skriv 133 — udtrækket skal ikke tilføje tests, og et højere tal betyder en duplikeret test.

**Step 6: En linje til konventionerne**

Under **Angular** i `CLAUDE.md` hører den lære, der kostede denne skive:

> **En delt `<ng-template>` med `let-`-variabler har konteksttype `any`.** `strictTemplates`
> tjekker den ikke, og `[ngTemplateOutletContext]` afstemmes ikke. Skal en række være
> typetjekket, skal den være en komponent med `input()`. Bruger den `<li>`, så giv den en
> attributvælger (`li[appTaskRow]`) — et eget element ville bryde `divide-y` og listens struktur.
>
> **`@if` indsnævrer ikke et signal-kald.** `@if (task().x != null)` efterlader `task().x`
> som `T | undefined` inde i blokken. Bind med `@let` først. Brug **ikke** `as`, som binder
> på sandhed og taber `0`.

**Step 7: Commit**

```
git add CLAUDE.md docs/HANDOFF.md docs/plans/2026-08-13-todo-app-design.md
git commit -m "📝 Ret premisset for strict mode og omnummerér de sidste skiver"
```

---

## Færdig når

- `strict` og `strictTemplates` er slået til, og `scripts\build-web.ps1` er grøn.
- `#taskRow` findes ikke længere; rækken er `TaskRow` med `input.required<TodoTask>()`.
- **Task 4 er rapporteret med fejltekst.** En tastefejl i rækketemplaten bryder bygningen, og et input af forkert type gør det også.
- 133 Vitest, 7 E2E, 33 Core, 106 Api — alle grønne, alle uændrede i antal.
- `task-list.spec.ts` er ikke rettet for at blive grøn.
- Premisset er rettet i både `HANDOFF.md` og designdokumentet, og skiverne er omnummereret.

## Til skive 7 (tilgængelighed, tastatur og dark mode)

Udtrækket her gør den skive lettere på ét punkt og ændrer intet andet: rækkens farver ligger nu i `task-row.html` alene, så kontrastgennemgangen af en række kan læses i én fil frem for at være vævet ind i sektionerne. `<body>` sætter stadig hverken baggrund eller tekstfarve, og `text-gray-400` på health-linjen er stadig ~2,9:1.
