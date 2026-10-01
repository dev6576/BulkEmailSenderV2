import {
  ChangeDetectorRef,
  Component,
  Input,
  signal,
  output
} from '@angular/core';

import {
  SendEvent
} from '../../models/send.models';

import { FormsModule } from '@angular/forms';

import {
  CellValueChangedEvent,
  ICellRendererParams,
  ClientSideRowModelModule,
  ColDef,
  GridApi,
  GridReadyEvent,
  ModuleRegistry,
  RowSelectionModule,
  RowSelectionOptions,
  TextEditorModule,
  themeQuartz
} from 'ag-grid-community';

import { AgGridAngular } from 'ag-grid-angular';

import * as XLSX from 'xlsx';

import {
  Recipient,
  RecipientValidationError
} from '../../models/send.models';


/* =========================================================
   AG GRID MODULES
   ========================================================= */

ModuleRegistry.registerModules([
  ClientSideRowModelModule,
  RowSelectionModule,
  TextEditorModule
]);


/* =========================================================
   GRID ROW
   ========================================================= */

interface RecipientGridRow {
  rowId: number;
  sendStatus?: string;
  sendStatusError?: string;
  validationStatus?: string;
  validationErrorsText?: string;

  [key: string]: string | number | undefined;
}

// These are AG Grid display fields, not imported recipient columns. Keeping
// them out of the recipient schema prevents previews/sends from receiving
// grid bookkeeping as merge-field data.
const GRID_ONLY_FIELDS = new Set([
  'sendStatus',
  'sendStatusError',
  'validationStatus',
  'validationErrorsText'
]);


/* =========================================================
   COMPONENT
   ========================================================= */

@Component({
  selector: 'app-recipient-input',

  standalone: true,

  imports: [
    FormsModule,
    AgGridAngular
  ],

  templateUrl: './recipient-input.html',

  styleUrl: './recipient-input.css'
})
export class RecipientInput {

  /* =======================================================
     INPUT / OUTPUT
     ======================================================= */

  @Input()
  emailColumns: string[] = [];

  @Input()
  sendRowEvents: Map<number, SendEvent> =
    new Map<number, SendEvent>();

  recipientsChanged =
    output<Recipient[]>();

  previewRequested =
    output<Recipient>();

  selectedRecipientsChanged =
    output<Recipient[]>();


  /* =======================================================
     GRID STATE
     ======================================================= */

  gridRows: RecipientGridRow[] = [];

  columnDefs:
    ColDef<RecipientGridRow>[] = [];

  defaultColDef:
    ColDef<RecipientGridRow> = {

      resizable: true,

      sortable: false,

      filter: false

    };

  readonly theme = themeQuartz;

  private gridApi?: GridApi<RecipientGridRow>;

  readonly rowSelection: RowSelectionOptions = {
    mode: 'multiRow',
    checkboxes: true,
    headerCheckbox: true,
    selectAll: 'all'
  };

  readonly selectionColumnDef = {
    width: 44,
    minWidth: 44,
    maxWidth: 44,
    pinned: 'left' as const,
    resizable: false,
    sortable: false,
    suppressHeaderMenuButton: true
  };


  /* =======================================================
     UI STATE
     ======================================================= */

  newColumnName = '';

  showAddColumnInput = false;

  // Keep this UI state reactive even when an async validation yield resumes
  // outside Angular's normal event turn.
  isValidating = signal(false);

  isImportingCsv = false;


  /* =======================================================
     VALIDATION STATE
     ======================================================= */

  private validationState =
    new Map<number, string[]>();

  private validatedRows =
    new Set<number>();

  validationErrors:
    RecipientValidationError[] = [];

  validationSummary: string | null = null;


  /* =======================================================
     CONSTRUCTOR
     ======================================================= */

  constructor(
    private readonly changeDetectorRef: ChangeDetectorRef
  ) {}


  /* =======================================================
     GRID INITIALIZATION
     ======================================================= */

  readonly getRowId = (
    params: { data: RecipientGridRow }
  ): string =>
    String(params.data.rowId);


  onGridReady(
    event: GridReadyEvent<RecipientGridRow>
  ): void {

    this.gridApi = event.api;

    this.rebuildColumns();

    this.emitSelectedRecipients();

  }

  /** Commit an in-progress cell edit before send validation reads the rows. */
  commitEditingAndGetSelectedRecipients(): Recipient[] {
    this.gridApi?.stopEditing();
    const recipients = (this.gridApi?.getSelectedRows() ?? [])
      .map(row => this.toRecipient(row));
    this.selectedRecipientsChanged.emit(recipients);
    return recipients;
  }


