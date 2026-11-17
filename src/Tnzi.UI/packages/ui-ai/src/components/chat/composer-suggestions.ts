/**
 * What a suggestion menu anchored to the composer needs (slash commands,
 * mentions, any "type a trigger, pick from a list" menu).
 *
 * `TThreadComposer` hands a `ComposerSuggestionContext` to its `#suggestions`
 * slot and emits every key press as `input-keydown` before acting on it. A
 * menu that is open handles the navigation keys there (ArrowUp / ArrowDown /
 * Enter / Tab / Escape) and calls `preventDefault()`; the composer then leaves
 * the key alone, so Enter picks the highlighted item instead of sending.
 *
 * The composer knows nothing about triggers: the consumer decides when its menu
 * is open (`token.text.startsWith('/')`, say) and what picking an item writes
 * (`replaceRange(token.start, token.end, '/review ')`).
 */

/** A range of the composer's text. `end` is exclusive. */
export interface ComposerTextRange {
  readonly text: string
  readonly start: number
  readonly end: number
}

/** Slot props of `TThreadComposer`'s `#suggestions` slot (and of the slots that forward it). */
export interface ComposerSuggestionContext {
  /** The composer's current text. */
  readonly text: string
  /** Caret (selection start) in `text`. */
  readonly selectionStart: number
  /** Selection end in `text`; equals `selectionStart` when nothing is selected. */
  readonly selectionEnd: number
  /**
   * The run of non-whitespace characters that ends at the caret: what a
   * `/command` or `@mention` menu filters by. Empty (`start === end`) right
   * after a space or at the start of the text, and when a range is selected.
   */
  readonly token: ComposerTextRange
  /** Replace `[start, end)` with `insert`; the caret lands after the insert and the input keeps focus. */
  readonly replaceRange: (start: number, end: number, insert: string) => void
  /** Replace the whole text; the caret goes to `caret` (default: the end). */
  readonly setText: (text: string, caret?: number) => void
  /** Focus the input without moving the caret. */
  readonly focus: () => void
}

/** The token that ends at `caret` (see `ComposerSuggestionContext.token`). */
export function tokenBeforeCaret(text: string, selectionStart: number, selectionEnd = selectionStart): ComposerTextRange {
  const caret = clamp(selectionStart, 0, text.length)
  if (clamp(selectionEnd, 0, text.length) !== caret) {
    return { text: '', start: caret, end: caret }
  }

  let start = caret
  while (start > 0 && !/\s/.test(text[start - 1]!)) {
    start--
  }

  return { text: text.slice(start, caret), start, end: caret }
}

/** `text` with `[start, end)` replaced by `insert`, and where the caret goes (right after the insert). */
export function replaceTextRange(text: string, start: number, end: number, insert: string): { text: string; caret: number } {
  const from = clamp(Math.min(start, end), 0, text.length)
  const to = clamp(Math.max(start, end), 0, text.length)
  return { text: text.slice(0, from) + insert + text.slice(to), caret: from + insert.length }
}

function clamp(value: number, min: number, max: number): number {
  return Number.isFinite(value) ? Math.min(Math.max(value, min), max) : max
}
