import { HttpErrorResponse } from '@angular/common/http';

export interface ErrorBody { code?: unknown; description?: unknown; detail?: unknown; title?: unknown; message?: unknown }

export function errorBody(error: unknown): ErrorBody | null {
  const body: unknown = error instanceof HttpErrorResponse ? error.error : null;
  return typeof body === 'object' && body !== null ? (body as ErrorBody) : null;
}

// Requests with responseType 'blob' deliver the JSON error as a Blob, which errorBody cannot read.
export async function readBlobError(error: unknown): Promise<unknown> {
  if (!(error instanceof HttpErrorResponse) || !(error.error instanceof Blob)) {
    return error;
  }
  try {
    return new HttpErrorResponse({ status: error.status, error: JSON.parse(await error.error.text()) });
  } catch {
    return error; // not JSON, for example a proxy's HTML error page
  }
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
    case 'File.TooLarge':
      return 'File is too large.';
    case 'File.Empty':
      return 'File is empty.';
    case 'File.NameTooLong':
      return 'File name is too long (255 characters at most).';
    case 'File.ContentTypeRequired':
    case 'File.ContentTypeTooLong':
      return 'File type is not supported.';
    case 'Upload.TooManyOpen':
      return 'Too many uploads in progress. Finish or cancel one first.';
    case 'Upload.NotFound':
    case 'Upload.BadPart':
    case 'Upload.Incomplete':
      return 'The upload was interrupted. Please try again.';
    default:
      return fallback;
  }
}