  /* =======================================================
     COLUMN MANAGEMENT
     ======================================================= */

  private rebuildColumns(): void {

    const dataColumns =
      this.getDataColumns();

    this.columnDefs = [

      this.createValidationColumn(),

      this.createSendStatusColumn(),

      ...dataColumns.map(
        column =>
          this.createDataColumn(column)
      ),

      this.createActionColumn()

    ];

  }


  /* =======================================================
     SEND STATUS COLUMN
     ======================================================= */

  private createSendStatusColumn():
    ColDef<RecipientGridRow> {

    return {

      colId: 'sendStatus',

      headerName: 'Send Status',

      width: 150,

      editable: false,

      sortable: false,

      filter: false,

      pinned: 'left',

      valueGetter: params => {

        if (!params.data) {
          return '';
        }

        return params.data.sendStatus ?? '';

      },

      cellRenderer:
        (
          params:
            ICellRendererParams<RecipientGridRow>
        ) => {

          const renderStatus = (
            text: string,
            className: string,
            icon?: string,
            title?: string
          ): HTMLElement => {
            const container = document.createElement('div');
            container.className = `send-status-cell ${className}`.trim();
            if (title) container.title = title;
            if (icon) {
              const iconElement = document.createElement('span');
              iconElement.className = 'send-status-icon';
              iconElement.textContent = icon;
              container.appendChild(iconElement);
            }
            const label = document.createElement('span');
            label.textContent = text;
            container.appendChild(label);
            return container;
          };

          const row =
            params.data;

          if (!row) {
            return null;
          }

          if (!row.sendStatus) {
            return null;
          }

          const status = row.sendStatus;


          if (status === 'Sent') {
            return renderStatus('Sent', 'send-status-success', '✓');

          }


          if (status === 'Failed') {

            const errorText = row.sendStatusError || 'The email could not be sent.';

            return renderStatus('Failed', 'send-status-failed', '⚠', errorText);

          }


          if (status === 'Sending') {

            return renderStatus('Sending...', 'send-status-sending');

          }


          return renderStatus(status, '');

        }

    };

  }


  /* =======================================================
     SEND STATUS REFRESH
     ======================================================= */

 refreshSendStatusCells(
  sendRowEvents: Map<number, SendEvent>
): void {

  /*
   * Store the latest send-event map locally.
   */
  this.sendRowEvents =
    sendRowEvents;


  console.log(
    'Refreshing send status cells:',
    [...sendRowEvents.entries()]
  );


  if (!this.gridApi) {
    console.warn(
      'Cannot refresh send status: gridApi is not available.'
    );

    return;
  }


  // Keep status in AG Grid's row data so the grid's row update lifecycle
  // refreshes the visible cell without requiring a manual edit.
  const changedRows = this.gridRows
    .filter(row => sendRowEvents.has(row.rowId))
    .map(row => {
      const event = sendRowEvents.get(row.rowId)!;
      return { ...row, sendStatus: event.status, sendStatusError: event.errors.join('\n') };
    });

  if (changedRows.length > 0) {
    const changedById = new Map(changedRows.map(row => [row.rowId, row]));
    this.gridRows = this.gridRows.map(row => changedById.get(row.rowId) ?? row);
    this.gridApi.applyTransaction({ update: changedRows });
  }


  /*
   * Make Angular process the updated component state.
   */
  this.changeDetectorRef.markForCheck();
  this.changeDetectorRef.detectChanges();

}

  /* =======================================================
     COLUMN HELPERS
     ======================================================= */

  private getDataColumns(): string[] {
    // Imported and manually-created rows share the same schema. Reading the
    // first row avoids rescanning every recipient just to rediscover columns.
    const firstRow = this.gridRows[0];
    if (firstRow) {
      return Object.keys(firstRow)
        .filter(key => key !== 'rowId' && !GRID_ONLY_FIELDS.has(key));
    }

    // Preserve known columns after the final row is removed.
    return this.columnDefs
      .map(column => column.field)
      .filter((field): field is string => typeof field === 'string' && field !== 'rowId');

  }


  private createDataColumn(
    column: string
  ): ColDef<RecipientGridRow> {

    return {

      field: column,

      headerName: column,

      editable: true,

      flex: 1,

      minWidth: 150,

      cellEditor: 'agTextCellEditor'

    };

  }


  /* =======================================================
     ADD COLUMN UI
     ======================================================= */

  openAddColumn(): void {

    this.showAddColumnInput =
      true;

    this.newColumnName =
      '';

    this.changeDetectorRef.detectChanges();

  }


