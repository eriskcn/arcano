import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, convertToParamMap } from '@angular/router';
import { SharePageComponent } from './share-page.component';
import { environment } from '../environments/environment';

describe('SharePageComponent', () => {
  const base = `${environment.apiBaseUrl}/Public/tok`;
  let http: HttpTestingController;

  const create = () => {
    const fixture = TestBed.createComponent(SharePageComponent);
    fixture.detectChanges();
    return fixture;
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [SharePageComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ token: 'tok' }) } } },
      ],
    });
    http = TestBed.inject(HttpTestingController);
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('shows the file and previews it when ready', () => {
    const fixture = create();
    http.expectOne(`${base}/info`).flush({ fileName: 'a.png', contentType: 'image/png', fileSize: 2048 });
    fixture.detectChanges();
    http.expectOne(base).flush(new Blob(['x'], { type: 'image/png' }));
    fixture.detectChanges();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.textContent).toContain('a.png');
    expect(el.textContent).toContain('2 KB');
  });

  it('shows the not-found message', () => {
    const fixture = create();
    http.expectOne(`${base}/info`).flush({ code: 'Share.NotFound' }, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('This link is invalid or has expired.');
  });

  it('downloads with a plain link to the public URL', () => {
    const fixture = create();
    http.expectOne(`${base}/info`).flush({ fileName: 'notes.bin', contentType: 'application/octet-stream', fileSize: 10 });
    fixture.detectChanges();
    let href = '';
    vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
      href = this.getAttribute('href') ?? '';
    });
    fixture.componentInstance.download();
    http.expectNone(base);
    expect(href).toBe(base);
  });
});
