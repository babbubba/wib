import { Component, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, Validators } from '@angular/forms';
import { AuthService } from './auth.service';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './login.component.html',
  styleUrls: ['./login.component.css'],
})
export class LoginComponent {
  error = signal<string>('');

  form = this.fb.group({
    username: this.fb.control('user', [Validators.required]),
    password: this.fb.control('user', [Validators.required]),
  });

  constructor(
    private fb: FormBuilder,
    private auth: AuthService
  ) {}

  submit() {
    this.error.set('');
    if (this.form.invalid) { 
      this.error.set('Username e password richiesti'); 
      return; 
    }
    const { username, password } = this.form.value as any;
    this.auth.login(username, password).subscribe({
      next: (response) => {
        if (response?.accessToken) {
          // Login successful, reload to show main app
          window.location.reload();
        } else {
          this.error.set('Risposta di login non valida');
        }
      },
      error: (e) => this.error.set(e?.error?.error || e?.message || 'Login fallito'),
    });
  }
}
