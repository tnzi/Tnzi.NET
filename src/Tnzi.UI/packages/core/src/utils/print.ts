/**
 * How long to keep the print iframe in the document after `print()` returns.
 *
 * `window.print()` is synchronous in the sense that it returns once the dialog
 * closes, but some browsers read the document again while spooling. Removing
 * the frame immediately has been observed to produce a blank page, so it is
 * torn down after a grace period instead.
 */
const PRINT_FRAME_TTL = 60_000;

/**
 * Default upper bound on waiting for the document's images before printing.
 *
 * The wait must be bounded: an image whose URL 404s behind a proxy that never
 * answers would otherwise hold the print dialog forever, and a print that never
 * opens is worse than one missing a logo.
 */
const DEFAULT_IMAGE_TIMEOUT = 5_000;

export interface PrintHtmlOptions {
  /**
   * Milliseconds to wait for the document's images to finish loading before
   * printing anyway. Default 5000. Pass `0` to print immediately without
   * waiting (the pre-image-wait behaviour).
   */
  imageTimeout?: number;
}

/**
 * Print a self-contained HTML document through an off-screen iframe.
 *
 * For documents that carry their own `@page` / `@media print` rules and must be
 * laid out by the browser exactly as written - cheques on pre-printed stock,
 * statements, labels - rather than screenshotted or re-styled by the host page.
 *
 * An iframe rather than `window.open`: a popup is blocked by default in most
 * browsers unless the click is still on the stack, and this content is normally
 * fetched over the network first, so by the time it is ready the gesture is gone.
 *
 * **Images are waited for.** `document.write` parses synchronously, but the
 * `<img>` elements it creates are still fetching when `close()` returns, so
 * printing right away can spool a cheque with a blank logo and an empty
 * signature - and nothing about the printed page says it was incomplete. Every
 * image that is not already `complete` is awaited (load *or* error, a broken
 * image resolves like any other), capped by `imageTimeout`. A document with no
 * pending images prints synchronously, exactly as before.
 *
 * ⚠️ **The browser tells us nothing about what happened next** - whether the
 * dialog was confirmed, whether paper came out, whether the printer was even on.
 * Callers must not treat "printHtml returned" as "it printed"; that is exactly
 * why anything printed this way needs to remain re-printable.
 *
 * The document is written into a same-origin iframe and therefore runs with the
 * host page's privileges. Only pass markup the application itself produced.
 * For rendering untrusted HTML, use a sandboxed frame instead.
 *
 * No-op outside a DOM environment.
 *
 * @param html A complete HTML document (including its own `<style>` / `@page` rules).
 * @param options See {@link PrintHtmlOptions}.
 */
export function printHtml(html: string, options: PrintHtmlOptions = {}): void {
  if (typeof document === 'undefined') return;

  const iframe = document.createElement('iframe');
  Object.assign(iframe.style, {
    position: 'fixed',
    right: '0',
    bottom: '0',
    width: '0',
    height: '0',
    border: '0',
  });
  document.body.appendChild(iframe);

  const frameWindow = iframe.contentWindow;
  if (!frameWindow) {
    document.body.removeChild(iframe);
    return;
  }

  const doc = frameWindow.document;
  doc.open();
  doc.write(html);
  doc.close();

  const print = (): void => {
    try {
      frameWindow.focus();
      frameWindow.print();
    } finally {
      // Deferred: see PRINT_FRAME_TTL. Guarded because the caller may have
      // navigated away while the dialog was open.
      setTimeout(() => {
        if (iframe.parentNode) iframe.parentNode.removeChild(iframe);
      }, PRINT_FRAME_TTL);
    }
  };

  const timeout = options.imageTimeout ?? DEFAULT_IMAGE_TIMEOUT;
  const pending = timeout > 0 ? pendingImages(doc) : [];
  if (pending.length === 0) {
    // Nothing was still loading, so nothing can change between here and the
    // dialog - print on the caller's stack, as this function always has.
    print();
    return;
  }

  whenSettled(pending, timeout, () => {
    // One macrotask beat after the last image resolves, so the layout that the
    // now-sized images force has flushed before the document is spooled.
    setTimeout(print, 0);
  });
}

/** Images the parser created that have not finished loading (or failing) yet. */
function pendingImages(doc: Document): HTMLImageElement[] {
  const images = doc.images;
  if (!images || typeof images.length !== 'number') return [];
  return Array.from(images).filter((img) => !img.complete);
}

/**
 * Run `done` once every image has resolved, or once `timeout` elapses -
 * whichever comes first, and exactly once either way.
 */
function whenSettled(images: HTMLImageElement[], timeout: number, done: () => void): void {
  let settled = false;
  let remaining = images.length;

  const finish = (): void => {
    if (settled) return;
    settled = true;
    clearTimeout(timer);
    done();
  };

  const timer = setTimeout(finish, timeout);

  const onSettle = (): void => {
    remaining -= 1;
    if (remaining <= 0) finish();
  };

  for (const img of images) {
    // `error` resolves too: a broken image is a finished image, and the print
    // must not hang on it.
    img.addEventListener('load', onSettle, { once: true });
    img.addEventListener('error', onSettle, { once: true });
  }
}
