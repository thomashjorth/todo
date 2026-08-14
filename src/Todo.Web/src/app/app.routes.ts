import { Routes } from '@angular/router';
import { RetroImport } from './retro/retro-import';
import { Settings } from './settings/settings';
import { TaskList } from './tasks/task-list';

export const routes: Routes = [
  { path: '', component: TaskList },
  { path: 'import', component: RetroImport },
  { path: 'settings', component: Settings },
];
