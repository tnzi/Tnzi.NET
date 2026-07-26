import { describe, it, expect, vi, beforeEach } from 'vitest';
import { effectScope } from 'vue';
import { useComposerAttachments } from '../../src/composables/useComposerAttachments';

function makeFile(name: string, size: number, type = 'text/plain'): File {
  const file = new File(['x'], name, { type });
  // happy-dom derives `size` from the blob parts; override it so the tests can
  // exercise the size limit without allocating megabytes.
  Object.defineProperty(file, 'size', { value: size });
  return file;
}

beforeEach(() => {
  // happy-dom does not implement object URLs.
  globalThis.URL.createObjectURL = vi.fn(() => 'blob:mock');
  globalThis.URL.revokeObjectURL = vi.fn();
});

describe('useComposerAttachments', () => {
  it('accepts files within the size limit', () => {
    const c = useComposerAttachments({ maxFileSize: 100 });
    c.addFiles([makeFile('a.txt', 50)]);
    expect(c.files.value).toHaveLength(1);
    expect(c.rejected.value).toHaveLength(0);
  });

  it('reports oversized files instead of dropping them silently', () => {
    const onReject = vi.fn();
    const c = useComposerAttachments({ maxFileSize: 100, onReject });

    c.addFiles([makeFile('small.txt', 50), makeFile('huge.bin', 500)]);

    expect(c.files.value.map((f) => f.name)).toEqual(['small.txt']);
    expect(c.rejected.value).toHaveLength(1);
    expect(c.rejected.value[0]?.file.name).toBe('huge.bin');
    expect(c.rejected.value[0]?.reason).toBe('too-large');
    expect(onReject).toHaveBeenCalledTimes(1);
    expect(onReject.mock.calls[0]?.[0]).toHaveLength(1);
  });

  it('does not call onReject when everything is accepted', () => {
    const onReject = vi.fn();
    const c = useComposerAttachments({ maxFileSize: 100, onReject });
    c.addFiles([makeFile('a.txt', 10)]);
    expect(onReject).not.toHaveBeenCalled();
  });

  it('replaces the rejection list on each addFiles call', () => {
    const c = useComposerAttachments({ maxFileSize: 100 });
    c.addFiles([makeFile('one.bin', 500)]);
    expect(c.rejected.value).toHaveLength(1);

    c.addFiles([makeFile('ok.txt', 10)]);
    expect(c.rejected.value).toHaveLength(0);
  });

  it('clears the rejection list on demand and on clearFiles', () => {
    const c = useComposerAttachments({ maxFileSize: 100 });
    c.addFiles([makeFile('big.bin', 500)]);
    c.clearRejected();
    expect(c.rejected.value).toHaveLength(0);

    c.addFiles([makeFile('big2.bin', 500)]);
    c.clearFiles();
    expect(c.rejected.value).toHaveLength(0);
  });

  it('creates and revokes preview URLs for images only', () => {
    const c = useComposerAttachments();
    const img = makeFile('pic.png', 10, 'image/png');
    const txt = makeFile('a.txt', 10);
    c.addFiles([img, txt]);

    expect(c.isImageFile(img)).toBe(true);
    expect(c.getPreviewUrl(img)).toBe('blob:mock');
    expect(c.getPreviewUrl(txt)).toBe('');

    c.removeFile(0);
    expect(globalThis.URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock');
  });

  it('removes a file by index without touching the rest', () => {
    const c = useComposerAttachments();
    c.addFiles([makeFile('a.txt', 1), makeFile('b.txt', 1), makeFile('c.txt', 1)]);
    c.removeFile(1);
    expect(c.files.value.map((f) => f.name)).toEqual(['a.txt', 'c.txt']);
  });

  it('revokes preview URLs when the owning scope is disposed', () => {
    const scope = effectScope();
    scope.run(() => {
      const c = useComposerAttachments();
      c.addFiles([makeFile('pic.png', 10, 'image/png')]);
    });

    scope.stop();
    expect(globalThis.URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock');
  });

  it('tracks drag state and ignores dragleave onto a child element', () => {
    const c = useComposerAttachments();
    const parent = document.createElement('div');
    const child = document.createElement('span');
    parent.appendChild(child);

    c.onDragOver({ preventDefault() {} } as unknown as DragEvent);
    expect(c.isDragOver.value).toBe(true);

    c.onDragLeave({ currentTarget: parent, relatedTarget: child } as unknown as DragEvent);
    expect(c.isDragOver.value).toBe(true);

    c.onDragLeave({ currentTarget: parent, relatedTarget: null } as unknown as DragEvent);
    expect(c.isDragOver.value).toBe(false);
  });
});
