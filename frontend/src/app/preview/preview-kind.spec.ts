import { PREVIEW_MAX_BYTES, previewKind } from './preview-kind';

describe('previewKind', () => {
  const small = 1024;

  it('returns image for common image types', () => {
    for (const type of ['image/png', 'image/jpeg', 'image/gif', 'image/webp', 'image/bmp']) {
      expect(previewKind(type, small)).toBe('image');
    }
  });

  it('returns media for audio and video types', () => {
    expect(previewKind('audio/mpeg', small)).toBe('media');
    expect(previewKind('video/mp4', small)).toBe('media');
  });

  it('returns text for plain text, markdown, csv, json and other text types', () => {
    for (const type of ['text/plain', 'text/markdown', 'text/csv', 'application/json', 'text/x-python']) {
      expect(previewKind(type, small)).toBe('text');
    }
  });

  it('ignores parameters and case in the content type', () => {
    expect(previewKind('Text/Plain; charset=utf-8', small)).toBe('text');
  });

  it('blocks html', () => {
    expect(previewKind('text/html', small)).toBe('none');
  });

  it('blocks svg', () => {
    expect(previewKind('image/svg+xml', small)).toBe('none');
  });

  it('blocks xml and xhtml', () => {
    expect(previewKind('application/xml', small)).toBe('none');
    expect(previewKind('text/xml', small)).toBe('none');
    expect(previewKind('application/xhtml+xml', small)).toBe('none');
  });

  it('returns none for pdf', () => {
    expect(previewKind('application/pdf', small)).toBe('none');
  });

  it('returns none above the size cap', () => {
    expect(previewKind('image/png', PREVIEW_MAX_BYTES)).toBe('image');
    expect(previewKind('image/png', PREVIEW_MAX_BYTES + 1)).toBe('none');
    expect(previewKind('text/plain', PREVIEW_MAX_BYTES + 1)).toBe('none');
  });

  it('returns none for unknown types', () => {
    expect(previewKind('application/octet-stream', small)).toBe('none');
    expect(previewKind('application/zip', small)).toBe('none');
  });
});
