import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { AiStore } from '../ai/ai-store';
import { DeadlineBucket, TodoStatus, TodoTask } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { ShortcutStore } from '../shortcuts/shortcut-store';
import { TaskDetail } from './task-detail';

// The status is waitingFor on purpose: the who field only exists behind it, so Alt+Shift+V would
// be missing on an open task. The note is left unopened for the matching reason, because the
// letter sits on the "Redigér noten" button and not on the editor - with the editor open the
// button does not exist.
const task = new TodoTask({
  id: 1,
  sourceId: 'manual',
  title: 'Betal regningen',
  status: TodoStatus.WaitingFor,
  bucket: DeadlineBucket.NoDeadline,
  createdAt: '2026-08-13T18:25:56.60+00:00',
  subTasks: [],
});

function titleField(fixture: ComponentFixture<TaskDetail>): HTMLInputElement {
  // The getAttribute selector rather than dataset.testid: noPropertyAccessFromIndexSignature is
  // on, so a lookup on dataset stops ng test's own build with TS4111.
  const field = (fixture.nativeElement as HTMLElement).querySelector('[data-testid="title-input"]');

  return field as HTMLInputElement;
}

function panel(): ComponentFixture<TaskDetail> {
  TestBed.configureTestingModule({
    imports: [translocoTesting()],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  });
  const fixture = TestBed.createComponent(TaskDetail);
  fixture.componentRef.setInput('task', task);
  fixture.detectChanges();

  return fixture;
}

