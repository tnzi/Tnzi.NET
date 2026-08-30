/**
 * The nav-badge display rule. Pure, so it is tested without mounting: the
 * "renders nothing" cases are the whole point of the feature (an empty chip
 * beside every menu entry would be worse than no badge at all), and they are
 * the ones a component test would quietly paper over.
 */
import { describe, it, expect } from 'vitest'
import { NAV_BADGE_MAX, normalizeNavBadge } from '../../src/utils/nav-badge'

describe('normalizeNavBadge', () => {
  it('renders nothing for absent, zero and negative counts', () => {
    expect(normalizeNavBadge(undefined)).toBeNull()
    expect(normalizeNavBadge(null)).toBeNull()
    expect(normalizeNavBadge(0)).toBeNull()
    expect(normalizeNavBadge(-3)).toBeNull()
    expect(normalizeNavBadge(Number.NaN)).toBeNull()
  })

  it('renders nothing for blank strings', () => {
    expect(normalizeNavBadge('')).toBeNull()
    expect(normalizeNavBadge('   ')).toBeNull()
  })

  it('paints positive counts verbatim', () => {
    expect(normalizeNavBadge(1)).toBe('1')
    expect(normalizeNavBadge(NAV_BADGE_MAX)).toBe('99')
  })

  it('caps counts above the max, matching THeaderBell', () => {
    expect(NAV_BADGE_MAX).toBe(99)
    expect(normalizeNavBadge(100)).toBe('99+')
    expect(normalizeNavBadge(4321)).toBe('99+')
  })

  it('passes short strings through untouched - a marker is not a count', () => {
    expect(normalizeNavBadge('NEW')).toBe('NEW')
    expect(normalizeNavBadge('  !  ')).toBe('!')
    // Not capped: a string is a label, not a number that ran past 99.
    expect(normalizeNavBadge('123456')).toBe('123456')
  })
})
