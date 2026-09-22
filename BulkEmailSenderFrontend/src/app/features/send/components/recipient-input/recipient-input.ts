import {
  CellValueChangedEvent,
  ClientSideRowModelModule,
  ColDef,
  GridApi,
  GridReadyEvent,
  ModuleRegistry,
  TextEditorModule,
  Theme,
  themeQuartz
} from 'ag-grid-community';
import {
  Component,
  output
} from '@angular/core';
import { AgGridAngular } from 'ag-grid-angular';

import {
  Recipient,
  RecipientValidationError
} from '../../models/send.models';

ModuleRegistry.registerModules([
  ClientSideRowModelModule,
  TextEditorModule
]);

interface RecipientGridRow {
  rowId: number;
  [key: string]: string | number;
}

@Component({
  selector: 'app-recipient-input',
  standalone: true,
  imports: [AgGridAngular],
  templateUrl: './recipient-input.html',
  styleUrl: './recipient-input.css'
})
export class RecipientInput {

  /*
   * ========================================================
   * OUTPUTS
   * ========================================================
   */

  recipientsChanged = output<Recipient[]>();

  previewRequested = output<Recipient>();

  /*
   * ========================================================
   * GRID STATE
   * ========================================================
   */

  gridRows: RecipientGridRow[] = [];

  columnDefs: ColDef<RecipientGridRow>[] = [];

  defaultColDef: ColDef<RecipientGridRow> = {
    editable: true,
    resizable: true,
    sortable: true
  };

  theme: Theme = themeQuartz;

  private gridApi?: GridApi<RecipientGridRow>;

  /*
   * ========================================================
   * ADD COLUMN STATE
   * ========================================================
   */

  showAddColumnInput = false;

  newColumnName = '';

  /*
   * ========================================================
   * CONSTRUCTOR
   * ========================================================
   */

  constructor() {
    this.initializeDefaultGrid();
  }

  /*
   * ========================================================
   * GRID INITIALIZATION
   * ========================================================
   */

  private initializeDefaultGrid(): void {

    this.columnDefs = [
      this.createDataColumn('Email'),
      this.createDataColumn('Name'),
      this.createPreviewColumn(),
      this.createDeleteColumn()
    ];
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

  private createPreviewColumn(): ColDef<RecipientGridRow> {

    return {
      headerName: 'Action',
      width: 120,
      editable: false,
      sortable: false,
      filter: false,
      cellRenderer: (params: { data: RecipientGridRow; }) => {

        const button = document.createElement('button');

        button.innerText = 'Preview';

        button.className = 'preview-button';

        button.addEventListener('click', event => {

          event.stopPropagation();

          if (!params.data) {
            return;
          }

          const recipient =
            this.gridRowToRecipient(params.data);

          this.previewRequested.emit(recipient);
        });

        return button;
      }
    };
  }

  private createDeleteColumn(): ColDef<RecipientGridRow> {

    return {
      headerName: '',
      width: 70,
      editable: false,
      sortable: false,
      filter: false,
      cellRenderer: (params: { data: { rowId: number; }; }) => {

        const button = document.createElement('button');

        button.innerText = '×';

        button.className = 'delete-button';

        button.addEventListener('click', event => {

          event.stopPropagation();

          if (!params.data) {
            return;
          }

          this.deleteRow(params.data.rowId);
        });

        return button;
      }
    };
  }

  /*
   * ========================================================
   * GRID READY
   * ========================================================
   */

  onGridReady(
    event: GridReadyEvent<RecipientGridRow>
  ): void {

    this.gridApi = event.api;
  }

  /*
   * ========================================================
   * CELL EDITING
   * ========================================================
   */

  onCellValueChanged(
    event: CellValueChangedEvent<RecipientGridRow>
  ): void {

    this.emitRecipients();
  }

  /*
   * ========================================================
   * ADD ROW
   * ========================================================
   */

  addRow(): void {

    const nextRowId =
      this.gridRows.length === 0
        ? 1
        : Math.max(
            ...this.gridRows.map(row => row.rowId)
          ) + 1;

    const newRow: RecipientGridRow = {
      rowId: nextRowId,
      Email: '',
      Name: ''
    };

    /*
     * Add empty values for any dynamically
     * created columns.
     */
    for (const column of this.getDataColumnNames()) {

      if (!(column in newRow)) {
        newRow[column] = '';
      }
    }

    this.gridRows = [
      ...this.gridRows,
      newRow
    ];

    this.refreshGridData();

    this.emitRecipients();
  }

  /*
   * ========================================================
   * ADD COLUMN
   * ========================================================
   */

  openAddColumn(): void {

    this.showAddColumnInput = true;

    this.newColumnName = '';
  }

  confirmAddColumn(): void {

    const columnName =
      this.newColumnName.trim();

    if (!columnName) {
      return;
    }

    /*
     * Prevent duplicate column names.
     */
    const existingColumn =
      this.getDataColumnNames()
        .some(
          existing =>
            existing.toLowerCase() ===
            columnName.toLowerCase()
        );

    if (existingColumn) {
      return;
    }

    /*
     * Add the column definition.
     */
    const newColumn =
      this.createDataColumn(columnName);

    /*
     * Insert before the Action/Delete columns.
     */
    const actionColumns =
      this.columnDefs.filter(
        column =>
          column.headerName === 'Action' ||
          column.headerName === ''
      );

    const dataColumns =
      this.columnDefs.filter(
        column =>
          column.headerName !== 'Action' &&
          column.headerName !== ''
      );

    this.columnDefs = [
      ...dataColumns,
      newColumn,
      ...actionColumns
    ];

    /*
     * Add the property to every existing row.
     */
    this.gridRows =
      this.gridRows.map(row => ({
        ...row,
        [columnName]: ''
      }));

    /*
     * IMPORTANT:
     *
     * Explicitly tell AG Grid that the
     * column definitions changed.
     */
    this.gridApi?.setGridOption(
      'columnDefs',
      this.columnDefs
    );

    /*
     * Refresh row data as well because existing
     * rows need the new property.
     */
    this.gridApi?.setGridOption(
      'rowData',
      this.gridRows
    );

    this.showAddColumnInput = false;

    this.newColumnName = '';

    this.emitRecipients();
  }

  cancelAddColumn(): void {

    this.showAddColumnInput = false;

    this.newColumnName = '';
  }

  /*
   * ========================================================
   * CSV
   * ========================================================
   */

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

    const reader = new FileReader();

    reader.onload = () => {

      const text =
        String(reader.result ?? '');

      this.loadCsv(text);
    };

    reader.readAsText(file);

    input.value = '';
  }

