import { Component, inject } from '@angular/core';
import { TranslocoPipe } from '@jsverse/transloco';
import { AuthStore } from './auth-store';

@Component({
  selector: 'app-login',
  imports: [TranslocoPipe],
  templateUrl: './login.html',
})
export class Login {
  protected readonly auth = inject(AuthStore);

  // A native form with a submit handler rather than Angular forms, as everywhere in the app: Enter in
  // the field and a click on the button are the same event, and a password manager recognises it.
  protected submit(event: Event, field: HTMLInputElement): void {
    event.preventDefault();
    this.auth.login(field.value).catch(() => {});
  }
}
