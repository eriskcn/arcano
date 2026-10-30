import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { FilePreviewComponent } from './file-preview.component';
import { environment } from '../../environments/environment';

describe('FilePreviewComponent', () => {
  const imageId = '3fa85f64-5717-4562-b3fc-2c963f66afa6';
  const otherId = '7b1c2d3e-0000-4000-8000-000000000001';
  let fixture: ComponentFixture<FilePreviewComponent>;
  let http: HttpTestingController;

  const fileUrl = (id: string) => `${environment.apiBaseUrl}/Files/${id}`;

  const create = (inputs: { fileId: string; contentType: string; fileSize: number; fileName: string }) => {
    fixture = TestBed.createComponent(FilePreviewComponent);
    fixture.componentRef.setInput('fileId', inputs.fileId);
    fixture.componentRef.setInput('contentType', inputs.contentType);
    fixture.componentRef.setInput('fileSize', inputs.fileSize);
    fixture.componentRef.setInput('fileName', inputs.fileName);
    fixture.detectChanges();
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FilePreviewComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:test');
    vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('fetches an image and renders an img element', () => {
    create({ fileId: imageId, contentType: 'image/png', fileSize: 1024, fileName: 'photo.png' });

    http.expectOne(fileUrl(imageId)).flush(new Blob(['x'], { type: 'image/png' }));
    fixture.detectChanges();

    const img = (fixture.nativeElement as HTMLElement).querySelector('img');
    expect(img).toBeTruthy();
    expect(img?.getAttribute('src')).toBe('blob:test');
    expect(img?.getAttribute('alt')).toBe('photo.png');
  });

  it('makes no request for pdf and shows the download button', () => {
    create({ fileId: imageId, contentType: 'application/pdf', fileSize: 1024, fileName: 'report.pdf' });

    http.verify();
    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Preview unavailable.');
    expect(element.querySelector('button')?.textContent).toContain('Download');
  });

  it('shows the error state and download button when the download fails', () => {
    create({ fileId: imageId, contentType: 'image/png', fileSize: 1024, fileName: 'photo.png' });

    http
      .expectOne(fileUrl(imageId))
      .flush(new Blob(['boom']), { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    const element = fixture.nativeElement as HTMLElement;
    expect(element.textContent).toContain('Preview unavailable.');
    expect(element.querySelector('button')?.textContent).toContain('Download');
  });

  it('cancels the previous request when fileId changes', () => {
    create({ fileId: imageId, contentType: 'image/png', fileSize: 1024, fileName: 'photo.png' });
    const first = http.expectOne(fileUrl(imageId));

    fixture.componentRef.setInput('fileId', otherId);
    fixture.detectChanges();

    expect(first.cancelled).toBe(true);
    http.expectOne(fileUrl(otherId)).flush(new Blob(['y'], { type: 'image/png' }));
    fixture.detectChanges();
    expect(URL.createObjectURL).toHaveBeenCalledTimes(1);
  });

  it('revokes the object URL when the component is destroyed', () => {
    create({ fileId: imageId, contentType: 'image/png', fileSize: 1024, fileName: 'photo.png' });
    http.expectOne(fileUrl(imageId)).flush(new Blob(['x'], { type: 'image/png' }));
    fixture.detectChanges();

    fixture.destroy();

    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:test');
  });
});
