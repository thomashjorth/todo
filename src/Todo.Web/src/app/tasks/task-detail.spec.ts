import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
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

function titleField(fixture: ComponentFixture<TaskDetail>): HTMLInputElement {
  // getAttribute-vælgeren og ikke dataset.testid: noPropertyAccessFromIndexSignature er slået til,
  // så et opslag på dataset stopper ng test's egen bygning med TS4111.
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
  // Rækkefølgen er dokumentets, ikke tabellens: den er assertionens tænder, for en sammenligning
  // af mængder ville bestå, hvis to felter byttede bogstav.
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

  // Vagten på [value]-fælden, som er [checked]-fælden ét felt over: ruller vi den gamle titel
  // tilbage, skifter signalet ikke, så bindingen har intet at gøre og feltet ville stå tomt, mens
  // opgaven beholdt sin titel. Derfor TO påstande — at intet blev sendt, og at feltet viser titlen
  // igen. Den første alene kan bestå på et felt der står tomt, altså netop fejlen.
  // Mellemrum og ikke den tomme streng: den beviser at trimningen løber før tomhedstjekket.
  it('should put the old title back when the field is emptied', () => {
    const fixture = panel();
    const field = titleField(fixture);

    field.value = '   ';
    field.dispatchEvent(new Event('blur'));

    TestBed.inject(HttpTestingController).expectNone((request) => request.method === 'PUT');
    expect(field.value).toBe('Betal regningen');
  });

  it('should save the title without its surrounding whitespace', () => {
    const fixture = panel();
    const field = titleField(fixture);

    field.value = '  Betal den store regning  ';
    field.dispatchEvent(new Event('blur'));

    const request = TestBed.inject(HttpTestingController).expectOne(
      (candidate) => candidate.method === 'PUT' && candidate.url === '/api/tasks/1',
    );

    // Kroppen er en streng: den genererede klient sender JSON.stringify(body) som indhold.
    expect(JSON.parse(request.request.body as string).title).toBe('Betal den store regning');
  });
});
