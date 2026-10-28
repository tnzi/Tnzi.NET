/**
 * Keyboard helpers shared by every text field that acts on Enter.
 */

/**
 * Whether a key event belongs to an IME composition (Chinese, Japanese, Korean
 * and other input methods), so Enter is committing a candidate rather than
 * submitting.
 *
 * `isComposing` alone is not enough: Safari fires `compositionend` before the
 * committing `keydown`, so that event reports `isComposing: false` and only
 * its legacy `keyCode` of 229 ("an IME is processing this key") gives it away.
 * Acting on that Enter sends the half-typed text, or runs whatever command is
 * highlighted.
 */
export function isImeComposing(event: Pick<KeyboardEvent, 'isComposing' | 'keyCode'>): boolean {
  return event.isComposing || event.keyCode === 229;
}
