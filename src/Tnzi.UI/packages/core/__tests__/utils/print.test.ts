import { describe, it, expect, vi, afterEach } from 'vitest';
import { printHtml } from '../../src/utils/print';

/**
 * `printHtml` renders a self-contained document (its own `@page` / `@media
 * print` rules) through an off-screen iframe.
 *
 * An iframe rather than `window.open`: a popup is blocked by default unless the
 * click is still on the stack, and this content is normally fetched over the
 * network first, so by then the gesture is gone.
 *
 * This package's tests run without a DOM, so - like `download.test.ts` - the
 * document is stubbed on `globalThis`.
 */
describe('printHtml', () => {
  afterEach(() => {
    delete (globalThis as Record<string, unknown>).document;
    vi.useRealTimers();
  });

  function stubDom(contentWindow: unknown) {
    const frame = { style: {}, contentWindow, parentNode: null as unknown } as Record<string, unknown>;
    const appendChild = vi.fn(() => {
      frame.parentNode = { removeChild };
    });
    const removeChild = vi.fn(() => {
      frame.parentNode = null;
    });
    (globalThis as Record<string, unknown>).document = {
      createElement: vi.fn(() => frame),
      body: { appendChild, removeChild },
    };
    return { frame, appendChild, removeChild };
  }

  it('writes the document into an off-screen frame and prints it', () => {
    vi.useFakeTimers();
    const doc = { open: vi.fn(), write: vi.fn(), close: vi.fn() };
    const print = vi.fn();
    const { frame, appendChild } = stubDom({ document: doc, focus: vi.fn(), print });

    printHtml('<html><body>cheque</body></html>');

    expect(appendChild).toHaveBeenCalledWith(frame);
    expect(doc.write).toHaveBeenCalledWith('<html><body>cheque</body></html>');
    // open/close bracket the write - without them the frame keeps the parent's
    // document and the markup lands nowhere.
    expect(doc.open).toHaveBeenCalled();
    expect(doc.close).toHaveBeenCalled();
    expect(print).toHaveBeenCalledTimes(1);
    // Off-screen, not merely hidden: `display:none` frames do not print.
    expect(frame.style).toMatchObject({ position: 'fixed', width: '0', height: '0' });
  });

  it('keeps the frame alive past the print call, then tears it down', () => {
    vi.useFakeTimers();
    const { removeChild } = stubDom({
      document: { open: vi.fn(), write: vi.fn(), close: vi.fn() },
      focus: vi.fn(),
      print: vi.fn(),
    });

    printHtml('<html></html>');

    // Removing it in the same tick has been observed to print a blank page:
    // some browsers read the document again while spooling.
    expect(removeChild).not.toHaveBeenCalled();

    vi.advanceTimersByTime(60_000);
    expect(removeChild).toHaveBeenCalledTimes(1);
  });

  it('cleans up when the frame has no window to print into', () => {
    const { removeChild } = stubDom(null);

    printHtml('<html></html>');

    // No orphan frame left behind in the document.
    expect(removeChild).toHaveBeenCalledTimes(1);
  });

  it('no-ops outside a DOM environment', () => {
    expect(() => printHtml('<html></html>')).not.toThrow();
  });

  /**
   * Cheques carry a logo and a signature image. `document.write` parses
   * synchronously but the images it creates are still fetching when `close()`
   * returns, so printing on that stack spools a blank logo - and nothing about
   * the paper says it was incomplete.
   */
  describe('images', () => {
    function fakeImage(complete: boolean) {
      const handlers: Record<string, () => void> = {};
      return {
        complete,
        addEventListener: vi.fn((type: string, fn: () => void) => {
          handlers[type] = fn;
        }),
        fire: (type: string) => handlers[type]?.(),
      };
    }

    function stubDomWithImages(images: unknown[]) {
      const print = vi.fn();
      const doc = { open: vi.fn(), write: vi.fn(), close: vi.fn(), images };
      stubDom({ document: doc, focus: vi.fn(), print });
      return { print };
    }

    it('waits for a pending image before printing', () => {
      vi.useFakeTimers();
      const img = fakeImage(false);
      const { print } = stubDomWithImages([img]);

      printHtml('<html><body><img src="logo.png"></body></html>');
      expect(print).not.toHaveBeenCalled();

      img.fire('load');
      // Settled, but the layout beat has not run yet.
      expect(print).not.toHaveBeenCalled();
      vi.advanceTimersByTime(1);
      expect(print).toHaveBeenCalledTimes(1);
    });

    it('prints on the caller stack when every image is already complete', () => {
      vi.useFakeTimers();
      const { print } = stubDomWithImages([fakeImage(true), fakeImage(true)]);

      printHtml('<html><body><img src="logo.png"></body></html>');

      // Nothing can change between here and the dialog, so nothing is deferred.
      expect(print).toHaveBeenCalledTimes(1);
    });

    it('resolves a broken image like any other - error must not hang the print', () => {
      vi.useFakeTimers();
      const img = fakeImage(false);
      const { print } = stubDomWithImages([img]);

      printHtml('<html><body><img src="404.png"></body></html>');
      img.fire('error');
      vi.advanceTimersByTime(1);

      expect(print).toHaveBeenCalledTimes(1);
    });

    it('prints anyway once the wait times out, and only once', () => {
      vi.useFakeTimers();
      const img = fakeImage(false);
      const { print } = stubDomWithImages([img]);

      printHtml('<html><body><img src="never-answers.png"></body></html>');

      vi.advanceTimersByTime(5_000);
      vi.advanceTimersByTime(1);
      expect(print).toHaveBeenCalledTimes(1);

      // A late-arriving image must not trigger a second dialog.
      img.fire('load');
      vi.advanceTimersByTime(1);
      expect(print).toHaveBeenCalledTimes(1);
    });

    it('waits for the last of several images, not the first', () => {
      vi.useFakeTimers();
      const logo = fakeImage(false);
      const signature = fakeImage(false);
      const { print } = stubDomWithImages([logo, signature]);

      printHtml('<html><body><img><img></body></html>');

      logo.fire('load');
      vi.advanceTimersByTime(1);
      expect(print).not.toHaveBeenCalled();

      signature.fire('load');
      vi.advanceTimersByTime(1);
      expect(print).toHaveBeenCalledTimes(1);
    });

    it('imageTimeout: 0 opts out of waiting entirely', () => {
      vi.useFakeTimers();
      const { print } = stubDomWithImages([fakeImage(false)]);

      printHtml('<html><body><img></body></html>', { imageTimeout: 0 });

      expect(print).toHaveBeenCalledTimes(1);
    });
  });
});
