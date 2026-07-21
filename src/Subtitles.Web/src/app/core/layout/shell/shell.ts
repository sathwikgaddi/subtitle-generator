import { Component, inject } from '@angular/core';
import { Router, RouterOutlet } from '@angular/router';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatButtonModule } from '@angular/material/button';
import { Auth } from '../../auth/auth';

/** App shell for authenticated routes — toolbar with brand + account menu, then the page. */
@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, MatIconModule, MatMenuModule, MatToolbarModule, MatButtonModule],
  templateUrl: './shell.html',
  styleUrl: './shell.scss',
})
export class Shell {
  private readonly auth = inject(Auth);
  private readonly router = inject(Router);

  signOut(): void {
    this.auth.logout();
    this.router.navigateByUrl('/login');
  }
}
