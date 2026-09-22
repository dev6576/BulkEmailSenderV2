import {
  Component,
  inject,
  signal
} from '@angular/core';

import {
  FormControl,
  FormGroup,
  FormArray,
  ReactiveFormsModule,
  Validators
} from '@angular/forms';

import { RecipientInput } from '../../components/recipient-input/recipient-input';
import { SendApiService } from '../../services/send-api';

import {
  Recipient,
  PreviewResponse
} from '../../models/send.models';

@Component({
  selector: 'app-send',
  imports: [
    ReactiveFormsModule,
    RecipientInput
  ],
  templateUrl: './send.html',
  styleUrl: './send.css'
})
export class Send {
  private readonly sendApi = inject(SendApiService);

  recipients: Recipient[] = [];

  selectedRecipient: Recipient | null = null;

  previewResponse =
    signal<PreviewResponse | null>(null);

  isPreviewLoading =
    signal(false);

  previewError =
    signal<string | null>(null);

  emailForm = new FormGroup({
    subject: new FormControl(
      'Hello {{Name}}',
      {
        nonNullable: true,
        validators: [Validators.required]
      }
    ),

    body: new FormControl(
      `Hello {{Name}},

This is a test email.`,
      {
        nonNullable: true,
        validators: [Validators.required]
      }
    ),

    attachments: new FormArray<FormControl<string>>([])
  });

  onRecipientsChanged(
    recipients: Recipient[]
  ): void {
    this.recipients = recipients;

    this.previewResponse.set(null);
    this.previewError.set(null);

    console.log(
      'Recipients received by Send:',
      recipients
    );
  }

  previewRecipient(
    recipient: Recipient
  ): void {
    this.selectedRecipient = recipient;

    this.previewResponse.set(null);
    this.previewError.set(null);

    this.preview();
  }

  preview(): void {
    if (!this.selectedRecipient) {
      return;
    }

    if (this.emailForm.invalid) {
      this.emailForm.markAllAsTouched();
      return;
    }

    this.isPreviewLoading.set(true);

    const formValue =
      this.emailForm.getRawValue();

    const request = {
      rowId: this.selectedRecipient.rowId,

      values:
        this.selectedRecipient.values,

      email: {
        subject: formValue.subject,
        body: formValue.body,
        attachments: []
      }
    };

    console.log(
      'Sending preview request:',
      request
    );

    this.sendApi.preview(request)
      .subscribe({
        next: response => {
          console.log(
            'PREVIEW RESPONSE:',
            response
          );

          this.previewResponse.set(
            response
          );

          this.isPreviewLoading.set(
            false
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

          this.isPreviewLoading.set(
            false
          );
        }
      });
  }
}