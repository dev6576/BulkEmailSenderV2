export interface Recipient {
  rowId: number;
  values: Record<string, string>;
}

export interface RecipientValidationError {
  rowId: number;
  errors: string[];
}

export interface EmailDefinitionRequest {
  subject: string;
  body: string;
  attachments: EmailAttachmentRequest[];
}

export interface EmailAttachmentRequest {
  fileName: string;
  contentBase64: string;
}

export interface PreviewRequest {
  rowId: number;
  values: Record<string, string>;
  email: EmailDefinitionRequest;
}

export interface PreviewResponse {
  rowId: number;
  isValid: boolean;
  email: RenderedEmailResponse | null;
  errors: ValidationError[];
}

export interface RenderedEmailResponse {
  subject: string;
  htmlBody: string;
  attachments: unknown[];
}

export interface ValidationError {
  code: string;
  message: string;
}

/* =========================================================
   SEND
   ========================================================= */

export interface SendRequest {
  recipients: Recipient[];
  email: EmailDefinitionRequest;
}

export interface SendResponse {
  operationId: string;
  status: string;
  totalRows: number;
}

/* =========================================================
   SEND EVENTS / SSE
   ========================================================= */

export interface SendEvent {
  operationId: string;

  type: string;

  rowId: number | null;

  status: string;

  errors: string[];

  providerMessageId: string | null;

  sentRows: number | null;

  failedRows: number | null;

  totalRows: number | null;
}