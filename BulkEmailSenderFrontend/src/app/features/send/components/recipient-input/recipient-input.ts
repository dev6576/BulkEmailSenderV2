import {
  ChangeDetectorRef,
  Component,
  Input,
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

  [key: string]: string | number;
}


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

  isValidating = false;

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

        const event =
          this.sendRowEvents.get(
            params.data.rowId
          );

        if (!event) {
          return '';
        }

        return event.status;

      },

      cellRenderer:
        (
          params:
            ICellRendererParams<RecipientGridRow>
        ) => {

          const row =
            params.data;

          if (!row) {
            return '';
          }

          const event =
            this.sendRowEvents.get(
              row.rowId
            );

          if (!event) {
            return '';
          }

          const status =
            event.status;


          if (status === 'Sent') {

            return `
              <div
                class="send-status-cell send-status-success">

                <span class="send-status-icon">
                  ✓
                </span>

                <span>
                  Sent
                </span>

              </div>
            `;

          }


          if (status === 'Failed') {

            const errorText =
              event.errors.length > 0
                ? event.errors.join('\n')
                : 'The email could not be sent.';

            return `
              <div
                class="send-status-cell send-status-failed"
                title="${this.escapeHtml(errorText)}">

                <span class="send-status-icon">
                  ⚠
                </span>

                <span>
                  Failed
                </span>

              </div>
            `;

          }


          if (status === 'Sending') {

            return `
              <div
                class="send-status-cell send-status-sending">

                <span>
                  Sending...
                </span>

              </div>
            `;

          }


          return `
            <div
              class="send-status-cell">

              ${this.escapeHtml(status)}

            </div>
          `;

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
     * IMPORTANT:
     *
     * The parent updates its signal and then immediately
     * calls this method.
     *
     * Angular may not have propagated the new @Input()
     * value into this component yet.
     *
     * Therefore we explicitly assign the Map here.
     */
    this.sendRowEvents =
      sendRowEvents;


    console.log(
      'refreshSendStatusCells map:',
      [...sendRowEvents.entries()]
    );


    if (!this.gridApi) {
      return;
    }


    /*
     * Force AG Grid to re-evaluate the external
     * send-status state.
     */
    this.gridApi.refreshCells({

      columns: [
        'sendStatus'
      ],

      force: true

    });


    /*
     * Force the row renderers to execute again.
     */
    this.gridApi.redrawRows();


    /*
     * Ensure Angular updates the component view.
     */
    this.changeDetectorRef.detectChanges();

  }


  /* =======================================================
     COLUMN HELPERS
     ======================================================= */

  private getDataColumns(): string[] {

    const columns =
      new Set<string>();


    for (
      const row
      of this.gridRows
    ) {

      for (
        const key
        of Object.keys(row)
      ) {

        if (
          key !== 'rowId'
        ) {

          columns.add(key);

        }

      }

    }


    return [
      ...columns
    ];

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

  async validateRows(): Promise<void> {

    if (this.isValidating) {
      return;
    }


    this.isValidating =
      true;


    /*
     * Existing row validation logic should
     * populate validationState and validationErrors.
     *
     * This method deliberately remains explicit;
     * validation is not performed while typing.
     */
    try {

      this.validationState.clear();

      this.validatedRows.clear();

      this.validationErrors = [];


      for (
        const row
        of this.gridRows
      ) {

        const errors:
          string[] = [];


        for (
          const column
          of this.getDataColumns()
        ) {

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


      this.refreshGrid(
        true
      );

    }
    finally {

      this.isValidating =
        false;

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


        const errors =
          this.validationState.get(
            params.data.rowId
          );


        if (
          !errors ||
          errors.length === 0
        ) {

          return '';

        }


        return `${errors.length} error(s)`;

      },

      cellRenderer:
        (
          params:
            ICellRendererParams<RecipientGridRow>
        ) => {

          if (!params.data) {
            return '';
          }


          const errors =
            this.validationState.get(
              params.data.rowId
            );


          if (
            !errors ||
            errors.length === 0
          ) {

            return '';

          }


          return `
            <div
              class="validation-error-cell"
              title="${this.escapeHtml(errors.join('\n'))}">

              <span>
                ⚠
              </span>

              <span>
                ${errors.length} error(s)
              </span>

            </div>
          `;

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