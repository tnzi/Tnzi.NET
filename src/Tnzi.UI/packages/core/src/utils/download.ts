/**
 * Trigger a browser download for a Blob via a temporary anchor element.
 *
 * Centralizes the object-URL lifecycle (create -> click -> revoke) that was
 * previously hand-rolled per page. No-op outside a DOM environment.
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
  } finally {
    URL.revokeObjectURL(url);
  }
}
