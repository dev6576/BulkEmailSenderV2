import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import {
  PreviewRequest,
  PreviewResponse
} from '../models/send.models';

@Injectable({
  providedIn: 'root'
})
export class SendApiService {
  private readonly http = inject(HttpClient);

  preview(request: PreviewRequest): Observable<PreviewResponse> {
    return this.http.post<PreviewResponse>(
      '/api/preview',
      request
    );
  }
}