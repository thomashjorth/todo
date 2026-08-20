import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { API_BASE_URL, IRetroPreviewRow, RetroPreviewRow } from '../api/todo-client';
import { translocoTesting } from '../i18n/transloco.testing';
import { RetroStore } from './retro-store';

const previewBody = {
  rows: [
    {
      key: 'aaaa',
      title: 'Write the retro summary',
      owner: 'Thomas Hjorth',
      author: 'Thomas Hjorth',
      zone: 'Actions',
      deadline: '2026-07-24',
      isMine: true,
      alreadyImported: false,
    },
    {
      key: 'bbbb',
      title: 'Book a room for the next one',
      owner: 'Mette Kirkegaard',
      author: 'Mette Kirkegaard',
      zone: 'Actions',
      deadline: null,
      isMine: false,
      alreadyImported: false,
    },
  ],
  skippedRatingCards: 3,
};

function row(overrides: Partial<IRetroPreviewRow> = {}): RetroPreviewRow {
  return new RetroPreviewRow({
    key: 'aaaa',
    title: 'Write the retro summary',
    owner: 'Thomas Hjorth',
    zone: 'Actions',
    deadline: '2026-07-24',
    isMine: true,
    alreadyImported: false,
    ...overrides,
  });
}

describe('RetroStore', () => {
  let store: RetroStore;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [translocoTesting()],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: API_BASE_URL, useValue: '' },
      ],
    });
    store = TestBed.inject(RetroStore);
    http = TestBed.inject(HttpTestingController);
  });

  it('should hold nothing before an export is analysed', () => {
    expect(store.rows()).toEqual([]);
    expect(store.skippedRatingCards()).toBe(0);
    expect(store.aliases()).toEqual([]);
    expect(store.error()).toBeNull();
  });

  it('should keep the rows and the skipped count the preview returned', async () => {
    const previewed = store.preview('"Content"\n"Write the retro summary"');

    const request = http.expectOne('/api/retro/preview');
    expect(request.request.method).toBe('POST');
    expect(JSON.parse(request.request.body).csv).toContain('Content');
    request.flush(new Blob([JSON.stringify(previewBody)]));
    await previewed;

    expect(store.rows().map((r) => r.title)).toEqual([
      'Write the retro summary',
      'Book a room for the next one',
    ]);
    expect(store.rows()[0].isMine).toBe(true);
    expect(store.rows()[1].deadline).toBeNull();
    expect(store.skippedRatingCards()).toBe(3);
    expect(store.error()).toBeNull();
  });

  it('should translate the code the server rejected the export with', async () => {
    const message = "The retro export is empty. It needs a header row with a 'Content' column.";
    const previewed = store.preview('');

    http
      .expectOne('/api/retro/preview')
      .flush(new Blob([JSON.stringify({ code: 'retro.emptyExport', message })]), {
        status: 400,
        statusText: 'Bad Request',
      });
    await previewed;

    expect(store.error()).toBe(
      'Eksporten er tom. Den skal have en overskriftsrække med en Content-kolonne.',
    );
    expect(store.rows()).toEqual([]);
    expect(store.skippedRatingCards()).toBe(0);
  });

  it('should show the server message for a code no translation file knows', async () => {
    const message = 'The board is on fire.';
    const previewed = store.preview('');

    http
      .expectOne('/api/retro/preview')
      .flush(new Blob([JSON.stringify({ code: 'retro.boardOnFire', message })]), {
        status: 400,
        statusText: 'Bad Request',
      });
    await previewed;

    expect(store.error()).toBe(message);
  });

  it('should drop the previous rows when a later export is rejected', async () => {
    const previewed = store.preview('a board');
    http.expectOne('/api/retro/preview').flush(new Blob([JSON.stringify(previewBody)]));
    await previewed;

    const rejected = store.preview('');
    http
      .expectOne('/api/retro/preview')
      .flush(new Blob([JSON.stringify({ code: 'retro.emptyExport', message: 'nope' })]), {
        status: 400,
        statusText: 'Bad Request',
      });
    await rejected;

    expect(store.rows()).toEqual([]);
  });

  it('should send the owner as the requester of every imported row', async () => {
    const imported = store.import([
      row(),
      row({ key: 'bbbb', title: 'Book a room', owner: undefined, deadline: undefined }),
    ]);

    const request = http.expectOne('/api/retro/import');
    expect(request.request.method).toBe('POST');
    expect(JSON.parse(request.request.body)).toEqual({
      rows: [
        {
          key: 'aaaa',
          title: 'Write the retro summary',
          requester: 'Thomas Hjorth',
          deadline: '2026-07-24',
        },
        { key: 'bbbb', title: 'Book a room' },
      ],
    });
    request.flush(new Blob([JSON.stringify({ imported: 2, skipped: 0 })]));

    expect((await imported)?.imported).toBe(2);
  });

  it('should send no request when no rows were selected', async () => {
    expect(await store.import([])).toBeUndefined();

    http.verify();
  });

  it('should read the stored aliases', async () => {
    const loaded = store.loadAliases();

    const request = http.expectOne('/api/retro/aliases');
    expect(request.request.method).toBe('GET');
    request.flush(new Blob([JSON.stringify({ aliases: ['TH', 'Thomas Hjorth'] })]));
    await loaded;

    expect(store.aliases()).toEqual(['TH', 'Thomas Hjorth']);
  });

  it('should keep the alias list the server saved, not the one it was sent', async () => {
    const saved = store.saveAliases(['  Thomas Hjorth  ', 'TH']);

    const request = http.expectOne('/api/retro/aliases');
    expect(request.request.method).toBe('PUT');
    expect(JSON.parse(request.request.body)).toEqual({ aliases: ['  Thomas Hjorth  ', 'TH'] });
    request.flush(new Blob([JSON.stringify({ aliases: ['TH', 'Thomas Hjorth'] })]));
    await saved;

    expect(store.aliases()).toEqual(['TH', 'Thomas Hjorth']);
  });

  it('should show the reason the server rejected an alias list', async () => {
    const saved = store.saveAliases(['Thomas', 'thomas']);

    http.expectOne('/api/retro/aliases').flush(
      new Blob([
        JSON.stringify({
          code: 'retro.duplicateAlias',
          message: "'thomas' is listed more than once.",
        }),
      ]),
      { status: 400, statusText: 'Bad Request' },
    );
    await saved;

    expect(store.error()).toBe('Det samme navn står på listen mere end én gang.');
  });
});
