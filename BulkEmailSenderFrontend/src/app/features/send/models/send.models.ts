export interface Recipient {
  rowId: number;
  values: Record<string, string>;
}

export interface RecipientValidationError {
  rowId: number;
  errors: string[];
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

export interface EmailDefinitionRequest {
  subject: string;
  body: string;
  attachments: EmailAttachmentRequest[];
}

export interface EmailAttachmentRequest {
  fileName: string;
  contentBase64: string;
}

export interface RenderedEmailResponse {
  subject: string;
  body: string;
}

export interface ValidationError {
  code: string;
  message: string;
}