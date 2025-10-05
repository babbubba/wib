import { bootstrapApplication } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { provideRouter, Routes } from '@angular/router';
import { AppComponent } from './app/app.component';
import { authInterceptor } from './app/auth.interceptor';
import { HomeComponent } from './app/home.component';
import { DashboardComponent } from './app/dashboard.component';
import { QueueComponent } from './app/queue.component';
import { LoginComponent } from './app/login.component';
import { authGuard } from './app/auth.guard';

const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'login', component: LoginComponent },
  { path: 'dashboard', component: DashboardComponent, canActivate: [authGuard] },
  { path: 'queue', component: QueueComponent, canActivate: [authGuard] },
  { path: '**', redirectTo: '' },
];

bootstrapApplication(AppComponent, {
  providers: [
    provideHttpClient(withInterceptors([authInterceptor])),
    provideNoopAnimations(),
    provideRouter(routes),
  ],
}).catch((err) => console.error(err));
