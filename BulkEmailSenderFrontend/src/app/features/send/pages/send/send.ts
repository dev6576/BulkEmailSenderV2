import {
  Component,
  inject,
  signal
} from '@angular/core';

import {
  FormArray,
  FormControl,
  FormGroup,
  ReactiveFormsModule,
  Validators
} from '@angular/forms';

import { RecipientInput } from '../../components/recipient-input/recipient-input';
import { SendApiService } from '../../services/send-api';

import {
  PreviewResponse,
  Recipient
} from '../../models/send.models';

type SendTab = 'data' | 'template';

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
  selectedRecipients: Recipient[] = [];
  selectedRecipient: Recipient | null = null;

  activeTab = signal<SendTab>('data');
  isPreviewDockOpen = signal(true);

  previewResponse = signal<PreviewResponse | null>(null);
  isPreviewLoading = signal(false);
  previewError = signal<string | null>(null);

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

  selectTab(tab: SendTab): void {
    this.activeTab.set(tab);
  }

  onRecipientsChanged(recipients: Recipient[]): void {
    this.recipients = recipients;

    this.previewResponse.set(null);
    this.previewError.set(null);

    console.log('Recipients received by Send:', recipients);
  }

  onSelectedRecipientsChanged(recipients: Recipient[]): void {
    this.selectedRecipients = recipients;

    console.log(
      'Selected recipients:',
      recipients
    );
  }

  previewRecipient(recipient: Recipient): void {
    this.selectedRecipient = recipient;
    this.isPreviewDockOpen.set(true);

    this.previewResponse.set(null);
    this.previewError.set(null);

    this.preview();
  }

  togglePreviewDock(): void {
    this.isPreviewDockOpen.update(isOpen => !isOpen);
  }

  preview(): void {
    if (!this.selectedRecipient) {
      return;
    }

    if (this.emailForm.invalid) {
      this.activeTab.set('template');
      this.emailForm.markAllAsTouched();
      return;
    }

    this.isPreviewLoading.set(true);

    const formValue = this.emailForm.getRawValue();

    const request = {
      rowId: this.selectedRecipient.rowId,
      values: this.selectedRecipient.values,
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

          this.previewResponse.set(response);
          this.isPreviewLoading.set(false);
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
