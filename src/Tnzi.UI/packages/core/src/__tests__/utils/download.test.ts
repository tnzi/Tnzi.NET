import { describe, expect, it, vi } from 'vitest';
import { downloadBlob } from '../../utils/download';

describe('downloadBlob', () => {
  it('no-ops outside a DOM environment', () => {
    expect(() => downloadBlob(new Blob(['x']), 'a.csv')).not.toThrow();
  });

  it('creates, clicks and cleans up an anchor when DOM is available', () => {
    vi.useFakeTimers();
    const anchor = { href: '', download: '', click: vi.fn() } as unknown as HTMLAnchorElement & {
      click: ReturnType<typeof vi.fn>;
    };
    const appendChild = vi.fn();
    const removeChild = vi.fn();
    (globalThis as Record<string, unknown>).document = {
      createElement: vi.fn(() => anchor),
      body: { appendChild, removeChild },
    };
    const createObjectURL = vi.fn(() => 'blob:mock');
    const revokeObjectURL = vi.fn();
    const urlGlobal = URL as unknown as Record<string, unknown>;
    const origCreate = urlGlobal.createObjectURL;
    const origRevoke = urlGlobal.revokeObjectURL;
    urlGlobal.createObjectURL = createObjectURL;
    urlGlobal.revokeObjectURL = revokeObjectURL;

    try {
      downloadBlob(new Blob(['x'], { type: 'text/csv' }), 'a.csv');

      expect(anchor.download).toBe('a.csv');
      expect(anchor.href).toBe('blob:mock');
      expect(anchor.click).toHaveBeenCalledTimes(1);
      expect(appendChild).toHaveBeenCalledWith(anchor);
      expect(removeChild).toHaveBeenCalledWith(anchor);
      // Revoking in the same tick as the click cancels the download in some
      // browsers, so the URL must still be alive right after the click...
      expect(revokeObjectURL).not.toHaveBeenCalled();
      // ...and released once the grace period has elapsed.
      vi.advanceTimersByTime(60_000);
      expect(revokeObjectURL).toHaveBeenCalledWith('blob:mock');
    } finally {
      urlGlobal.createObjectURL = origCreate;
      urlGlobal.revokeObjectURL = origRevoke;
      delete (globalThis as Record<string, unknown>).document;
      vi.useRealTimers();
    }
  });

  it('revokes immediately when the anchor could not be clicked', () => {
    (globalThis as Record<string, unknown>).document = {
      createElement: vi.fn(() => { throw new Error('no DOM'); }),
      body: { appendChild: vi.fn(), removeChild: vi.fn() },
    };
    const revokeObjectURL = vi.fn();
    const urlGlobal = URL as unknown as Record<string, unknown>;
    const origCreate = urlGlobal.createObjectURL;
    const origRevoke = urlGlobal.revokeObjectURL;
    urlGlobal.createObjectURL = vi.fn(() => 'blob:mock');
    urlGlobal.revokeObjectURL = revokeObjectURL;

    try {
      expect(() => downloadBlob(new Blob(['x']), 'a.csv')).not.toThrow();
      expect(revokeObjectURL).toHaveBeenCalledWith('blob:mock');
    } finally {
      urlGlobal.createObjectURL = origCreate;
      urlGlobal.revokeObjectURL = origRevoke;
      delete (globalThis as Record<string, unknown>).document;
    }
  });
});
