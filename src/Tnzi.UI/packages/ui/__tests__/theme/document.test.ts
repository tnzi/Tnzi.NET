import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join, relative, sep } from 'node:path'
import {
  applyLanguageToDOM,
  applyThemeModeToDocument,
  applyThemeToDOM,
  DARK_CLASS,
  resolveThemeMode,
} from '../../src/theme/document'

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

describe('resolveThemeMode', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  it('returns mode unchanged for explicit light', () => {
    expect(resolveThemeMode('light')).toEqual({ resolved: 'light' })
  })

  it('returns mode unchanged for explicit dark', () => {
    expect(resolveThemeMode('dark')).toEqual({ resolved: 'dark' })
  })

  it('falls back to light in SSR (no window)', () => {
    vi.stubGlobal('window', undefined)
    expect(resolveThemeMode('auto')).toEqual({ resolved: 'light' })
    // (vi.unstubAllGlobals in afterEach already handles cleanup)
  })

  it('resolves auto via matchMedia and attaches listener with cleanup', () => {
    const removeListener = vi.fn()
    const addListener = vi.fn()
    const mockWindow = {
      matchMedia: vi.fn().mockReturnValue({
        matches: true,
        addEventListener: addListener,
        removeEventListener: removeListener,
      }),
    }
    vi.stubGlobal('window', mockWindow)

    const onChange = vi.fn()
    const result = resolveThemeMode('auto', onChange)

    expect(result.resolved).toBe('dark')
    expect(mockWindow.matchMedia).toHaveBeenCalledWith('(prefers-color-scheme: dark)')
    expect(addListener).toHaveBeenCalledTimes(1)
    expect(onChange).not.toHaveBeenCalled() // per contract - only on change, not initial

    result.cleanup?.()
    expect(removeListener).toHaveBeenCalledTimes(1)
    // Same handler identity for add + remove
    expect(removeListener.mock.calls[0][1]).toBe(addListener.mock.calls[0][1])
  })

  it('auto without onChange returns resolved without cleanup', () => {
    vi.stubGlobal('window', {
      matchMedia: vi.fn().mockReturnValue({
        matches: false,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      }),
    })
    const result = resolveThemeMode('auto')
    expect(result).toEqual({ resolved: 'light' })
    expect(result.cleanup).toBeUndefined()
  })

  // Safari before 14 only has the deprecated addListener; resolving must not
  // throw there (the theme context subscribes on every 'auto').
  it('auto with onChange still resolves when the MediaQueryList has no addEventListener', () => {
    vi.stubGlobal('window', {
      matchMedia: vi.fn().mockReturnValue({ matches: true, addListener: vi.fn() }),
    })
    const result = resolveThemeMode('auto', vi.fn())
    expect(result).toEqual({ resolved: 'dark' })
  })
})

describe('applyThemeToDOM', () => {
  let originalMatchMedia: typeof window.matchMedia
  beforeEach(() => {
    originalMatchMedia = window.matchMedia
    document.documentElement.classList.remove('dark')
  })
  afterEach(() => {
    window.matchMedia = originalMatchMedia
  })

  it('adds dark class when theme=dark', () => {
    applyThemeToDOM('dark')
    expect(document.documentElement.classList.contains('dark')).toBe(true)
  })

  it('removes dark class when theme=light', () => {
    document.documentElement.classList.add('dark')
    applyThemeToDOM('light')
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })

  it('auto theme defers to matchMedia (dark)', () => {
    window.matchMedia = vi.fn().mockReturnValue({ matches: true }) as any
    applyThemeToDOM('auto')
    expect(document.documentElement.classList.contains('dark')).toBe(true)
  })

  // Starts from the dark class already set, so a broken 'auto' resolution
  // cannot pass by simply never adding it.
  it('auto theme defers to matchMedia (light)', () => {
    document.documentElement.classList.add('dark')
    window.matchMedia = vi.fn().mockReturnValue({ matches: false }) as any
    applyThemeToDOM('auto')
    expect(document.documentElement.classList.contains('dark')).toBe(false)
  })
})

describe('applyLanguageToDOM', () => {
  it('sets documentElement.lang', () => {
    applyLanguageToDOM('zh-CN')
    expect(document.documentElement.lang).toBe('zh-CN')
    applyLanguageToDOM('en-US')
    expect(document.documentElement.lang).toBe('en-US')
  })
})
