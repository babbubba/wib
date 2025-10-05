import { TestBed } from '@angular/core/testing';
import { AppComponent } from './app.component';

describe('AppComponent (shell)', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [AppComponent] }).compileComponents();
  });
  it('should create', () => {
    const fix = TestBed.createComponent(AppComponent);
    expect(fix.componentInstance).toBeTruthy();
  });
});

