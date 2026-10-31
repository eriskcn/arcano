import { HttpErrorResponse } from '@angular/common/http';

export interface ErrorBody { code?: unknown; description?: unknown; detail?: unknown; title?: unknown; message?: unknown }

export function errorBody(error: unknown): ErrorBody | null {
  const body: unknown = error instanceof HttpErrorResponse ? error.error : null;
  return typeof body === 'object' && body !== null ? (body as ErrorBody) : null;
}

export function fileErrorMessage(error: unknown, fallback: string): string {
  if (error instanceof HttpErrorResponse && error.status === 413) {
    return 'File is too large.';
  }
  switch (errorBody(error)?.code) {
    case 'Storage.QuotaExceeded':
      return 'Not enough storage space.';
    case 'File.InvalidName':
      return 'Enter a valid file name.';
    default:
      return fallback;
  }
}
