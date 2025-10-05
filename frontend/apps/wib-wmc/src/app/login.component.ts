import { Component, effect, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { Router, ActivatedRoute, RouterLink } from '@angular/router';
import { AuthService } from './auth.service';

@Component({
  selector: 'wib-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css'],
})
export class LoginComponent {
  error = signal<string>('');
  returnUrl = signal<string>('/dashboard');

  form = this.fb.group({
    username: this.fb.control('admin', [Validators.required]),
    password: this.fb.control('admin', [Validators.required]),
  });

  constructor(
    private fb: FormBuilder,
    private auth: AuthService,
    private router: Router,
    private route: ActivatedRoute,
  ) {
    const ret = this.route.snapshot.queryParamMap.get('returnUrl');
    if (ret) this.returnUrl.set(ret);
    // auto-redirect if a valid token is already present
    const tok = this.auth.getToken();
    if (tok && !this.auth.isTokenExpired(tok)) this.router.navigate([this.returnUrl()]);
  }

  submit() {
    this.error.set('');
    if (this.form.invalid) { this.error.set('Username e password richiesti'); return; }
    const { username, password } = this.form.value as any;
    this.auth.login(username, password).subscribe({
      next: (t) => {
        if (t) this.router.navigate([this.returnUrl()]);
        else this.error.set('Risposta di login non valida');
      },
      error: (e) => this.error.set(e?.message || 'Login fallito'),
    });
  }
}

