import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, sep } from 'node:path'
import { applyThemeModeToDocument, DARK_CLASS } from '../../src/theme/document'

describe('applyThemeModeToDocument', () => {
  let originalMatchMedia: typeof window.matchMedia

  beforeEach(() => {
    originalMatchMedia = window.matchMedia
    document.documentElement.classList.remove(DARK_CLASS)
  })

  afterEach(() => {
    window.matchMedia = originalMatchMedia
    document.documentElement.classList.remove(DARK_CLASS)
  })

  it('adds the dark class for mode=dark and returns the resolved mode', () => {
    expect(applyThemeModeToDocument('dark')).toBe('dark')
    expect(document.documentElement.classList.contains(DARK_CLASS)).toBe(true)
  })

  it('removes the dark class for mode=light', () => {
    document.documentElement.classList.add(DARK_CLASS)
    expect(applyThemeModeToDocument('light')).toBe('light')
    expect(document.documentElement.classList.contains(DARK_CLASS)).toBe(false)
  })

  it('resolves mode=auto against the OS preference (dark)', () => {
    window.matchMedia = vi.fn().mockReturnValue({ matches: true }) as never
    expect(applyThemeModeToDocument('auto')).toBe('dark')
    expect(document.documentElement.classList.contains(DARK_CLASS)).toBe(true)
  })

  it('resolves mode=auto against the OS preference (light)', () => {
    document.documentElement.classList.add(DARK_CLASS)
    window.matchMedia = vi.fn().mockReturnValue({ matches: false }) as never
    expect(applyThemeModeToDocument('auto')).toBe('light')
    expect(document.documentElement.classList.contains(DARK_CLASS)).toBe(false)
  })
})

/**
 * Convention gate.
 *
 * Three separate places used to compute "is this dark?" and write the class,
 * each with its own copy of the auto/system resolution, and they disagreed.
 * Nothing failed when they did - the page was just the wrong colour. A budget
 * or a behavioural test cannot catch a fourth copy appearing; only a rule
 * about the source can.
 */
describe('dark class single-writer convention', () => {
  // This file lives at __tests__/theme/, so `src` is two levels up.
  const SRC = join(__dirname, '..', '..', 'src')
  // The one file allowed to write the class.
  const ALLOWED = join('theme', 'document.ts')
  // `classList.add('dark')` / `.remove("dark")` / `.toggle(`dark`, …)`.
  const WRITE = /classList\s*\.\s*(?:add|remove|toggle|replace)\s*\(\s*['"`]dark['"`]/

  function walk(dir: string): string[] {
    return readdirSync(dir).flatMap((entry) => {
      const full = join(dir, entry)
      if (statSync(full).isDirectory()) {
        return entry === '__tests__' ? [] : walk(full)
      }
      return /\.(ts|vue)$/.test(entry) ? [full] : []
    })
  }

  it('has exactly one module writing the dark class', () => {
    const offenders = walk(SRC)
      .filter((file) => relative(SRC, file).split(sep).join(sep) !== ALLOWED)
      .filter((file) => WRITE.test(readFileSync(file, 'utf8')))
      .map((file) => relative(SRC, file))

    expect(
      offenders,
      `These modules write the dark class directly. Call applyThemeModeToDocument() ` +
        `from src/theme/document.ts instead, so 'auto' is resolved in one place:\n` +
        offenders.map((f) => `  - ${f}`).join('\n'),
    ).toEqual([])
  })

  it('the allowed writer really does write it (guards against a vacuous pass)', () => {
    const source = readFileSync(join(SRC, ALLOWED), 'utf8')
    expect(source).toMatch(/classList\s*\.\s*toggle\(\s*DARK_CLASS/)
  })
})
