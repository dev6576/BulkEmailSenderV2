import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Send } from './send';

describe('Send', () => {
  let component: Send;
  let fixture: ComponentFixture<Send>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [Send],
    }).compileComponents();

    fixture = TestBed.createComponent(Send);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