  cancelAddColumn(): void {

    this.showAddColumnInput =
      false;

    this.newColumnName =
      '';

    this.changeDetectorRef.detectChanges();

  }


  addColumn(): void {

    const columnName =
      this.newColumnName.trim();


    /*
     * Keep the input open when validation fails.
     */
    if (!columnName) {

      return;

    }


    const existingColumns =
      this.getDataColumns();


    if (
      existingColumns.some(
        column =>
          column.toLowerCase() ===
          columnName.toLowerCase()
      )
    ) {

      alert(
        `Column "${columnName}" already exists.`
      );

      return;

    }

    this.validationSummary = null;


    /*
     * Add the new column to every existing row.
     */
    for (
      const row
      of this.gridRows
    ) {

      row[columnName] = '';

    }


    /*
     * Rebuild AG Grid columns so the new column
     * becomes visible.
     */
    this.rebuildColumns();


    this.refreshGrid(
      true
    );


    /*
     * Clear and hide the add-column UI after
     * a successful add.
     */
    this.newColumnName =
      '';

    this.showAddColumnInput =
      false;


    this.emitRecipients();

  }


  /* =======================================================
     ROW MANAGEMENT
     ======================================================= */

  addRow(): void {

    this.validationSummary = null;

    const rowId =
      this.getNextRowId();

    const row:
      RecipientGridRow = {
        rowId
      };


    for (
      const column
      of this.getDataColumns()
    ) {

      row[column] =
        '';

    }


    this.gridRows = [
      ...this.gridRows,
      row
    ];


    this.refreshGrid(
      true
    );


    this.emitRecipients();

  }


  private getNextRowId(): number {

    if (
      this.gridRows.length === 0
    ) {

      return 1;

    }


    return Math.max(
      ...this.gridRows.map(
        row =>
          row.rowId
      )
    ) + 1;

  }


  /* =======================================================
     REMOVE ROW
     ======================================================= */

  private removeRow(
    rowId: number
  ): void {

    this.validationSummary = null;

    this.gridRows =
      this.gridRows.filter(
        row =>
          row.rowId !== rowId
      );


    this.validationState.delete(
      rowId
    );

    this.validatedRows.delete(
      rowId
    );


    this.validationErrors =
      this.validationErrors.filter(
        error =>
          error.rowId !== rowId
      );


    this.refreshGrid(
      true
    );


    this.emitRecipients();

  }


  /* =======================================================
     ACTION COLUMN
     ======================================================= */

  private createActionColumn():
    ColDef<RecipientGridRow> {

    return {

      colId: 'actions',

      headerName: 'Actions',

      width: 100,

      editable: false,

      sortable: false,

      filter: false,

      pinned: 'right',

      cellRenderer:
        (
          params:
            ICellRendererParams<RecipientGridRow>
        ) => {

          const button =
            document.createElement(
              'button'
            );

          button.type =
            'button';

          button.textContent =
            'Delete';

          button.className =
            'delete-row-button';


          button.addEventListener(
            'click',
            () => {

              if (!params.data) {
                return;
              }

              this.removeRow(
                params.data.rowId
              );

            }
          );


          return button;

        }

    };

  }


  /* =======================================================
     GRID EVENTS
     ======================================================= */

  onCellValueChanged(
    event: CellValueChangedEvent<RecipientGridRow>
  ): void {

    if (!event.data) {
      return;
    }

    this.validationSummary = null;


    /*
     * Editing a cell invalidates the previous
     * validation result for that row.
     */
    this.validationState.delete(
      event.data.rowId
    );

    this.validatedRows.delete(
      event.data.rowId
    );


    this.validationErrors =
      this.validationErrors.filter(
        error =>
          error.rowId !==
          event.data!.rowId
      );


    this.refreshGrid(
      true
    );


    this.emitRecipients();

  }


  onSelectionChanged(): void {

    this.emitSelectedRecipients();

  }


  /* =======================================================
     RECIPIENT OUTPUT
     ======================================================= */

  private emitRecipients(): void {

    const recipients =
      this.gridRows.map(
        row =>
          this.toRecipient(row)
      );


    this.recipientsChanged.emit(
      recipients
    );

  }


  private emitSelectedRecipients(): void {

    if (!this.gridApi) {
      return;
    }


    const selectedRows =
      this.gridApi.getSelectedRows();


    const recipients =
      selectedRows.map(
        row =>
          this.toRecipient(row)
      );


    this.selectedRecipientsChanged.emit(
      recipients
    );

  }


  private toRecipient(
    row: RecipientGridRow
  ): Recipient {

    const values:
      Record<string, string> = {};


    for (
      const column
      of this.getDataColumns()
    ) {

      values[column] =
        String(
          row[column] ?? ''
        );

    }


    return {

      rowId:
        row.rowId,

      values

    };

  }


