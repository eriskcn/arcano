export type PreviewKind = 'image' | 'media' | 'text' | 'none';

export const PREVIEW_MAX_BYTES = 25 * 1024 * 1024;

const IMAGE_TYPES = new Set(['image/png', 'image/jpeg', 'image/gif', 'image/webp', 'image/bmp']);
const TEXT_TYPES = new Set(['application/json']);
const BLOCKED_TYPES = new Set(['text/html', 'application/xhtml+xml', 'image/svg+xml']);

// Blocked types are never rendered: uploads come from other users and HTML/SVG would run their script.
export function previewKind(contentType: string, fileSize: number): PreviewKind {
  if (fileSize > PREVIEW_MAX_BYTES) return 'none';

  const type = contentType.split(';')[0].trim().toLowerCase();
  if (BLOCKED_TYPES.has(type) || type.includes('xml')) return 'none';
  if (IMAGE_TYPES.has(type)) return 'image';
  if (type.startsWith('audio/') || type.startsWith('video/')) return 'media';
  if (type.startsWith('text/') || TEXT_TYPES.has(type)) return 'text';
  return 'none';
}
