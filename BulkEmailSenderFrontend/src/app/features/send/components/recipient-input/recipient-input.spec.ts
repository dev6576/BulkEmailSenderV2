import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RecipientInput } from './recipient-input';

describe('RecipientInput', () => {
  let component: RecipientInput;
  let fixture: ComponentFixture<RecipientInput>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RecipientInput],
    }).compileComponents();

    fixture = TestBed.createComponent(RecipientInput);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
