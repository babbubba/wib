import { TestBed } from '@angular/core/testing';
import { AppComponent } from './app.component';
import { RouterTestingModule } from '@angular/router/testing';

describe('AppComponent (shell)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [AppComponent, RouterTestingModule] }).compileComponents();
  });
  it('should create', () => {
    const fix = TestBed.createComponent(AppComponent);
    expect(fix.componentInstance).toBeTruthy();
  });
});
