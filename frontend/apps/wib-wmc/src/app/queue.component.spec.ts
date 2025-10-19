import { TestBed } from '@angular/core/testing';
import { QueueComponent } from './queue.component';
import { HttpClientTestingModule } from '@angular/common/http/testing';

describe('QueueComponent', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [QueueComponent, HttpClientTestingModule] }).compileComponents();
  });

  it('should create', () => {
    const fix = TestBed.createComponent(QueueComponent);
    expect(fix.componentInstance).toBeTruthy();
  });
});
