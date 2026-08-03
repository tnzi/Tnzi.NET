import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { useBreakpoints } from '../../../src/headless/theme/useBreakpoints'

// Track media queries so we can mock matchMedia by width
function mockMatchMedia(width: number) {
  const mm = vi.fn().mockImplementation((query: string) => {
    // Parse '(min-width: 768px)' and '(max-width: 767.9px)' forms
    const minMatch = /min-width:\s*(\d+(?:\.\d+)?)px/.exec(query)
    const maxMatch = /max-width:\s*(\d+(?:\.\d+)?)px/.exec(query)
    let matches = true
    if (minMatch) matches = matches && width >= parseFloat(minMatch[1]!)
    if (maxMatch) matches = matches && width <= parseFloat(maxMatch[1]!)
    return {
      matches,
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    }
  })
  window.matchMedia = mm as unknown as typeof window.matchMedia
}

describe('useBreakpoints', () => {
  const originalMM = window.matchMedia
  const originalInner = window.innerWidth

  afterEach(() => {
    window.matchMedia = originalMM
    Object.defineProperty(window, 'innerWidth', { value: originalInner, configurable: true })
  })

  it('returns boolean refs for all Tailwind breakpoints', () => {
    Object.defineProperty(window, 'innerWidth', { value: 1024, configurable: true })
    mockMatchMedia(1024)
    const bp = useBreakpoints()
    expect(bp.raw).toBeDefined()
    expect(typeof bp.isMobile.value).toBe('boolean')
    expect(typeof bp.isTablet.value).toBe('boolean')
    expect(typeof bp.isDesktop.value).toBe('boolean')
    expect(bp.smAndUp).toBeDefined()
    expect(bp.mdAndUp).toBeDefined()
    expect(bp.lgAndUp).toBeDefined()
    expect(bp.xlAndUp).toBeDefined()
  })

  it('isMobile is true below md (768px)', () => {
    Object.defineProperty(window, 'innerWidth', { value: 400, configurable: true })
    mockMatchMedia(400)
    const bp = useBreakpoints()
    expect(bp.isMobile.value).toBe(true)
    expect(bp.isDesktop.value).toBe(false)
  })

  it('isDesktop is true at and above lg (1024px)', () => {
    Object.defineProperty(window, 'innerWidth', { value: 1280, configurable: true })
    mockMatchMedia(1280)
    const bp = useBreakpoints()
    expect(bp.isDesktop.value).toBe(true)
    expect(bp.isMobile.value).toBe(false)
  })
})
