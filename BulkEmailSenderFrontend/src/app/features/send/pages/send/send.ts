import {
  Component,
  inject,
  signal
} from '@angular/core';

import { RecipientInput } from '../../components/recipient-input/recipient-input';
import { SendApiService } from '../../services/send-api';

import {
  Recipient,
  PreviewResponse
} from '../../models/send.models';

@Component({
  selector: 'app-send',
  imports: [RecipientInput],
  templateUrl: './send.html',
  styleUrl: './send.css'
})
export class Send {
  debugMessage = 'THIS IS THE NEW SEND COMPONENT';

  private readonly sendApi = inject(SendApiService);

  subject = 'Hello {{Name}}';

  body = `Hello {{Name}},

This is a test email.`;

  recipients: Recipient[] = [];

  previewResponse = signal<PreviewResponse | null>(null);

  isPreviewLoading = signal(false);

  previewError = signal<string | null>(null);

  onRecipientsChanged(recipients: Recipient[]): void {
    this.recipients = recipients;

    this.previewResponse.set(null);
    this.previewError.set(null);

    console.log(
      'Recipients received by Send:',
      recipients
    );
  }

  preview(): void {
    if (this.recipients.length === 0) {
      return;
    }

    this.isPreviewLoading.set(true);
    this.previewResponse.set(null);
    this.previewError.set(null);

    const request = {
      rowId: this.recipients[0].rowId,
      values: this.recipients[0].values,
      email: {
        subject: this.subject,
        body: this.body,
        attachments: []
      }
    };

    this.sendApi.preview(request).subscribe({
      next: response => {
        console.log(
          'PREVIEW RESPONSE:',
          response
        );

        this.previewResponse.set(response);
        this.isPreviewLoading.set(false);

        console.log(
          'Loading after response:',
          this.isPreviewLoading()
        );

        console.log(
          'Response after assignment:',
          this.previewResponse()
        );
      },

      error: error => {
        console.error(
          'PREVIEW ERROR:',
          error
        );

        this.previewError.set(
          'Unable to generate email preview.'
        );

        this.isPreviewLoading.set(false);
      }
    });
  }
}