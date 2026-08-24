import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { DeadlineBucket, TodoStatus, TodoTask } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { ShortcutStore } from '../shortcuts/shortcut-store';
import { TaskDetail } from './task-detail';

// Statussen er waitingFor med vilje: hvem-feltet findes kun bag den, så Alt+Shift+V ville mangle
// på en åben opgave. Noten er tilsvarende ulukket, fordi bogstavet sidder på knappen "Redigér
// noten" og ikke på editoren — er editoren åben, findes knappen ikke.
const task = new TodoTask({
  id: 1,
  sourceId: 'manual',
  title: 'Betal regningen',
  status: TodoStatus.WaitingFor,
  bucket: DeadlineBucket.NoDeadline,
  createdAt: '2026-08-13T18:25:56.60+00:00',
  subTasks: [],
});

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
  // Rækkefølgen er dokumentets, ikke tabellens: den er assertionens tænder, for en sammenligning
  // af mængder ville bestå, hvis to felter byttede bogstav.
  it('should announce one Alt+Shift letter per field', () => {
    const element = panel().nativeElement as HTMLElement;

    const labels = Array.from(element.querySelectorAll('[aria-keyshortcuts]')).map((badge) =>
      badge.getAttribute('aria-keyshortcuts'),
    );

    expect(labels).toEqual([
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

  // aria-hidden er bærende frem for pynt: en mærkat inde i en <label> eller en <button> indgår
  // ellers i kontrollens tilgængelige navn, og E2E-suiten matcher navne i deres helhed. Vagten
  // hører hjemme her, fordi ingen E2E holder Alt nede med panelet åbent — målt, en fjernet
  // aria-hidden fælder ingen af de 61 rejser.
  it('should keep every badge out of its control accessible name', () => {
    const fixture = panel();
    TestBed.inject(ShortcutStore).setAltHeld(true);
    fixture.detectChanges();

    const badges = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('[data-testid="shortcut-badge"]'),
    ).map((badge) => `${badge.textContent}:${badge.getAttribute('aria-hidden')}`);

    expect(badges).toEqual([
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
});
