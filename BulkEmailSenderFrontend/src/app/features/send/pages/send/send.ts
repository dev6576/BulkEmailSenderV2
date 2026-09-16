import { Component, inject } from '@angular/core';
import { SendApiService } from '../../services/send-api';
import { RecipientInput } from '../../components/recipient-input/recipient-input';

@Component({
  selector: 'app-send',
  imports: [RecipientInput],
  templateUrl: './send.html',
  styleUrl: './send.css'
})
export class Send {
  private readonly sendApi = inject(SendApiService);

  testPreview(): void {
    const request = {
      rowId: 1,
      values: {
        Email: 'test@example.com',
        Name: 'Test User'
      },
      email: {
        subject: 'Hello {{Name}}',
        body: 'Hello {{Name}}, this is a test.',
        attachments: []
      }
    };

    this.sendApi.preview(request).subscribe({
      next: response => {
        console.log('Preview response:', response);
      },
      error: error => {
        console.error('Preview failed:', error);
      }
    });
  }
}