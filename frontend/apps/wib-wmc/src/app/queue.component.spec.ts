import { TestBed } from '@angular/core/testing';
import { QueueComponent } from './queue.component';

describe('QueueComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [QueueComponent] }).compileComponents();
  });

  it('should create', () => {
    const fix = TestBed.createComponent(QueueComponent);
    expect(fix.componentInstance).toBeTruthy();
  });
});