describe('TaskDetail', () => {
  // The order is the document's, not the table's: it is the assertion's teeth, because a
  // comparison of sets would pass if two fields swapped letters.
  it('should announce one Alt+Shift letter per field', () => {
    const element = panel().nativeElement as HTMLElement;

    const labels = Array.from(element.querySelectorAll('[aria-keyshortcuts]')).map((badge) =>
      badge.getAttribute('aria-keyshortcuts'),
    );

    expect(labels).toEqual([
      'Alt+Shift+I',
      'Alt+Shift+D',
      'Alt+Shift+S',
      'Alt+Shift+O',
      'Alt+Shift+N',
      'Alt+Shift+T',
      'Alt+Shift+V',
      'Alt+Shift+U',
      'Alt+Shift+L',
    ]);
  });

  // aria-hidden is load-bearing rather than decoration: a badge inside a <label> or a <button>
  // otherwise counts towards the control's accessible name, and the E2E suite matches names
  // whole. The guard belongs here, because no E2E journey holds Alt down with the panel open -
  // measured, a removed aria-hidden fells no journey at all.
  it('should keep every badge out of its control accessible name', () => {
    const fixture = panel();
    TestBed.inject(ShortcutStore).setAltHeld(true);
    fixture.detectChanges();

    const badges = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="shortcut-badge"]'),
    ).map((badge) => `${badge.textContent}:${badge.getAttribute('aria-hidden')}`);

    expect(badges).toEqual([
      '⇧I:true',
      '⇧D:true',
      '⇧S:true',
      '⇧O:true',
      '⇧N:true',
      '⇧T:true',
      '⇧V:true',
      '⇧U:true',
      '⇧L:true',
    ]);
  });

  // The guard on the [value] trap, which is the [checked] trap one field over: rolling the old
  // title back does not change the signal, so the binding has nothing to do and the field would
  // stand empty while the task kept its title. Hence TWO assertions - that nothing was sent, and
  // that the field shows the title again. The first alone can pass on a field standing empty,
  // which is precisely the defect.
  // Whitespace rather than the empty string: it proves the trim runs before the emptiness check.
  it('should put the old title back when the field is emptied', () => {
    const fixture = panel();
    const field = titleField(fixture);

    field.value = '   ';
    field.dispatchEvent(new Event('blur'));

    TestBed.inject(HttpTestingController).expectNone((request) => request.method === 'PUT');
    expect(field.value).toBe('Betal regningen');
  });

  describe('AI', () => {
    const imported = new TodoTask({
      ...task.toJSON(),
      id: 2,
      sourceId: 'jira',
      status: TodoStatus.Open,
      note: 'En lang beskrivelse fra Jira.',
      externalUrl: 'https://jira.example.com/browse/SAAS-1',
    });

    function panelFor(shown: TodoTask, ready: boolean): ComponentFixture<TaskDetail> {
      const fixture = panel();
      TestBed.inject(AiStore).ready.set(ready);
      fixture.componentRef.setInput('task', shown);
      fixture.detectChanges();

      return fixture;
    }

    const has = (fixture: ComponentFixture<TaskDetail>, id: string) =>
      (fixture.nativeElement as HTMLElement).querySelector(`[data-testid="${id}"]`) !== null;

    it('should offer no AI action while the model is not ready', () => {
      const fixture = panelFor(imported, false);

      expect(has(fixture, 'split-task')).toBe(false);
      expect(has(fixture, 'summarize-note')).toBe(false);
    });

    // The order is the document's: the summary's R sits beside the note's N, and the split's F under
    // the new-subtask field's U. A comparison of sets would pass if two letters swapped.
    it('should announce the AI actions in the panel layer, beside the field they belong to', () => {
      const element = panelFor(imported, true).nativeElement as HTMLElement;

      const labels = Array.from(element.querySelectorAll('[aria-keyshortcuts]')).map((el) =>
        el.getAttribute('aria-keyshortcuts'),
      );

      expect(labels).toEqual([
        'Alt+Shift+I',
        'Alt+Shift+D',
        'Alt+Shift+S',
        'Alt+Shift+O',
        'Alt+Shift+R',
        'Alt+Shift+N',
        'Alt+Shift+T',
        'Alt+Shift+U',
        'Alt+Shift+F',
        'Alt+Shift+L',
      ]);
    });

    // On a task of your own the note is the only copy, so replacing it with a summary would lose it.
    it('should offer a summary only on an imported task with a note', () => {
      expect(has(panelFor(imported, true), 'summarize-note')).toBe(true);
      TestBed.resetTestingModule();
      expect(
        has(
          panelFor(new TodoTask({ ...imported.toJSON(), externalUrl: undefined }), true),
          'summarize-note',
        ),
      ).toBe(false);
      TestBed.resetTestingModule();
      expect(
        has(panelFor(new TodoTask({ ...imported.toJSON(), note: '  ' }), true), 'summarize-note'),
      ).toBe(false);
    });

    // Side by side and in one column alike, a panel shows only what was asked for its own task.
    it('should show suggested subtasks in the panel of their own task only, all ticked', () => {
      const fixture = panelFor(imported, true);
      const ai = TestBed.inject(AiStore);

      ai.subTaskSuggestions.set({ taskId: 99, value: [{ title: 'Andetsteds', chosen: true }] });
      fixture.detectChanges();
      expect(has(fixture, 'split-suggestions')).toBe(false);

      ai.subTaskSuggestions.set({
        taskId: imported.id,
        value: [
          { title: 'Find ejeren', chosen: true },
          { title: 'Bestil certifikat', chosen: true },
        ],
      });
      fixture.detectChanges();
      const boxes = Array.from(
        (fixture.nativeElement as HTMLElement).querySelectorAll<HTMLInputElement>(
          '[data-testid="split-choice"]',
        ),
      );
      expect(boxes.map((box) => box.checked)).toEqual([true, true]);
      expect(has(fixture, 'split-task')).toBe(false);
    });
  });

  it('should save the title without its surrounding whitespace', () => {
    const fixture = panel();
    const field = titleField(fixture);

    field.value = '  Betal den store regning  ';
    field.dispatchEvent(new Event('blur'));

    const request = TestBed.inject(HttpTestingController).expectOne(
      (candidate) => candidate.method === 'PUT' && candidate.url === '/api/tasks/1',
    );

    // The body is a string: the generated client sends JSON.stringify(body) as the content.
    expect(JSON.parse(request.request.body as string).title).toBe('Betal den store regning');
  });
});
