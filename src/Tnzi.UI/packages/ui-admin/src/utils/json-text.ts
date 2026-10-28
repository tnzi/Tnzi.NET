/**
 * Re-indent JSON text without parsing its values.
 *
 * `JSON.stringify(JSON.parse(text), null, 2)` looks like a formatter but is a
 * rewrite: every number goes through a float64, so an integer past 2^53 comes
 * back as a different integer (a 19-digit Snowflake id `1234567890123456789`
 * becomes `1234567890123456800`), duplicate keys collapse to the last one, and
 * `1.0` / `1E3` are respelled. When the result is saved, or shown to someone
 * who is approving it, that is a silent change of the data.
 *
 * These helpers only move whitespace: number, literal and string tokens are
 * copied byte for byte from the input. For every value JavaScript can hold
 * exactly the output is identical to `JSON.stringify` with the same indent.
 *
 * The input must already be valid JSON; callers validate it with `JSON.parse`
 * first (and keep that call for its error message). Malformed input is not
 * detected here.
 */

export interface FormatJsonTextOptions {
  /** Spaces per nesting level. `0` produces the minified form. Default 2. */
  indent?: number
  /**
   * Rewrite each string token the way `JSON.stringify` would (`"café"`
   * reads `"café"`). Strings round-trip exactly, so this is lossless; use it
   * for text shown to a reader. Leave it off for text that is saved, so a
   * format changes nothing but whitespace.
   */
  normalizeStrings?: boolean
}

function isJsonWhitespace(ch: string | undefined): boolean {
  return ch === ' ' || ch === '\t' || ch === '\n' || ch === '\r'
}

function isStructural(ch: string | undefined): boolean {
  return ch === '{' || ch === '}' || ch === '[' || ch === ']' || ch === ',' || ch === ':' || ch === '"'
}

/** Index just past the closing quote of the string token that opens at `start`. */
function endOfString(text: string, start: number): number {
  let i = start + 1
  while (i < text.length && text[i] !== '"') {
    i += text[i] === '\\' ? 2 : 1
  }
  return i + 1
}

export function formatJsonText(text: string, options: FormatJsonTextOptions = {}): string {
  const indent = Math.max(0, options.indent ?? 2)
  const newline = (depth: number): string => (indent > 0 ? `\n${' '.repeat(indent * depth)}` : '')
  let out = ''
  let depth = 0
  let i = 0

  while (i < text.length) {
    const ch = text[i]

    if (ch === '"') {
      const end = endOfString(text, i)
      const token = text.slice(i, end)
      out += options.normalizeStrings ? JSON.stringify(JSON.parse(token)) : token
      i = end
    } else if (isJsonWhitespace(ch)) {
      i += 1
    } else if (ch === '{' || ch === '[') {
      const close = ch === '{' ? '}' : ']'
      let next = i + 1
      while (isJsonWhitespace(text[next])) next += 1
      if (text[next] === close) {
        // An empty container stays on one line, as JSON.stringify writes it.
        out += ch + close
        i = next + 1
      } else {
        depth += 1
        out += ch + newline(depth)
        i += 1
      }
    } else if (ch === '}' || ch === ']') {
      depth -= 1
      out += newline(depth) + ch
      i += 1
    } else if (ch === ',') {
      out += ',' + newline(depth)
      i += 1
    } else if (ch === ':') {
      out += indent > 0 ? ': ' : ':'
      i += 1
    } else {
      // A number or a literal (true / false / null): copy the run verbatim.
      let end = i
      while (end < text.length && !isStructural(text[end]) && !isJsonWhitespace(text[end])) end += 1
      out += text.slice(i, end)
      i = end
    }
  }

  return out
}

/** {@link formatJsonText} with no indentation: the `JSON.stringify(value)` shape. */
export function minifyJsonText(text: string): string {
  return formatJsonText(text, { indent: 0 })
}
