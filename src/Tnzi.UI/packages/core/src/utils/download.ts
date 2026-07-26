/**
 * How long to keep a download object URL alive after the click (ms).
 * Long enough for any browser to have started reading the blob, short enough
 * that a page doing many exports does not accumulate them.
 */
const OBJECT_URL_TTL = 60_000;

/**
 * Trigger a browser download for a Blob via a temporary anchor element.
 *
 * Centralizes the object-URL lifecycle (create -> click -> revoke) that was
 * previously hand-rolled per page. No-op outside a DOM environment.
 *
 * The revoke is deferred rather than synchronous: the anchor click only
 * SCHEDULES the download, and revoking in the same tick has been observed to
 * cancel it (Firefox and Safari in particular). The URL is still released, just
 * after a grace period.
 */
export function downloadBlob(blob: Blob, fileName: string): void {
  if (typeof URL === 'undefined' || typeof document === 'undefined') return;

  const url = URL.createObjectURL(blob);
  try {
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
  } catch {
    // Anchor never fired - nothing is reading the URL, release it immediately.
    URL.revokeObjectURL(url);
    return;
  }
  setTimeout(() => URL.revokeObjectURL(url), OBJECT_URL_TTL);
}
