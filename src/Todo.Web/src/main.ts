import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
import { connectionsConfig } from './app/connections/connections.config';
import { ConnectionsRoot } from './app/connections/connections-root';

// Two roots in one bundle: the helper serves this same bundle on the PC, and on /connections it is
// the helper's Connections page rather than the app. The server answers 404 on that path, so the page
// only ever starts from the helper's origin.
if (window.location.pathname === '/connections') {
  bootstrapApplication(ConnectionsRoot, connectionsConfig).catch((err) => console.error(err));
} else {
  bootstrapApplication(App, appConfig).catch((err) => console.error(err));
}
