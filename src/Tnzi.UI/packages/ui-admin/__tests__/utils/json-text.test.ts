import { describe, it, expect } from 'vitest'
import { formatJsonText, minifyJsonText } from '../../src/utils/json-text'

/**
 * Re-indentation that only moves whitespace. For any value JavaScript can hold
 * exactly, the output must be byte-identical to JSON.stringify, so swapping it
 * in changes nothing a reader could see except the cases JSON.stringify gets
 * wrong (big integers, duplicate keys, number spellings).
 */
const SAMPLES: unknown[] = [
  {},
  [],
  { a: 1 },
  { a: [], b: {}, c: [{}], d: [[]] },
  { nested: { deep: { list: [1, 2, { x: null, y: true, z: false }] } } },
  [1, 'two', null, { three: 3 }],
  { s: 'commas, colons: braces {} brackets [] and "quotes"' },
  { s: 'back\\slash and escaped \\" quote and tab\t and newline\n' },
  { unicode: 'café 中文' },
  { neg: -12.5, exp: 1.5e-7, zero: 0 },
  'a top-level string',
  42,
  null,
]

describe('formatJsonText', () => {
  it('matches JSON.stringify(value, null, 2) for values JavaScript holds exactly', () => {
    for (const value of SAMPLES) {
      const compact = JSON.stringify(value)
      expect(formatJsonText(compact, { indent: 2 })).toBe(JSON.stringify(value, null, 2))
    }
  })

  it('normalises existing whitespace instead of adding to it', () => {
    const messy = '{ "a" :\r\n\t[ 1 ,2 ] , "b" : { } }'
    expect(formatJsonText(messy, { indent: 2 })).toBe(JSON.stringify(JSON.parse(messy), null, 2))
  })

  it('★ keeps integers past 2^53 digit for digit', () => {
    const text = '{"id":1234567890123456789,"ids":[9007199254740993]}'
    const out = formatJsonText(text, { indent: 2 })
    expect(out).toContain('1234567890123456789')
    expect(out).toContain('9007199254740993')
  })

  it('★ keeps duplicate keys and number spellings', () => {
    expect(formatJsonText('{"a":1.0,"a":1E3}', { indent: 2 })).toBe('{\n  "a": 1.0,\n  "a": 1E3\n}')
  })

  it('copies string tokens verbatim by default, escapes included', () => {
    expect(formatJsonText('{"s":"caf\\u00e9"}', { indent: 2 })).toBe('{\n  "s": "caf\\u00e9"\n}')
  })

  it('normalises string escapes the way JSON.stringify does when asked', () => {
    const text = '{"s":"caf\\u00e9 \\/ ok","n":1234567890123456789}'
    expect(formatJsonText(text, { indent: 2, normalizeStrings: true })).toBe(
      '{\n  "s": "café / ok",\n  "n": 1234567890123456789\n}',
    )
  })
})

describe('minifyJsonText', () => {
  it('matches JSON.stringify(value) for values JavaScript holds exactly', () => {
    for (const value of SAMPLES) {
      const pretty = JSON.stringify(value, null, 4)
      expect(minifyJsonText(pretty)).toBe(JSON.stringify(value))
    }
  })

  it('★ keeps integers past 2^53', () => {
    expect(minifyJsonText('{\n  "id": 1234567890123456789\n}')).toBe('{"id":1234567890123456789}')
  })

  it('does not touch whitespace inside strings', () => {
    expect(minifyJsonText('{ "s" : "a  b\\n c" }')).toBe('{"s":"a  b\\n c"}')
  })
})
