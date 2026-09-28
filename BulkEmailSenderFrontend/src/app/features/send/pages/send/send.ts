import {
  Component,
  ViewChild,
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

import {
  RecipientInput
} from '../../components/recipient-input/recipient-input';

import {
  QuillEditorComponent
} from 'ngx-quill';

import Quill from 'quill';

import QuillTableBetter from 'quill-table-better';

import QuillResize from 'quill-resize-module';

import {
  SendApiService
} from '../../services/send-api';

import {
  PreviewResponse,
  Recipient,
  SendEvent
} from '../../models/send.models';


Quill.register(
  {
    'modules/table-better': QuillTableBetter
  },
  true
);

Quill.register(
  'modules/resize',
  QuillResize
);


type SendTab =
  'data'
  | 'template';


@Component({
  selector: 'app-send',

  imports: [
    ReactiveFormsModule,
    RecipientInput,
    QuillEditorComponent
  ],

  templateUrl: './send.html',

  styleUrl: './send.css'
})
export class Send {

  private readonly sendApi =
    inject(SendApiService);


  /*
   * We use ViewChild only to trigger the existing
   * recipient validation before sending.
   *
   * RecipientInput already exposes validateRows()
   * and validationErrors.
   */
  @ViewChild(RecipientInput)
  recipientInput?: RecipientInput;

  private emailEditor?: Quill;


  /* =========================================================
     RECIPIENT STATE
     ========================================================= */

  recipients: Recipient[] = [];

  selectedRecipients: Recipient[] = [];

  selectedRecipient: Recipient | null = null;


  /* =========================================================
     TAB STATE
     ========================================================= */

  activeTab =
    signal<SendTab>('data');

  isPreviewDockOpen =
    signal(true);


  /* =========================================================
     PREVIEW STATE
     ========================================================= */

  previewResponse =
    signal<PreviewResponse | null>(null);

  isPreviewLoading =
    signal(false);

  previewError =
    signal<string | null>(null);


  /* =========================================================
     SEND STATE
     ========================================================= */

  isSending =
    signal(false);

  sendError =
    signal<string | null>(null);

  sendOperationId =
    signal<string | null>(null);

  sendStatus =
    signal<string | null>(null);

  sendTotalRows =
    signal(0);

  sendProcessedRows =
    signal(0);

  sendSentRows =
    signal(0);

  sendFailedRows =
    signal(0);


  /*
   * Row-level send events are retained here.
   *
   * This gives us a central place to store the
   * results even before we add visual status cells
   * to RecipientInput.
   */
  sendRowEvents =
    signal<Map<number, SendEvent>>(
      new Map<number, SendEvent>()
    );


  /* =========================================================
     EMAIL FORM
     ========================================================= */

  emailForm =
    new FormGroup({

      subject:
        new FormControl(
          'Hello {Name}',
          {
            nonNullable: true,
            validators: [
              Validators.required
            ]
          }
        ),

      body:
        new FormControl(
          `<p>Hello {Name},</p>
<p>This is a <strong>test email</strong>.</p>
<p><br></p>`,
          {
            nonNullable: true,
            validators: [
              Validators.required
            ]
          }
        ),

      attachments:
        new FormArray<
          FormControl<string>
        >([])

    });


  readonly editorModules = {

    toolbar: [
      [
        'bold',
        'italic',
        'underline',
        'strike'
      ],

      [
        { header: 1 },
        { header: 2 },
        { header: 3 }
      ],

      [
        { list: 'ordered' },
        { list: 'bullet' }
      ],

      [
        { indent: '-1' },
        { indent: '+1' }
      ],

      [
        { align: [] }
      ],

      [
        { color: [] },
        { background: [] }
      ],

      [
        {
          size: [
            'small',
            false,
            'large',
            'huge'
          ]
        }
      ],

      [
        'link',
        'image',
        'video'
      ],

      [
        'clean'
      ],

      [
        'table-better'
      ]
    ],

    table: false,

    'table-better': {
      language: 'en_US',
      menus: [
        'column',
        'row',
        'merge',
        'table',
        'cell',
        'wrap',
        'copy',
        'delete'
      ],
      toolbarTable: true
    },

    keyboard: {
      bindings:
        QuillTableBetter.keyboardBindings
    },

    resize: {
      modules: [
        'Resize',
        'DisplaySize',
        'Toolbar'
      ]
    }

  };


  /* =========================================================
     RICH TEXT VALIDATION
     ========================================================= */

  private isRichTextEmpty(
    html: string
  ): boolean {

    const text =
      html
        .replace(/<[^>]*>/g, '')
        .replace(/&nbsp;/gi, ' ')
        .trim();

    return text.length === 0;

  }


  private validateRichTextBody(): boolean {

    const body =
      this.emailForm.controls.body.value;

    if (!this.isRichTextEmpty(body)) {
      return true;
    }

    this.emailForm.controls.body.setErrors({
      required: true
    });

    this.emailForm.controls.body.markAsTouched();

    this.sendError.set(
      'Email body cannot be empty.'
    );

    this.activeTab.set('template');

    return false;

  }


  /* =========================================================
     EMAIL EDITOR
     ========================================================= */

  onEditorCreated(
    editor: Quill
  ): void {

    this.emailEditor = editor;

  }


  get templateColumnNames(): string[] {

    const columns =
      new Set<string>();

    for (
      const recipient
      of this.recipients
    ) {

      for (
        const column
        of Object.keys(
          recipient.values
        )
      ) {

        columns.add(column);

      }

    }

    /*
     * If the grid has not emitted its complete
     * recipient list yet, fall back to the
     * currently selected rows.
     */
    if (
      columns.size === 0
    ) {

      for (
        const recipient
        of this.selectedRecipients
      ) {

        for (
          const column
          of Object.keys(
            recipient.values
          )
        ) {

          columns.add(column);

        }

      }

    }

    return [
      ...columns
    ];

  }


  insertTemplateField(
    columnName: string
  ): void {

    if (!this.emailEditor) {
      return;
    }

    const placeholder =
      `{${columnName}}`;

    const selection =
      this.emailEditor.getSelection(
        true
      );

    const index =
      selection
        ? selection.index
        : Math.max(
            0,
            this.emailEditor.getLength() - 1
          );

    this.emailEditor.insertText(
      index,
      placeholder,
      'user'
    );

    this.emailEditor.setSelection(
      index + placeholder.length,
      0,
      'silent'
    );

    this.emailEditor.focus();

  }


  /* =========================================================
     TAB
     ========================================================= */

  selectTab(
    tab: SendTab
  ): void {

    this.activeTab.set(tab);

  }


  /* =========================================================
     RECIPIENT EVENTS
     ========================================================= */

  onRecipientsChanged(
    recipients: Recipient[]
  ): void {

    this.recipients =
      recipients;

    this.previewResponse.set(null);

    this.previewError.set(null);

    /*
     * Changing recipient data means a previous
     * send operation's row state is no longer
     * relevant.
     */
    if (!this.isSending()) {

      this.sendError.set(null);

    }

    console.log(
      'Recipients received by Send:',
      recipients
    );

  }


  onSelectedRecipientsChanged(
    recipients: Recipient[]
  ): void {

    this.selectedRecipients =
      recipients;

    console.log(
      'Selected recipients:',
      recipients
    );

  }


  /* =========================================================
     PREVIEW
     ========================================================= */

  previewRecipient(
    recipient: Recipient
  ): void {

    this.selectedRecipient =
      recipient;

    this.isPreviewDockOpen.set(true);

    this.previewResponse.set(null);

    this.previewError.set(null);

    this.preview();

  }


  togglePreviewDock(): void {

    this.isPreviewDockOpen.update(
      isOpen => !isOpen
    );

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

    if (!this.validateRichTextBody()) {
      return;
    }

    /*
     * Validate template placeholders before
     * making the preview request.
     */
    const templateErrors =
      this.validateTemplatePlaceholders();

    if (templateErrors.length > 0) {

      this.sendError.set(
        templateErrors.join(' ')
      );

      this.activeTab.set('template');

      return;

    }


    this.isPreviewLoading.set(true);


    const formValue =
      this.emailForm.getRawValue();


    const request = {

      rowId:
        this.selectedRecipient.rowId,

      values:
        this.selectedRecipient.values,

      email: {

        subject:
          formValue.subject,

        body:
          formValue.body,

        attachments: []

      }

    };


    console.log(
      'Sending preview request:',
      request
    );


    this.sendApi
      .preview(request)
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


  /* =========================================================
     SEND
     ========================================================= */

  async sendEmails(): Promise<void> {

    if (this.isSending()) {
      return;
    }


    /*
     * Clear previous send error.
     */
    this.sendError.set(null);


    /* -------------------------------------------------------
       1. Selection validation
       ------------------------------------------------------- */

    if (
      this.selectedRecipients.length === 0
    ) {

      this.sendError.set(
        'Select at least one recipient before sending.'
      );

      this.activeTab.set('data');

      return;

    }


    /* -------------------------------------------------------
       2. Email form validation
       ------------------------------------------------------- */

    if (this.emailForm.invalid) {

      this.activeTab.set('template');

      this.emailForm.markAllAsTouched();

      return;

    }

    if (!this.validateRichTextBody()) {
      return;
    }


    /* -------------------------------------------------------
       3. Recipient validation
       ------------------------------------------------------- */

    if (!this.recipientInput) {

      this.sendError.set(
        'Recipient validation is not available.'
      );

      return;

    }


    /*
     * Explicitly run the same validation that the
     * user can run with "Validate Rows".
     *
     * This ensures the selected rows are validated
     * immediately before sending.
     */
    await this.recipientInput.validateRows();


    const selectedRowIds =
      new Set(
        this.selectedRecipients.map(
          recipient => recipient.rowId
        )
      );


    const recipientValidationErrors =
      this.recipientInput.validationErrors
        .filter(error =>
          selectedRowIds.has(error.rowId)
        );


    if (
      recipientValidationErrors.length > 0
    ) {

      const firstError =
        recipientValidationErrors[0];

      this.sendError.set(
        `Recipient row ${firstError.rowId} is invalid: ${firstError.errors.join(' ')}`
      );

      this.activeTab.set('data');

      return;

    }


    /* -------------------------------------------------------
       4. Template placeholder validation
       ------------------------------------------------------- */

    const templateErrors =
      this.validateTemplatePlaceholders();


    if (templateErrors.length > 0) {

      this.sendError.set(
        templateErrors.join(' ')
      );

      this.activeTab.set('template');

      return;

    }


    /* -------------------------------------------------------
       5. Build request
       ------------------------------------------------------- */

    const formValue =
      this.emailForm.getRawValue();


    const request = {

      recipients:
        this.selectedRecipients,

      email: {

        subject:
          formValue.subject,

        body:
          formValue.body,

        attachments: []

      }

    };


    console.log(
      'SEND REQUEST:',
      request
    );


    const initialSendEvents =
  new Map<number, SendEvent>();

for (
  const recipient
  of this.selectedRecipients
) {

  initialSendEvents.set(
    recipient.rowId,
    {
      operationId: '',
      type: 'RowUpdate',
      rowId: recipient.rowId,
      status: 'Sending',
      errors: [],
      providerMessageId: null,
      sentRows: null,
      failedRows: null,
      totalRows: this.selectedRecipients.length
    }
  );

}

this.sendRowEvents.set(
  initialSendEvents
);

    /* -------------------------------------------------------
       6. Send POST request
       ------------------------------------------------------- */

    this.isSending.set(true);

    this.sendStatus.set(
      'Submitting...'
    );

    this.sendOperationId.set(null);

    this.sendTotalRows.set(
      this.selectedRecipients.length
    );

    this.sendProcessedRows.set(0);

    this.sendSentRows.set(0);

    this.sendFailedRows.set(0);

    this.sendRowEvents.set(
      new Map<number, SendEvent>()
    );


    this.sendApi
      .send(request)
      .subscribe({

        next: response => {

          console.log(
            'SEND QUEUED:',
            response
          );


          this.sendOperationId.set(
            response.operationId
          );

          this.sendStatus.set(
            response.status
          );

          this.sendTotalRows.set(
            response.totalRows
          );


          /*
           * The POST only tells us that the operation
           * was queued.
           *
           * The actual progress now comes from SSE.
           */
          this.listenToSendEvents(
            response.operationId
          );

        },


        error: error => {

          console.error(
            'SEND REQUEST ERROR:',
            error
          );

          this.isSending.set(false);

          this.sendStatus.set(
            null
          );

          this.sendError.set(
            this.getHttpErrorMessage(
              error
            )
          );

        }

      });

  }


  /* =========================================================
     SSE
     ========================================================= */

  private listenToSendEvents(
    operationId: string
  ): void {

    this.sendStatus.set(
      'Queued'
    );


    this.sendApi
      .sendEvents(operationId)
      .subscribe({

        next: event => {

          console.log(
            'SEND SSE EVENT:',
            event
          );


          this.handleSendEvent(
            event
          );

        },


        error: error => {

          console.error(
            'SEND SSE ERROR:',
            error
          );


          /*
           * If OperationCompleted has already arrived,
           * handleSendEvent() will have set isSending
           * to false.
           */
          if (this.isSending()) {

            this.isSending.set(
              false
            );

            this.sendStatus.set(
              null
            );

            this.sendError.set(
              'The connection to the send operation was lost before completion.'
            );

          }

        },

        complete: () => {

          /*
           * The backend can close the SSE stream after
           * OperationCompleted.
           */
          console.log(
            'SEND SSE COMPLETED'
          );

        }

      });

  }


 private handleSendEvent(
  event: SendEvent
): void {

  /*
   * Ignore events belonging to another operation.
   */
  if (
    this.sendOperationId() &&
    event.operationId !== this.sendOperationId()
  ) {

    console.warn(
      'Ignoring SSE event for another operation:',
      event
    );

    return;

  }


  console.log(
    'Handling send event:',
    event
  );


  /* =======================================================
     COMPLETION EVENT
     ======================================================= */

  /*
   * Current backend protocol:
   *
   * Type: RowUpdate
   * RowId: 0
   * Status: Completed
   *
   * Treat this as the operation completion event.
   */
  if (
    event.status === 'Completed'
  ) {

    if (
      event.totalRows !== null
    ) {

      this.sendTotalRows.set(
        event.totalRows
      );

    }


    if (
      event.sentRows !== null
    ) {

      this.sendSentRows.set(
        event.sentRows
      );

    }


    if (
      event.failedRows !== null
    ) {

      this.sendFailedRows.set(
        event.failedRows
      );

    }


    this.sendProcessedRows.set(
      this.sendSentRows() +
      this.sendFailedRows()
    );


    this.sendStatus.set(
      'Completed'
    );


    this.isSending.set(
      false
    );


    if (
      this.sendFailedRows() > 0
    ) {

      this.sendError.set(
        `${this.sendFailedRows()} recipient(s) failed.`
      );

    }


    console.log(
      'Send operation completed:',
      {
        sent: this.sendSentRows(),
        failed: this.sendFailedRows(),
        total: this.sendTotalRows()
      }
    );


    return;

  }


  /* =======================================================
     ROW UPDATE
     ======================================================= */

  if (
    event.rowId !== null
  ) {

    const updated =
      new Map(
        this.sendRowEvents()
      );

    updated.set(
      event.rowId,
      event
    );

    this.sendRowEvents.set(
      updated
    );

    this.recipientInput?.refreshSendStatusCells(updated);

  }


  /*
   * Use counters supplied by the backend when available.
   */
  if (
    event.totalRows !== null
  ) {

    this.sendTotalRows.set(
      event.totalRows
    );

  }


  if (
    event.sentRows !== null
  ) {

    this.sendSentRows.set(
      event.sentRows
    );

  }


  if (
    event.failedRows !== null
  ) {

    this.sendFailedRows.set(
      event.failedRows
    );

  }


  /*
   * If this is a normal row event without counters,
   * derive the counts from the row events we've received.
   */
  if (
    event.sentRows === null &&
    event.failedRows === null
  ) {

    const events =
      [
        ...this.sendRowEvents().values()
      ];


    const sent =
      events.filter(
        rowEvent =>
          rowEvent.status === 'Sent'
      ).length;


    const failed =
      events.filter(
        rowEvent =>
          rowEvent.status === 'Failed'
      ).length;


    this.sendSentRows.set(
      sent
    );

    this.sendFailedRows.set(
      failed
    );

  }


  this.sendProcessedRows.set(
    this.sendSentRows() +
    this.sendFailedRows()
  );


  this.sendStatus.set(
    'Running'
  );

}

  /* =========================================================
     TEMPLATE VALIDATION
     ========================================================= */

 /* =========================================================
   TEMPLATE VALIDATION
   ========================================================= */

private validateTemplatePlaceholders(): string[] {

  const formValue =
    this.emailForm.getRawValue();

  const columnNames =
    this.getTemplateColumnNames();

  const allowedColumns =
    new Set(columnNames);

  const errors: string[] = [];

  this.validateTemplateText(
    'Subject',
    formValue.subject,
    allowedColumns,
    errors
  );

  this.validateTemplateText(
    'Body',
    formValue.body,
    allowedColumns,
    errors
  );

  return errors;
}


private validateTemplateText(
  fieldName: string,
  text: string,
  allowedColumns: Set<string>,
  errors: string[]
): void {

  /*
   * Valid template field:
   *
   * {Name}
   * {Email}
   * {Company}
   *
   * The content between the braces must exactly
   * match one of the recipient column names.
   */

  const placeholderPattern =
    /{([^{}]*)}/g;

  let match:
    RegExpExecArray | null;

  const matchedRanges: Array<{
    start: number;
    end: number;
  }> = [];


  /*
   * Find every {Field} expression.
   */
  while (
    (
      match =
        placeholderPattern.exec(text)
    ) !== null
  ) {

    const fullPlaceholder =
      match[0];

    const field =
      match[1];

    const start =
      match.index;

    const end =
      start + fullPlaceholder.length;


    matchedRanges.push({
      start,
      end
    });


    /*
     * Empty field:
     *
     * {}
     */
    if (!field) {

      errors.push(
        `${fieldName} contains an empty template field "{}".`
      );

      continue;

    }


    /*
     * Whitespace inside the field is not allowed.
     *
     * { Name }
     */
    if (
      field !== field.trim()
    ) {

      errors.push(
        `${fieldName} contains invalid template field "${fullPlaceholder}". Use {ColumnName} without spaces.`
      );

      continue;

    }


    /*
     * Check that the field is an actual
     * recipient column.
     */
    if (
      !allowedColumns.has(field)
    ) {

      errors.push(
        `${fieldName} contains unknown field "${fullPlaceholder}".`
      );

    }

  }


  /*
   * Detect malformed braces that weren't part
   * of a valid {Field} expression.
   *
   * Examples:
   *
   * {{Name}}
   * {Name
   * Name}
   * }
   * {
   */
  const coveredCharacters =
    new Set<number>();


  for (
    const range
    of matchedRanges
  ) {

    for (
      let index = range.start;
      index < range.end;
      index++
    ) {

      coveredCharacters.add(
        index
      );

    }

  }


  for (
    let index = 0;
    index < text.length;
    index++
  ) {

    if (
      coveredCharacters.has(index)
    ) {

      continue;

    }


    const character =
      text[index];


    if (
      character === '{' ||
      character === '}'
    ) {

      errors.push(
        `${fieldName} contains invalid template braces. Use {ColumnName} for recipient fields.`
      );

      /*
       * One error is enough for malformed
       * standalone braces.
       */
      break;

    }

  }


  /*
   * Specifically detect double braces such as:
   *
   * {{Name}}
   *
   * The regex above can otherwise interpret the
   * inner {Name} as a valid expression.
   */
  if (
    /{{|}}/.test(text)
  ) {

    errors.push(
      `${fieldName} contains invalid double braces. Use {ColumnName}, not {{ColumnName}}.`
    );

  }

}


private getTemplateColumnNames(): string[] {

  const columns =
    new Set<string>();


  /*
   * The selected recipients are the rows that
   * will actually be sent.
   */
  for (
    const recipient
    of this.selectedRecipients
  ) {

    for (
      const column
      of Object.keys(
        recipient.values
      )
    ) {

      columns.add(column);

    }

  }


  return [
    ...columns
  ];

}

  /* =========================================================
     HTTP ERROR
     ========================================================= */

  private getHttpErrorMessage(
    error: unknown
  ): string {

    if (
      typeof error === 'object' &&
      error !== null
    ) {

      const httpError =
        error as {
          error?: {
            message?: string;
            title?: string;
          };
        };


      if (
        httpError.error?.message
      ) {

        return httpError.error.message;

      }


      if (
        httpError.error?.title
      ) {

        return httpError.error.title;

      }

    }


    return (
      'Unable to start the send operation.'
    );

  }


  /* =========================================================
     SEND PROGRESS
     ========================================================= */

  get sendProgressPercentage(): number {

    const total =
      this.sendTotalRows();

    if (total <= 0) {
      return 0;
    }


    const processed =
      this.sendProcessedRows();


    return Math.min(
      100,
      Math.round(
        (
          processed /
          total
        ) * 100
      )
    );

  }


  get hasSendProgress(): boolean {

    return (
      this.isSending() ||
      this.sendProcessedRows() > 0
    );

  }


  /* =========================================================
     SEND SUMMARY
     ========================================================= */

  get sendSummary(): string {

    const total =
      this.sendTotalRows();

    const sent =
      this.sendSentRows();

    const failed =
      this.sendFailedRows();


    if (total <= 0) {
      return '';
    }


    return `${sent} sent, ${failed} failed, ${total} total`;

  }

}