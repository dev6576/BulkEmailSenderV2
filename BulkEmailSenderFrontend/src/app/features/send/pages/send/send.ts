import {
  Component,
  DestroyRef,
  NgZone,
  OnInit,
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
import { debounceTime, distinctUntilChanged } from 'rxjs';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import {
  SendApiService
} from '../../services/send-api';
import { AuthService } from '../../../auth/auth.service';

import {
  PreviewResponse,
  EmailAttachmentRequest,
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
export class Send implements OnInit {

  readonly maxAttachmentFileSize = 10 * 1024 * 1024;
  readonly maxTotalAttachmentSize = 25 * 1024 * 1024;
  attachmentItems = signal<Array<{ fileName: string; size: number }>>([]);
  attachmentError = signal<string | null>(null);
  sourceMode = signal<'visual' | 'html'>('visual');
  sourceHtml = '';
  showSendConfirmation = signal(false);

  private readonly sendApi =
    inject(SendApiService);
  readonly auth = inject(AuthService);

  private readonly ngZone =
    inject(NgZone);


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
  private previewRequestSequence = 0;
  private lastPreviewedBody = '';
  private readonly destroyRef = inject(DestroyRef);

  ngOnInit(): void {
    this.emailForm.controls.body.valueChanges
      .pipe(
        debounceTime(300),
        distinctUntilChanged(),
        takeUntilDestroyed(this.destroyRef)
      )
      .subscribe(body => {
        if (
          body === this.lastPreviewedBody ||
          !this.selectedRecipient ||
          !this.isPreviewDockOpen() ||
          this.showSendConfirmation() ||
          this.isSending()
        ) {
          return;
        }

        this.previewResponse.set(null);
        this.previewError.set(null);
        this.preview();
      });
  }


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
    signal(false);

  previewDockHeight =
    signal<number | null>(null);

  private previewResizeStart: {
    pointerId: number;
    startY: number;
    startHeight: number;
  } | null = null;


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

  /** Keep requests based on Quill's live DOM. getSemanticHTML() normalizes
   * empty paragraph blocks, which can drop the deliberate blank lines the
   * editor displays. The preview and send paths both use this same HTML. */
  private syncBodyFromVisualEditor(): void {
    if (this.sourceMode() !== 'visual' || !this.emailEditor) return;
    const currentHtml = this.emailEditor.root.innerHTML;
    if (currentHtml !== this.emailForm.controls.body.value) {
      this.emailForm.controls.body.setValue(currentHtml);
    }
  }

  get attachmentsArray(): FormArray<FormControl<string>> {
    return this.emailForm.controls.attachments;
  }

  async onAttachmentsSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const files = Array.from(input.files ?? []);
    input.value = '';
    this.attachmentError.set(null);
    for (const file of files) {
      if (file.size === 0) {
        this.attachmentError.set(`${file.name} is empty and was not added.`);
        continue;
      }
      if (file.size > this.maxAttachmentFileSize) {
        this.attachmentError.set(`${file.name} exceeds the 10 MB per-file limit.`);
        continue;
      }
      if (this.attachmentItems().some(item => item.fileName === file.name && item.size === file.size)) {
        continue;
      }
      if (this.totalAttachmentSize + file.size > this.maxTotalAttachmentSize) {
        this.attachmentError.set(`${file.name} would exceed the 25 MB total attachment limit.`);
        continue;
      }
      try {
        const contentBase64 = await this.readFileAsBase64(file);
        this.ngZone.run(() => {
          this.attachmentsArray.push(new FormControl(contentBase64, { nonNullable: true }));
          this.attachmentItems.update(items => [...items, { fileName: file.name, size: file.size }]);
        });
      } catch {
        this.ngZone.run(() => this.attachmentError.set(`Unable to read ${file.name}. Please try again.`));
      }
    }
  }

  removeAttachment(index: number): void {
    this.attachmentsArray.removeAt(index);
    this.attachmentItems.update(items => items.filter((_, itemIndex) => itemIndex !== index));
    this.attachmentError.set(null);
  }

  get totalAttachmentSize(): number {
    return this.attachmentItems().reduce((total, item) => total + item.size, 0);
  }

  get previewAttachmentNames(): string {
    return this.attachmentItems().map(item => item.fileName).join(', ');
  }

  formatFileSize(size: number): string {
    return size < 1024 * 1024
      ? `${Math.max(1, Math.round(size / 1024))} KB`
      : `${(size / (1024 * 1024)).toFixed(1)} MB`;
  }

  private readFileAsBase64(file: File): Promise<string> {
    return new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onload = () => resolve(String(reader.result).split(',')[1] ?? '');
      reader.onerror = () => reject(new Error(`Unable to read ${file.name}.`));
      reader.readAsDataURL(file);
    });
  }

  setSourceMode(mode: 'visual' | 'html'): void {
    if (mode === this.sourceMode()) return;
    if (mode === 'html') this.sourceHtml = this.emailForm.controls.body.value;
    else this.emailForm.controls.body.setValue(this.sourceHtml);
    this.sourceMode.set(mode);
  }

  private buildAttachments(): EmailAttachmentRequest[] {
    return this.attachmentItems().map((item, index) => ({
      fileName: item.fileName,
      contentBase64: this.attachmentsArray.at(index).value
    }));
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


  startPreviewResize(event: PointerEvent): void {
    if (event.button !== 0) {
      return;
    }

    const handle = event.currentTarget as HTMLElement;
    const dock = handle.closest('.preview-dock');
    if (!dock) {
      return;
    }

    event.preventDefault();
    handle.setPointerCapture(event.pointerId);
    this.previewResizeStart = {
      pointerId: event.pointerId,
      startY: event.clientY,
      startHeight: dock.getBoundingClientRect().height
    };
  }


  movePreviewResize(event: PointerEvent): void {
    const start = this.previewResizeStart;
    if (!start || start.pointerId !== event.pointerId) {
      return;
    }

    const height = start.startHeight + start.startY - event.clientY;
    this.setPreviewDockHeight(height);
  }


  stopPreviewResize(event: PointerEvent): void {
    if (this.previewResizeStart?.pointerId === event.pointerId) {
      this.previewResizeStart = null;
    }
  }


  resizePreviewWithKeyboard(event: KeyboardEvent): void {
    if (event.key !== 'ArrowUp' && event.key !== 'ArrowDown') {
      return;
    }

    event.preventDefault();
    const dock = (event.currentTarget as HTMLElement).closest('.preview-dock');
    const currentHeight = dock?.getBoundingClientRect().height ?? this.previewDockHeight() ?? 240;
    this.setPreviewDockHeight(currentHeight + (event.key === 'ArrowUp' ? 20 : -20));
  }


  private setPreviewDockHeight(height: number): void {
    const maxHeight = Math.max(180, Math.min(700, window.innerHeight * 0.75));
    this.previewDockHeight.set(Math.round(Math.max(120, Math.min(maxHeight, height))));
  }


  preview(): void {

    if (!this.selectedRecipient) {
      return;
    }

    this.syncBodyFromVisualEditor();
    const requestSequence = ++this.previewRequestSequence;
    this.isPreviewLoading.set(false);

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
    this.lastPreviewedBody = formValue.body;


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

        attachments: this.buildAttachments()

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

          if (requestSequence !== this.previewRequestSequence) return;

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

          if (requestSequence !== this.previewRequestSequence) return;

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

    if (!this.auth.status().connected) {
      this.sendError.set('Connect Gmail before sending email.');
      return;
    }

    if (this.isSending()) {
      return;
    }

    // Clicking Send can blur an active AG Grid editor. Commit it first so the
    // selected-recipient snapshot and validation both read the latest cell.
    if (this.recipientInput) {
      this.selectedRecipients =
        this.recipientInput.commitEditingAndGetSelectedRecipients();
    }

    this.syncBodyFromVisualEditor();


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
    await this.recipientInput.validateRows(
      this.getRequiredRecipientFields()
    );


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


    this.showSendConfirmation.set(true);

  }

  cancelSendConfirmation(): void {
    this.showSendConfirmation.set(false);
  }

  confirmSendEmails(): void {
    if (this.isSending() || this.selectedRecipients.length === 0) return;
    this.syncBodyFromVisualEditor();
    this.showSendConfirmation.set(false);
    const formValue = this.emailForm.getRawValue();
    const request = {
      recipients: this.selectedRecipients,
      email: {
        subject: formValue.subject,
        body: formValue.body,
        attachments: this.buildAttachments()
      }
    };

    const initialSendEvents = new Map<number, SendEvent>();
    for (const recipient of this.selectedRecipients) {
      initialSendEvents.set(recipient.rowId, {
        operationId: '', type: 'RowUpdate', rowId: recipient.rowId,
        status: 'Sending', errors: [], providerMessageId: null,
        sentRows: null, failedRows: null, totalRows: this.selectedRecipients.length
      });
    }

    this.isSending.set(true);
    this.sendStatus.set('Submitting...');
    this.sendOperationId.set(null);
    this.sendTotalRows.set(this.selectedRecipients.length);
    this.sendProcessedRows.set(0);
    this.sendSentRows.set(0);
    this.sendFailedRows.set(0);
    this.sendRowEvents.set(initialSendEvents);

    this.sendApi.send(request).subscribe({
      next: response => {
        this.sendOperationId.set(response.operationId);
        this.sendStatus.set(response.status);
        this.sendTotalRows.set(response.totalRows);
        this.listenToSendEvents(response.operationId);
      },
      error: error => {
        this.isSending.set(false);
        this.sendStatus.set(null);
        this.sendError.set(this.getHttpErrorMessage(error));
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


          this.ngZone.run(() => this.handleSendEvent(event));

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
          this.ngZone.run(() => {
            if (this.isSending()) {
              this.isSending.set(false);
              this.sendStatus.set(null);
              this.sendError.set(
                'The connection to the send operation was lost before completion.'
              );
            }
          });

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

  // Placeholder validity depends on the imported recipient schema, not on
  // current row selection. Previewing the first row with no selection must
  // still recognize fields such as {Name} from the other imported rows.
  const schemaRecipients = [
    ...this.recipients,
    ...this.selectedRecipients,
    ...(this.selectedRecipient ? [this.selectedRecipient] : [])
  ];

  for (const recipient of schemaRecipients) {

    for (const column of Object.keys(recipient.values)) {
      columns.add(column);
    }
  }


  return [
    ...columns
  ];

}

/** Return recipient columns whose values are needed to send this template. */
private getRequiredRecipientFields(): string[] {
  const formValue = this.emailForm.getRawValue();
  const columns = this.getTemplateColumnNames();
  const emailColumn = columns.find(column => column.toLowerCase() === 'email');
  const required = new Set<string>();
  if (emailColumn) required.add(emailColumn);

  const placeholderPattern = /{([^{}]+)}/g;
  for (const text of [formValue.subject, formValue.body]) {
    let match: RegExpExecArray | null;
    while ((match = placeholderPattern.exec(text)) !== null) {
      const field = match[1].trim();
      if (columns.includes(field)) required.add(field);
    }
  }

  return [...required];
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


    return `${this.sendProcessedRows()} / ${total} processed · ${sent} sent · ${failed} failed`;

  }

}