  /* =======================================================
     PREVIEW
     ======================================================= */

  previewFirstRow(): void {

    const selectedRows =
      this.gridApi?.getSelectedRows() ?? [];


    const row =
      selectedRows.length > 0
        ? selectedRows[0]
        : this.gridRows[0];


    if (!row) {

      alert(
        'There are no recipient rows to preview.'
      );

      return;

    }


    this.previewRequested.emit(
      this.toRecipient(row)
    );

  }


  /* =======================================================
     VALIDATION
     ======================================================= */

  async validateRows(requiredColumns?: string[]): Promise<void> {

    if (this.isValidating()) {
      return;
    }


    this.isValidating.set(true);
    this.changeDetectorRef.detectChanges();


    // Keep this as an explicit action so large sheets are not revalidated
    // while the user is still editing cells.
    try {

      // Let Angular paint the loading label before doing any CPU work.
      await new Promise<void>(resolve => setTimeout(resolve, 0));

      this.validationState.clear();

      this.validatedRows.clear();

      this.validationErrors = [];
      this.validationSummary = null;

      // Column names are the same for every row. Compute them once; looking
      // them up by scanning all rows inside this loop made validation quadratic.
      const dataColumns = this.getDataColumns();
      const emailColumn = dataColumns.find(column => column.toLowerCase() === 'email');
      // Send validation only requires values that the message uses plus Email;
      // the standalone Validate Rows action still checks every data column.
      const columnsToRequire = requiredColumns
        ? new Set(requiredColumns.map(column => column.toLowerCase()))
        : new Set(dataColumns.map(column => column.toLowerCase()));

      for (let rowIndex = 0; rowIndex < this.gridRows.length; rowIndex++) {
        // Yield occasionally so a large imported sheet does not freeze the UI.
        if (rowIndex > 0 && rowIndex % 250 === 0) {
          await new Promise<void>(resolve => setTimeout(resolve, 0));
        }

        const row = this.gridRows[rowIndex];

        const errors:
          string[] = [];


        for (
          const column
          of dataColumns
        ) {

          if (!columnsToRequire.has(column.toLowerCase())) {
            continue;
          }

          const value =
            String(
              row[column] ?? ''
            ).trim();


          if (!value) {

            errors.push(
              `${column} is required.`
            );

          }

        }

        if (!emailColumn) {
          errors.push('Email column is required.');
        } else {
          const email = String(row[emailColumn] ?? '').trim();
          if (email && !/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email)) {
            errors.push('Email address is not valid.');
          }
        }


        this.validatedRows.add(
          row.rowId
        );


        this.validationState.set(
          row.rowId,
          errors
        );


        if (
          errors.length > 0
        ) {

          this.validationErrors.push({

            rowId:
              row.rowId,

            errors

          });

        }

      }

      const invalidRows = this.validationErrors.length;
      this.validationSummary = invalidRows === 0
        ? `All ${this.gridRows.length} recipient row(s) passed validation.`
        : `Validation complete: ${invalidRows} row(s) need attention. See the Validation column.`;


      // Store results in row data and submit immutable row updates through
      // AG Grid so its own row lifecycle refreshes the visible cells.
      const validatedRows = this.gridRows.map(row => {
        const errors = this.validationState.get(row.rowId);
        if (!errors) return row;
        return {
          ...row,
          validationStatus: errors.length === 0 ? 'Valid' : `${errors.length} error(s)`,
          validationErrorsText: errors.join('\n')
        };
      });
      this.gridRows = validatedRows;
      if (this.gridApi && validatedRows.length > 0) {
        this.gridApi.applyTransaction({ update: validatedRows });
      }
      this.changeDetectorRef.detectChanges();

    }
    catch (error) {
      console.error('Recipient row validation failed:', error);
      this.validationSummary = 'Validation could not complete. Please try again.';
    }
    finally {

      this.isValidating.set(false);
      this.changeDetectorRef.detectChanges();

    }

  }


  private createValidationColumn():
    ColDef<RecipientGridRow> {

    return {

      colId: 'validation',

      headerName: 'Validation',

      width: 120,

      editable: false,

      sortable: false,

      filter: false,

      pinned: 'left',

      valueGetter: params => {

        if (!params.data) {
          return '';
        }


        return params.data.validationStatus ?? '';

      },

      cellRenderer:
        (
          params:
            ICellRendererParams<RecipientGridRow>
        ) => {

          if (!params.data) {
            return '';
          }


          if (params.data.validationStatus === undefined) {
            return '';
          }

          // Returning an HTMLElement makes this a real AG Grid renderer;
          // returning an HTML-looking string displays markup as plain text.
          const container = document.createElement('div');
          const icon = document.createElement('span');
          const message = document.createElement('span');
          const isValid = params.data.validationStatus === 'Valid';

          container.className = `validation-cell ${isValid ? 'validation-cell-success' : 'validation-cell-warning'}`;
          container.title = isValid ? 'This row passed validation.' : (params.data.validationErrorsText ?? 'Validation failed.');
          icon.className = isValid ? 'validation-success' : 'validation-warning';
          icon.textContent = isValid ? '✓' : '⚠';
          message.className = 'validation-message';
          message.textContent = params.data.validationStatus!;
          container.append(icon, message);

          return container;

        }

    };

  }


  /* =======================================================
     GRID REFRESH
     ======================================================= */

  private refreshGrid(
    replaceRowData = false
  ): void {

    if (!this.gridApi) {
      return;
    }


    if (
      replaceRowData
    ) {

      /*
       * Create new row objects so AG Grid receives
       * a new rowData reference while preserving
       * stable row identity through getRowId().
       */
      this.gridRows =
        this.gridRows.map(
          row => ({
            ...row
          })
        );


      this.gridApi.setGridOption(
        'rowData',
        this.gridRows
      );

    }


    this.gridApi.refreshCells({

      columns: [
        'validation'
      ],

      force: true

    });


    this.gridApi.redrawRows();


    this.emitSelectedRecipients();


    this.changeDetectorRef.detectChanges();

  }


  /* =======================================================
     FILE IMPORT
     ======================================================= */

  onCsvSelected(
    event: Event
  ): void {

    const input =
      event.target as HTMLInputElement;


    const file =
      input.files?.[0];


    if (!file) {
      return;
    }


    this.isImportingCsv =
      true;


    const reader =
      new FileReader();


    reader.onload =
      () => {

        try {

          const data =
            reader.result;


          if (!data) {
            return;
          }


          const workbook =
            XLSX.read(
              data,
              {
                type: 'array'
              }
            );


          const firstSheet =
            workbook.Sheets[
              workbook.SheetNames[0]
            ];


          if (!firstSheet) {

            alert(
              'The imported file does not contain a worksheet.'
            );

            return;

          }


          const rows =
            XLSX.utils.sheet_to_json<
              Record<string, unknown>
            >(
              firstSheet,
              {
                defval: ''
              }
            );


          if (
            rows.length === 0
          ) {

            alert(
              'The imported file does not contain any rows.'
            );

            return;

          }


          const headers =
            this.getImportedHeaders(
              rows
            );


          if (
            headers.length === 0
          ) {

            alert(
              'The imported file does not contain any columns.'
            );

            return;

          }


          this.gridRows =
            rows.map(
              (sourceRow, index) => {

                const row:
                  RecipientGridRow = {

                  rowId:
                    index + 1

                };


                for (
                  const header
                  of headers
                ) {

                  row[header] =
                    String(
                      sourceRow[header] ?? ''
                    );

                }


                return row;

              }
            );


          this.validationState.clear();

          this.validatedRows.clear();

          this.validationErrors = [];
          this.validationSummary = null;


          this.rebuildColumns();


          this.refreshGrid(
            true
          );


          this.emitRecipients();

        }
        catch (error) {

          console.error(
            'IMPORT ERROR:',
            error
          );

          alert(
            'Unable to import the selected file.'
          );

        }
        finally {

          this.isImportingCsv =
            false;

          input.value =
            '';

        }

      };


    reader.onerror =
      () => {

        this.isImportingCsv =
          false;

        input.value =
          '';

        alert(
          'Unable to read the selected file.'
        );

      };


    reader.readAsArrayBuffer(
      file
    );

  }


  private getImportedHeaders(
    rows: Record<string, unknown>[]
  ): string[] {

    const headers =
      new Set<string>();


    for (
      const row
      of rows
    ) {

      for (
        const key
        of Object.keys(row)
      ) {

        if (
          key.trim()
        ) {

          headers.add(
            key.trim()
          );

        }

      }

    }


    return [
      ...headers
    ];

  }


  /* =======================================================
     HTML SAFETY
     ======================================================= */

  private escapeHtml(
    value: string
  ): string {

    return value
      .replace(
        /&/g,
        '&amp;'
      )
      .replace(
        /</g,
        '&lt;'
      )
      .replace(
        />/g,
        '&gt;'
      )
      .replace(
        /"/g,
        '&quot;'
      )
      .replace(
        /'/g,
        '&#039;'
      );

  }

}
