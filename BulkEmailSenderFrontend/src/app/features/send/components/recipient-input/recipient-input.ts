import { Component, output } from '@angular/core';
import {
  FormControl,
  ReactiveFormsModule
} from '@angular/forms';

import {
  Recipient,
  RecipientValidationError
} from '../../models/send.models';

@Component({
  selector: 'app-recipient-input',
  imports: [ReactiveFormsModule],
  templateUrl: './recipient-input.html',
  styleUrl: './recipient-input.css'
})
export class RecipientInput {
  recipientText = new FormControl('');

  recipients: Recipient[] = [];

  validationErrors: RecipientValidationError[] = [];

  recipientsChanged = output<Recipient[]>();

  parseRecipients(): void {
    this.validationErrors = [];

    const text = this.recipientText.value ?? '';

    const lines = text
      .split('\n')
      .map(line => line.trim())
      .filter(line => line.length > 0);

    this.recipients = lines.map((line, index) => {
      const [email, name] = line
        .split(',')
        .map(value => value.trim());

      return {
        rowId: index + 1,
        values: {
          Email: email ?? '',
          Name: name ?? ''
        }
      };
    });

    this.validateRecipients();

    this.recipientsChanged.emit(this.recipients);
  }

  private validateRecipients(): void {
    for (const recipient of this.recipients) {
      const email = recipient.values['Email'] ?? '';

      const errors: string[] = [];

      if (!email) {
        errors.push('Email is required.');
      } else if (!this.isValidEmail(email)) {
        errors.push('Email address is invalid.');
      }

      if (errors.length > 0) {
        this.validationErrors.push({
          rowId: recipient.rowId,
          errors
        });
      }
    }
  }

  private isValidEmail(email: string): boolean {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email);
  }

  getErrors(rowId: number): string[] {
    return this.validationErrors
      .find(error => error.rowId === rowId)
      ?.errors ?? [];
  }

  isValid(): boolean {
    return (
      this.recipients.length > 0 &&
      this.validationErrors.length === 0
    );
  }
}