  private loadCsv(csv: string): void {

    const lines =
      csv
        .split(/\r?\n/)
        .map(line => line.trim())
        .filter(line => line.length > 0);

    if (lines.length === 0) {
      return;
    }

    const headers =
      this.parseCsvLine(lines[0]);

    if (headers.length === 0) {
      return;
    }

    /*
     * Make sure Email exists because it is
     * required by our recipient model.
     */
    const normalizedHeaders =
      headers.map(header => header.trim());

    const rows: RecipientGridRow[] = [];

    for (let i = 1; i < lines.length; i++) {

      const values =
        this.parseCsvLine(lines[i]);

      const row: RecipientGridRow = {
        rowId: i
      };

      normalizedHeaders.forEach(
        (header, index) => {

          row[header] =
            values[index] ?? '';
        }
      );

      rows.push(row);
    }

    /*
     * Rebuild the data columns from the CSV.
     */
    this.columnDefs = [
      ...normalizedHeaders.map(
        header => this.createDataColumn(header)
      ),
      this.createPreviewColumn(),
      this.createDeleteColumn()
    ];

    this.gridRows = rows;

    this.gridApi?.setGridOption(
      'columnDefs',
      this.columnDefs
    );

    this.gridApi?.setGridOption(
      'rowData',
      this.gridRows
    );

    this.emitRecipients();
  }

  private parseCsvLine(
    line: string
  ): string[] {

    const result: string[] = [];

    let current = '';

    let insideQuotes = false;

    for (let i = 0; i < line.length; i++) {

      const character =
        line[i];

      if (character === '"') {

        if (
          insideQuotes &&
          line[i + 1] === '"'
        ) {

          current += '"';

          i++;

        } else {

          insideQuotes =
            !insideQuotes;
        }

        continue;
      }

      if (
        character === ',' &&
        !insideQuotes
      ) {

        result.push(current);

        current = '';

        continue;
      }

      current += character;
    }

    result.push(current);

    return result;
  }

  /*
   * ========================================================
   * DELETE ROW
   * ========================================================
   */

  deleteRow(rowId: number): void {

    this.gridRows =
      this.gridRows.filter(
        row => row.rowId !== rowId
      );

    this.refreshGridData();

    this.emitRecipients();
  }

  /*
   * ========================================================
   * RECIPIENT CONVERSION
   * ========================================================
   */

  private gridRowToRecipient(
    row: RecipientGridRow
  ): Recipient {

    const values: Record<string, string> = {};

    for (const column of this.getDataColumnNames()) {

      values[column] =
        String(row[column] ?? '');
    }

    return {
      rowId: row.rowId,
      values
    };
  }

  private emitRecipients(): void {

    const recipients =
      this.gridRows.map(
        row => this.gridRowToRecipient(row)
      );

    this.recipientsChanged.emit(recipients);
  }

  /*
   * ========================================================
   * VALIDATION
   * ========================================================
   */

  validate(): RecipientValidationError[] {

    const errors: RecipientValidationError[] = [];

    for (const row of this.gridRows) {

      const recipient =
        this.gridRowToRecipient(row);

      const rowErrors: string[] = [];

      if (
        !recipient.values['Email']?.trim()
      ) {

        rowErrors.push(
          'Email is required.'
        );
      }

      if (
        recipient.values['Email'] &&
        !this.isValidEmail(
          recipient.values['Email']
        )
      ) {

        rowErrors.push(
          'Email address is invalid.'
        );
      }

      if (rowErrors.length > 0) {

        errors.push({
          rowId: row.rowId,
          errors: rowErrors
        });
      }
    }

    return errors;
  }

  private isValidEmail(
    email: string
  ): boolean {

    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/
      .test(email);
  }

  /*
   * ========================================================
   * HELPERS
   * ========================================================
   */

  private getDataColumnNames(): string[] {

    return this.columnDefs
      .filter(
        column =>
          column.headerName !== 'Action' &&
          column.headerName !== ''
      )
      .map(
        column =>
          String(column.headerName)
      );
  }

  private refreshGridData(): void {

    this.gridApi?.setGridOption(
      'rowData',
      this.gridRows
    );
  }
}