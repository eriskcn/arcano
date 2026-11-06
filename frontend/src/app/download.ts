import { HttpClient } from '@angular/common/http';
import { Observable, map } from 'rxjs';

// Asks the API for a 60-second ticket for this GET download URL. Errors arrive here as JSON, before any download starts.
export function ticketUrl(http: HttpClient, url: string): Observable<string> {
  return http
    .post<{ ticket: string }>(`${url}/ticket`, {})
    .pipe(map(({ ticket }) => `${url}?dt=${encodeURIComponent(ticket)}`));
}

// The browser streams the response to disk under its Content-Disposition name, so the file is never held in memory.
export function startDownload(url: string): void {
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = '';
  anchor.click();
}
