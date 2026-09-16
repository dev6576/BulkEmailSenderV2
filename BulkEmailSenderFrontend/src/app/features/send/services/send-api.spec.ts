import { TestBed } from '@angular/core/testing';
import { SendApi } from './send-api';

describe('SendApi', () => {
  let service: SendApi;

  beforeEach(() => {
    TestBed.configureTestingModule({});
    service = TestBed.inject(SendApi);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
