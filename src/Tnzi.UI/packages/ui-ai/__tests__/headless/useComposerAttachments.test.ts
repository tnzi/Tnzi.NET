import { describe, it, expect, vi, beforeEach } from 'vitest';
import { effectScope } from 'vue';
import { useComposerAttachments, isAcceptedFile } from '../../src/headless/useComposerAttachments';

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
  /**
   * `TThreadComposer` gated only the paperclip on `enableAttachments`; paste
   * and drop still produced chips, so a consumer that turned attachments off
   * because its transport could not send them still collected files that
   * vanished on Send. The gate lives here so every entry point shares it.
   */
  describe('enabled gate', () => {
    const dropEvent = (files: File[]) =>
      ({ preventDefault() {}, dataTransfer: { files } }) as unknown as DragEvent;
    const pasteEvent = (files: File[]) =>
      ({
        clipboardData: { items: files.map((f) => ({ type: f.type, getAsFile: () => f })) },
      }) as unknown as ClipboardEvent;

    it('paste, drop, drag-over and addFiles are inert when disabled', () => {
      const c = useComposerAttachments({ enabled: () => false });
      c.onPaste(pasteEvent([makeFile('shot.png', 10, 'image/png')]));
      c.onDrop(dropEvent([makeFile('a.txt', 10)]));
      c.addFiles([makeFile('b.txt', 10)]);
      c.onDragOver({ preventDefault() {} } as unknown as DragEvent);
      expect(c.files.value).toHaveLength(0);
      expect(c.isDragOver.value).toBe(false);
    });

    it('the gate is read live, so a prop flip takes effect', () => {
      let enabled = false;
      const c = useComposerAttachments({ enabled: () => enabled });
      c.onPaste(pasteEvent([makeFile('shot.png', 10, 'image/png')]));
      expect(c.files.value).toHaveLength(0);
      enabled = true;
      c.onPaste(pasteEvent([makeFile('shot.png', 10, 'image/png')]));
      expect(c.files.value).toHaveLength(1);
    });

    it('defaults to enabled', () => {
      const c = useComposerAttachments();
      c.onDrop(dropEvent([makeFile('a.txt', 10)]));
      expect(c.files.value).toHaveLength(1);
    });
  });

  /**
   * The picker applies `accept` itself; a drop, a paste or a host handing a
   * file over does not. The composer checks it for every path so none of them
   * can attach a file the picker would never have offered.
   */
  describe('accepted types', () => {
    it('matches extensions, exact MIME types and wildcards, case-insensitively', () => {
      const accept = '.PDF, image/*, text/csv';
      expect(isAcceptedFile(makeFile('Report.pdf', 1, 'application/pdf'), accept)).toBe(true);
      expect(isAcceptedFile(makeFile('shot.png', 1, 'image/png'), accept)).toBe(true);
      expect(isAcceptedFile(makeFile('t.csv', 1, 'text/csv'), accept)).toBe(true);
      expect(isAcceptedFile(makeFile('a.exe', 1, 'application/x-msdownload'), accept)).toBe(false);
    });

    it('a file without a MIME type can only match an extension, as in the browser picker', () => {
      expect(isAcceptedFile(makeFile('notes.md', 1, ''), 'text/*')).toBe(false);
      expect(isAcceptedFile(makeFile('notes.md', 1, ''), 'text/*,.md')).toBe(true);
    });

    it('an empty accept list takes every type', () => {
      expect(isAcceptedFile(makeFile('a.exe', 1, 'application/x-msdownload'), '')).toBe(true);
      expect(isAcceptedFile(makeFile('a.exe', 1, 'application/x-msdownload'), undefined)).toBe(true);
    });

    it('refuses a type outside the list with its own reason, ahead of the size check', () => {
      const onReject = vi.fn();
      const c = useComposerAttachments({ maxFileSize: 100, accept: () => '.pdf', onReject });

      const result = c.addFiles([makeFile('ok.pdf', 10), makeFile('huge.exe', 500), makeFile('big.pdf', 500)]);

      expect(c.files.value.map((f) => f.name)).toEqual(['ok.pdf']);
      expect(result?.added.map((f) => f.name)).toEqual(['ok.pdf']);
      expect(result?.rejected.map((r) => [r.file.name, r.reason])).toEqual([
        ['huge.exe', 'unsupported-type'],
        ['big.pdf', 'too-large'],
      ]);
      expect(onReject).toHaveBeenCalledTimes(1);
    });

    it('applies to a drop too, not only to addFiles', () => {
      const c = useComposerAttachments({ accept: () => 'image/*' });
      c.onDrop({ preventDefault() {}, dataTransfer: { files: [makeFile('a.txt', 1, 'text/plain')] } } as unknown as DragEvent);
      expect(c.files.value).toHaveLength(0);
      expect(c.rejected.value[0]?.reason).toBe('unsupported-type');
    });

    it('reads the list live, so a prop change takes effect', () => {
      let accept = '.pdf';
      const c = useComposerAttachments({ accept: () => accept });
      c.addFiles([makeFile('a.txt', 1)]);
      expect(c.files.value).toHaveLength(0);
      accept = '.txt';
      c.addFiles([makeFile('a.txt', 1)]);
      expect(c.files.value).toHaveLength(1);
    });

    it('returns null when attachments are off: nothing was considered, so nothing was refused', () => {
      const onReject = vi.fn();
      const c = useComposerAttachments({ enabled: () => false, onReject });
      expect(c.addFiles([makeFile('a.txt', 1)])).toBeNull();
      expect(onReject).not.toHaveBeenCalled();
    });
  });
});
