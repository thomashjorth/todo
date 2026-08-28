import { Routes } from '@angular/router';
import { AdoImport } from './ado/ado-import';
import { JiraImport } from './jira/jira-import';
import { RetroImport } from './retro/retro-import';
import { Review } from './review/review';
import { Settings } from './settings/settings';
import { TaskList } from './tasks/task-list';

export const routes: Routes = [
  { path: '', component: TaskList },
  { path: 'review', component: Review },
  { path: 'import', component: RetroImport },
  { path: 'jira', component: JiraImport },
  { path: 'ado', component: AdoImport },
  { path: 'settings', component: Settings },
];
