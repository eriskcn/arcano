import { HttpErrorResponse } from '@angular/common/http';
import { fileErrorMessage } from './http-errors';

const failure = (status: number, code?: string) => new HttpErrorResponse({ status, error: code ? { code } : null });

describe('fileErrorMessage', () => {
  it('maps known upload and file codes to plain messages', () => {
    expect(fileErrorMessage(failure(400, 'Upload.TooManyOpen'), 'x')).toBe('Too many uploads in progress. Finish or cancel one first.');
    expect(fileErrorMessage(failure(404, 'Upload.NotFound'), 'x')).toContain('interrupted');
    expect(fileErrorMessage(failure(400, 'Upload.BadPart'), 'x')).toContain('interrupted');
    expect(fileErrorMessage(failure(400, 'Upload.Incomplete'), 'x')).toContain('interrupted');
    expect(fileErrorMessage(failure(400, 'File.Empty'), 'x')).toBe('File is empty.');
    expect(fileErrorMessage(failure(400, 'File.TooLarge'), 'x')).toBe('File is too large.');
    expect(fileErrorMessage(failure(400, 'File.NameTooLong'), 'x')).toContain('too long');
    expect(fileErrorMessage(failure(400, 'File.ContentTypeRequired'), 'x')).toBe('File type is not supported.');
    expect(fileErrorMessage(failure(400, 'Storage.QuotaExceeded'), 'x')).toBe('Not enough storage space.');
  });

  it('maps 413 and uses the fallback for unknown codes', () => {
    expect(fileErrorMessage(failure(413), 'x')).toBe('File is too large.');
    expect(fileErrorMessage(failure(500, 'Something.Else'), 'fallback')).toBe('fallback');
    expect(fileErrorMessage(failure(500), 'fallback')).toBe('fallback');
  });
});
