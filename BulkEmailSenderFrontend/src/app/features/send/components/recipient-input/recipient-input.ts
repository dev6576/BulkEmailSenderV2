import {
  ChangeDetectorRef,
  Component,
  Input,
  output
} from '@angular/core';

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
  ): string => String(params.data.rowId);


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

      ...dataColumns.map(
        column =>
          this.createDataColumn(column)
      ),

      this.createActionColumn()

    ];

  }


  private getDataColumns(): string[] {

    const columns =
      new Set<string>();

    /*
     * Get columns from existing rows.
     */
    for (const row of this.gridRows) {

      for (const key of Object.keys(row)) {

        if (key !== 'rowId') {

          columns.add(key);

        }

      }

    }

    /*
     * Also include explicitly configured columns.
     */
    for (const column of this.emailColumns) {

      columns.add(column);

    }

    return [...columns];

  }


  private createDataColumn(
    columnName: string
  ): ColDef<RecipientGridRow> {

    return {

      field: columnName,

      headerName: columnName,

      editable: true,

      cellEditor: 'agTextCellEditor',

      cellDataType: 'text'

    };

  }


  private createValidationColumn():
    ColDef<RecipientGridRow> {

    return {

      colId: 'validation',

      headerName: 'Validation',

      width: 280,

      editable: false,

      sortable: false,

      filter: false,

      pinned: 'left',

      valueGetter: params => {

        if (!params.data) {
          return '';
        }

        const rowId = params.data.rowId;

        if (!this.validatedRows.has(rowId)) {
          return '';
        }

        const errors =
          this.validationState.get(rowId);

        if (!errors || errors.length === 0) {
          return '✓';
        }

        return `⚠️ ${errors[0]}`;

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

          const hasBeenValidated =
            this.validatedRows.has(row.rowId);

          /*
           * A new or changed row has no validation
           * result yet. Keep the status cell empty.
           */
          if (!hasBeenValidated) {
            return '';
          }

          const errors =
            this.validationState.get(row.rowId);

          /*
           * Valid row.
           */
          if (!errors || errors.length === 0) {

            return `
              <div
                class="validation-cell validation-cell-success"
                title="Row is valid">

                <span class="validation-success">
                  ✓
                </span>

              </div>
            `;

          }

          /*
           * Invalid row.
           */
          return `
            <div
              class="validation-cell validation-cell-warning"
              title="${this.escapeHtml(
                errors.join('\n')
              )}">

              <span class="validation-warning">
                ⚠️
              </span>

              <span class="validation-message">
                ${this.escapeHtml(errors[0])}
              </span>

            </div>
          `;

        }

    };

  }


  private createActionColumn():
    ColDef<RecipientGridRow> {

    return {

      headerName: 'Actions',

      width: 150,

      editable: false,

      cellRenderer:
        (
          params:
            ICellRendererParams<RecipientGridRow>
        ) => {

          if (!params.data) {
            return '';
          }

          const button = document.createElement('button');

          button.type = 'button';
          button.className = 'grid-preview-button';
          button.textContent = 'Preview';

          button.addEventListener('click', event => {
            event.stopPropagation();

            if (!params.data) {
              return;
            }

            if (!this.validateSingleRow(params.data)) {
              this.refreshGrid(true);
              this.changeDetectorRef.detectChanges();
              return;
            }

            this.refreshGrid(true);
            this.changeDetectorRef.detectChanges();

            this.previewRequested.emit(
              this.gridRowToRecipient(params.data)
            );
          });

          return button;

        }

    };

  }


  /* =======================================================
     ROW CLASS
     ======================================================= */

  getRowClass = (
    params: {
      data?: RecipientGridRow;
    }
  ): string => {

    if (!params.data) {
      return '';
    }

    const errors =
      this.validationState.get(
        params.data.rowId
      );

    if (
      errors &&
      errors.length > 0
    ) {

      return 'recipient-row-invalid';

    }

    return 'recipient-row-valid';

  };


  /* =======================================================
     ROW SELECTION
     ======================================================= */

  onSelectionChanged(): void {
    this.emitSelectedRecipients();
  }

  private emitSelectedRecipients(): void {
    if (!this.gridApi) {
      this.selectedRecipientsChanged.emit([]);
      return;
    }

    const selectedRows = this.gridApi.getSelectedRows();

    this.selectedRecipientsChanged.emit(
      selectedRows.map(row => this.gridRowToRecipient(row))
    );
  }


  /* =======================================================
     CELL EDITING
     ======================================================= */

  onCellValueChanged(
    event: CellValueChangedEvent<RecipientGridRow>
  ): void {

    if (!event.data) {
      return;
    }

    /*
     * Editing a row makes its previous validation
     * result stale. Do not validate automatically.
     */
    this.validationState.delete(event.data.rowId);
    this.validatedRows.delete(event.data.rowId);

    this.refreshGrid();
    this.emitRecipients();
  }


  /* =======================================================
     ADD ROW
     ======================================================= */

  addRow(): void {

    const rowId =
      this.getNextRowId();

    const row:
      RecipientGridRow = {
        rowId
      };

    /*
     * Initialize every existing column
     * with an empty value.
     */
    for (
      const column
      of this.getDataColumns()
    ) {

      row[column] = '';

    }

    this.gridRows = [
      ...this.gridRows,
      row
    ];

    /*
     * New rows start without validation.
     */
    this.validationState.delete(
      rowId
    );

    this.validatedRows.delete(
      rowId
    );

    this.rebuildColumns();

    this.emitRecipients();

    this.refreshGrid();

    this.changeDetectorRef.detectChanges();

  }


  /* =======================================================
     ADD COLUMN
     ======================================================= */

  addColumn(): void {

    const columnName =
      this.newColumnName.trim();

    if (!columnName) {
      return;
    }

    const existingColumns =
      this.getDataColumns();

    const alreadyExists =
      existingColumns.some(
        column =>
          column.toLowerCase() ===
          columnName.toLowerCase()
      );

    if (alreadyExists) {

      alert(
        'A column with this name already exists.'
      );

      return;

    }

    /*
     * Add the new property to every
     * existing row.
     */
    for (const row of this.gridRows) {

      row[columnName] = '';

    }

    this.emailColumns = [
      ...this.emailColumns,
      columnName
    ];

    this.newColumnName = '';

    /*
     * Adding a column invalidates any previous
     * validation result because the row now has
     * another required field.
     */
    this.validationState.clear();
    this.validatedRows.clear();

    this.validationErrors = [];

    this.rebuildColumns();

    this.emitRecipients();

    this.refreshGrid();

    this.changeDetectorRef.detectChanges();

  }


  /* =======================================================
     FILE IMPORT
     ======================================================= */

  onCsvSelected(
    event: Event
  ): void {

    if (
      this.isImportingCsv ||
      this.isValidating
    ) {

      return;

    }

    const input =
      event.target as HTMLInputElement;

    const file =
      input.files?.[0];

    if (!file) {
      return;
    }

    /*
     * Validate supported file extension.
     */
    const extension =
      this.getFileExtension(
        file.name
      );

    const supportedExtensions =
      new Set([
        'csv',
        'xls',
        'xlsx'
      ]);

    if (
      !supportedExtensions.has(
        extension
      )
    ) {

      alert(
        'Please select a CSV, XLS, or XLSX file.'
      );

      input.value = '';

      return;

    }

    console.log(
      'Starting file import:',
      file.name
    );

    this.isImportingCsv = true;

    /*
     * Force Angular to immediately render
     * "Importing CSV..." before FileReader starts.
     */
    this.changeDetectorRef.detectChanges();

    const reader =
      new FileReader();


    reader.onload = () => {

      try {

        const result =
          reader.result;

        if (!result) {

          throw new Error(
            'The selected file could not be read.'
          );

        }

        /*
         * XLSX.read accepts CSV, XLS and XLSX
         * when given an ArrayBuffer.
         */
        const workbook =
          XLSX.read(
            result,
            {
              type: 'array'
            }
          );

        if (
          !workbook.SheetNames ||
          workbook.SheetNames.length === 0
        ) {

          throw new Error(
            'The file does not contain any worksheets.'
          );

        }

        /*
         * Use the first worksheet.
         */
        const worksheet =
          workbook.Sheets[
            workbook.SheetNames[0]
          ];

        if (!worksheet) {

          throw new Error(
            'The first worksheet could not be read.'
          );

        }

        /*
         * Convert worksheet to an array of
         * arrays so we can build our own rows.
         */
        const data =
          XLSX.utils.sheet_to_json<
            (string | number | boolean | null)[]
          >(
            worksheet,
            {
              header: 1,
              defval: ''
            }
          );

        this.importTabularData(data);

        console.log(
          'File import completed:',
          file.name
        );

      }
      catch (error) {

        console.error(
          'File import failed:',
          error
        );

        alert(
          error instanceof Error
            ? error.message
            : 'The file could not be imported.'
        );

      }
      finally {

        this.isImportingCsv = false;

        /*
         * Immediately update the UI.
         */
        this.changeDetectorRef.detectChanges();

      }

    };


    reader.onerror = () => {

      console.error(
        'Could not read file.'
      );

      this.isImportingCsv = false;

      alert(
        'The selected file could not be read.'
      );

      this.changeDetectorRef.detectChanges();

    };


    /*
     * ArrayBuffer works for CSV, XLS and XLSX.
     */
    reader.readAsArrayBuffer(file);

    /*
     * Clear the input so selecting the same
     * file again triggers change.
     */
    input.value = '';

  }


  /* =======================================================
     TABULAR DATA IMPORT
     * ======================================================= */

  private importTabularData(
    data:
      (string | number | boolean | null)[][]
  ): void {

    if (
      !data ||
      data.length === 0
    ) {

      throw new Error(
        'The selected file is empty.'
      );

    }

    /*
     * First row is the header.
     */
    const rawHeaders =
      data[0] ?? [];

    const headers =
      rawHeaders.map(
        (header, index) => {

          const value =
            String(
              header ?? ''
            ).trim();

          /*
           * Give unnamed columns a predictable name.
           */
          return value ||
            `Column${index + 1}`;

        }
      );


    /*
     * Remove duplicate column names.
     *
     * Example:
     *
     * Email, Name, Email
     *
     * becomes:
     *
     * Email, Name, Email_2
     */
    const uniqueHeaders =
      this.makeUniqueHeaders(
        headers
      );


    const importedRows:
      RecipientGridRow[] = [];


    /*
     * Every row after the header becomes
     * one grid row.
     */
    for (
      let index = 1;
      index < data.length;
      index++
    ) {

      const sourceRow =
        data[index] ?? [];

      /*
       * Ignore completely empty rows.
       */
      const hasValue =
        sourceRow.some(
          value =>
            String(
              value ?? ''
            ).trim() !== ''
        );

      if (!hasValue) {
        continue;
      }


      const rowId =
        index;


      const row:
        RecipientGridRow = {
          rowId
        };


      uniqueHeaders.forEach(
        (header, columnIndex) => {

          const value =
            sourceRow[columnIndex];

          row[header] =
            this.normalizeCellValue(
              value
            );

        }
      );


      importedRows.push(row);

    }


    /*
     * Replace the current grid data.
     */
    this.gridRows =
      importedRows;


    /*
     * The imported headers become our columns.
     */
    this.emailColumns =
      [...uniqueHeaders];


    /*
     * Importing new data invalidates all
     * previous validation state.
     */
    this.validationState.clear();

    this.validatedRows.clear();

    this.validationErrors = [];


    /*
     * Rebuild the grid structure.
     */
    this.rebuildColumns();


    /*
     * Notify parent.
     */
    this.emitRecipients();


    /*
     * Refresh AG Grid.
     */
    this.refreshGrid();


    /*
     * Explicitly trigger Angular rendering.
     */
    this.changeDetectorRef.detectChanges();

  }


  /* =======================================================
     UNIQUE HEADERS
     * ======================================================= */

  private makeUniqueHeaders(
    headers: string[]
  ): string[] {

    const counts =
      new Map<string, number>();

    return headers.map(
      header => {

        const existingCount =
          counts.get(header) ?? 0;

        counts.set(
          header,
          existingCount + 1
        );

        if (existingCount === 0) {
          return header;
        }

        return `${header}_${existingCount + 1}`;

      }
    );

  }


  /* =======================================================
     CELL VALUE NORMALIZATION
     * ======================================================= */

  private normalizeCellValue(
    value:
      string | number | boolean | null | undefined
  ): string {

    if (
      value === null ||
      value === undefined
    ) {

      return '';

    }

    return String(value);

  }


  /* =======================================================
     FILE EXTENSION
     * ======================================================= */

  private getFileExtension(
    fileName: string
  ): string {

    const lastDot =
      fileName.lastIndexOf('.');

    if (lastDot === -1) {
      return '';
    }

    return fileName
      .substring(lastDot + 1)
      .toLowerCase();

  }


  /* =======================================================
     VALIDATION
     * ======================================================= */

  async validateRows(): Promise<void> {

    if (
      this.isValidating ||
      this.isImportingCsv
    ) {

      return;

    }

    console.log(
      'Validate Rows clicked'
    );

    this.isValidating = true;

    /*
     * Force Angular to immediately render
     * "Validating...".
     */
    this.changeDetectorRef.detectChanges();


    try {

      /*
       * Let the browser paint the loading bar.
       */
      await new Promise<void>(
        resolve =>
          setTimeout(
            resolve,
            50
          )
      );


      console.log(
        'Starting validation'
      );


      this.revalidateAllRows();


      console.log(
        'Validation finished'
      );

    }
    catch (error) {

      console.error(
        'Validation failed:',
        error
      );

    }
    finally {

      this.isValidating = false;

      /*
       * Immediately render the completed
       * validation state.
       */
      this.changeDetectorRef.detectChanges();

      console.log(
        'Validation state reset'
      );

    }

  }


  /* =======================================================
     VALIDATE ALL ROWS
     * ======================================================= */

  private revalidateAllRows(): void {

    console.log(
      'Validating rows:',
      this.gridRows.length
    );


    /*
     * First count email occurrences.
     *
     * This allows duplicate email errors
     * to appear on EVERY affected row.
     */
    const emailCounts =
      new Map<string, number>();


    for (
      const row of this.gridRows
    ) {

      const email =
        this.getEmail(row)
          .trim()
          .toLowerCase();


      if (!email) {
        continue;
      }


      emailCounts.set(
        email,
        (emailCounts.get(email) ?? 0) + 1
      );

    }


    /*
     * Clear previous state.
     *
     * This ensures corrected rows become
     * valid on the next validation.
     */
    this.validationState.clear();
    this.validatedRows.clear();

    this.validationErrors = [];


    /*
     * Validate every row.
     */
    for (
      const row of this.gridRows
    ) {

      this.validatedRows.add(row.rowId);

      const errors =
        this.validateRow(
          row,
          emailCounts
        );

      this.validatedRows.add(row.rowId);


      if (
        errors.length === 0
      ) {

        continue;

      }


      this.validationState.set(
        row.rowId,
        errors
      );


      this.validationErrors.push({

        rowId:
          row.rowId,

        errors

      });

    }


    console.log(
      'Validation state:',
      this.validationState
    );


    console.log(
      'Validation errors:',
      this.validationErrors
    );


    /*
     * Only refresh the existing grid.
     *
     * IMPORTANT:
     * Do NOT call rebuildColumns() here.
     */
    this.refreshGrid(true);


    /*
     * Force Angular to update summary values
     * and other template state.
     */
    this.changeDetectorRef.detectChanges();

  }


  /* =======================================================
     VALIDATE ONE ROW
     * ======================================================= */

  private validateSingleRow(
    row: RecipientGridRow
  ): boolean {

    const emailCounts =
      new Map<string, number>();


    /*
     * Duplicate validation is cross-row,
     * so we must count every row even when
     * validating only one row.
     */
    for (
      const currentRow of this.gridRows
    ) {

      const email =
        this.getEmail(currentRow)
          .trim()
          .toLowerCase();


      if (!email) {
        continue;
      }


      emailCounts.set(
        email,
        (emailCounts.get(email) ?? 0) + 1
      );

    }


    const errors =
      this.validateRow(
        row,
        emailCounts
      );


    this.validatedRows.add(row.rowId);

    if (
      errors.length === 0
    ) {

      this.validationState.delete(
        row.rowId
      );

      return true;

    }


    this.validationState.set(
      row.rowId,
      errors
    );

    return false;

  }


  /* =======================================================
     VALIDATE ROW
     * ======================================================= */

  private validateRow(
    row: RecipientGridRow,
    emailCounts: Map<string, number>
  ): string[] {

    const errors: string[] = [];


    const dataColumns =
      this.getDataColumns();


    /*
     * Every data column is required.
     */
    for (
      const column of dataColumns
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


    /*
     * Email validation.
     */
    const email =
      this.getEmail(row)
        .trim();


    if (email) {

      const emailRegex =
        /^[^\s@]+@[^\s@]+\.[^\s@]+$/;


      if (
        !emailRegex.test(email)
      ) {

        errors.push(
          'Invalid email address.'
        );

      }


      /*
       * Duplicate email validation.
       */
      if (
        (
          emailCounts.get(
            email.toLowerCase()
          ) ?? 0
        ) > 1
      ) {

        errors.push(
          'Duplicate email address.'
        );

      }

    }


    return errors;

  }


  /* =======================================================
     PREVIEW
     * ======================================================= */

  previewFirstRow(): void {

    const row =
      this.gridRows[0];


    if (!row) {
      return;
    }


    /*
     * Safety validation before preview.
     */
    if (
      !this.validateSingleRow(row)
    ) {

      this.refreshGrid();

      this.changeDetectorRef.detectChanges();

      return;

    }


    this.refreshGrid();

    this.changeDetectorRef.detectChanges();


    console.log(
      'Preview row:',
      row
    );

  }


  /* =======================================================
     GET EMAIL
     * ======================================================= */

  private getEmail(
    row: RecipientGridRow
  ): string {

    const emailColumn =
      Object.keys(row).find(
        key =>
          key.toLowerCase() === 'email'
      );


    if (!emailColumn) {
      return '';
    }


    return String(
      row[emailColumn] ?? ''
    );

  }


  /* =======================================================
     NEXT ROW ID
     * ======================================================= */

  private getNextRowId(): number {

    if (
      this.gridRows.length === 0
    ) {

      return 1;

    }


    return Math.max(
      ...this.gridRows.map(
        row => row.rowId
      )
    ) + 1;

  }


  private gridRowToRecipient(
    row: RecipientGridRow
  ): Recipient {
    const values: Record<string, string> = {};

    for (const column of this.getDataColumns()) {
      values[column] = String(row[column] ?? '');
    }

    return {
      rowId: row.rowId,
      values
    };
  }


  /* =======================================================
     EMIT RECIPIENTS
     * ======================================================= */

  private emitRecipients(): void {
    const recipients = this.gridRows.map(
      row => this.gridRowToRecipient(row)
    );

    this.recipientsChanged.emit(recipients);
  }


  /* =======================================================
     GRID REFRESH
     * ======================================================= */

  private refreshGrid(replaceRowData = false): void {

    if (!this.gridApi) {
      return;
    }

    /*
     * validationState and validatedRows live outside AG Grid's
     * rowData. A normal refreshCells() can leave a function-based
     * cell renderer displaying its previous result until another
     * grid interaction occurs.
     *
     * When validation has just completed, replace rowData with a
     * new array and new row objects. This gives AG Grid an explicit
     * data change and forces the validation cells to render from the
     * current validation state immediately.
     */
    if (replaceRowData) {
      const selectedIds = new Set(
        this.gridApi
          .getSelectedRows()
          .map(row => row.rowId)
      );

      this.gridApi.setGridOption(
        'rowData',
        this.gridRows.map(row => ({ ...row }))
      );

      this.gridApi.forEachNode(node => {
        if (node.data) {
          node.setSelected(
            selectedIds.has(node.data.rowId)
          );
        }
      });
    }

    this.gridApi.refreshCells({
      columns: ['validation'],
      force: true
    });

    this.gridApi.redrawRows();

    this.emitSelectedRecipients();

  }

  /* =======================================================
     HTML ESCAPING
     * ======================================================= */

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


  /* =======================================================
     VALID / INVALID COUNTS
     * ======================================================= */

  get validRowCount(): number {

    return this.gridRows.filter(
      row =>
        this.validatedRows.has(row.rowId) &&
        !this.validationState.has(row.rowId)
    ).length;

  }


  get invalidRowCount(): number {

    return this.gridRows.filter(
      row =>
        this.validatedRows.has(row.rowId) &&
        this.validationState.has(row.rowId)
    ).length;

  }

}