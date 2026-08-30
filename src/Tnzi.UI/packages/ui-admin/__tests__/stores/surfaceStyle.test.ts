import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { nextTick } from 'vue'
import { setActivePinia, createPinia } from 'pinia'
import { useAdminThemeStore } from '../../src/stores/useAdminThemeStore'

/**
 * The container-chrome setting. What is worth locking here is not "the store
 * holds a string" but the properties that make it a design language rather
 * than a handful of independent knobs:
 *
 *   1. The DEFAULT clears the overrides instead of writing values inline.
 *      Inline `:root` values outrank the stylesheet's `.dark` block, so a
 *      default written inline would pin every card to whichever mode's values
 *      happened to be baked in.
 *   2. `elevated` therefore switches to its shadows BY REFERENCE - those DO
 *      vary with the mode, and a literal could only be right in one of them.
 *   3. Switching between the non-default styles leaves nothing behind, so
 *      `flat` never inherits `elevated`'s shadow.
 *   4. Border and shadow are always written as a pair.
 */
describe('theme store - surfaceStyle', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
  })
  afterEach(() => {
    document.documentElement.removeAttribute('style')
  })

  const read = (name: string) => document.documentElement.style.getPropertyValue(name)

  it('defaults to outlined and writes no overrides', async () => {
    const store = useAdminThemeStore()
    await nextTick()
    expect(store.surfaceStyle).toBe('outlined')
    expect(read('--tnzi-surface-card-border')).toBe('')
    expect(read('--tnzi-surface-card-shadow')).toBe('')
    expect(read('--tnzi-surface-card-shadow-hover')).toBe('')
  })

  it('elevated switches to the mode-varying shadows by reference', async () => {
    // A literal here would be correct in exactly one of light and dark.
    const store = useAdminThemeStore()
    store.setSurfaceStyle('elevated')
    await nextTick()
    expect(read('--tnzi-surface-card-border')).toBe('none')
    expect(read('--tnzi-surface-card-shadow')).toContain('var(--tnzi-surface-card-shadow-raised)')
    expect(read('--tnzi-surface-card-shadow-hover')).toContain('raised-hover')
    // No border means the card's own material is the separating signal, and a
    // card nested in a card shares it.
    expect(read('--tnzi-surface-card-step')).not.toBe('0%')
  })

  it('returning to outlined clears every override it can own', async () => {
    const store = useAdminThemeStore()
    store.setSurfaceStyle('flat')
    await nextTick()
    store.setSurfaceStyle('outlined')
    await nextTick()
    for (const name of [
      '--tnzi-surface-card-border',
      '--tnzi-surface-card-shadow',
      '--tnzi-surface-card-shadow-hover',
      '--tnzi-surface-card-step',
      '--tnzi-surface-inset-border',
      '--tnzi-surface-chrome-border',
      '--tnzi-surface-chrome-shadow-header',
    ]) {
      expect(read(name), name).toBe('')
    }
  })

  it('flat drops both halves and leans on the material step instead', async () => {
    const store = useAdminThemeStore()
    store.setSurfaceStyle('flat')
    await nextTick()
    expect(read('--tnzi-surface-card-border')).toBe('none')
    expect(read('--tnzi-surface-card-shadow')).toBe('none')
    // Material is the only channel left, so the nested step has to be non-zero
    // here or a card inside a card has no edge of any kind.
    expect(read('--tnzi-surface-card-step')).not.toBe('0%')
    expect(read('--tnzi-surface-card-step')).not.toBe('')
    // Hover is still the shared accent ring - a style with no shadow cannot
    // cue hover with a shadow, and it must not cue it with a translate.
    expect(read('--tnzi-surface-card-shadow-hover')).toContain('--tnzi-primary-rgb')
  })

  it('elevated -> flat leaves no elevated-only token behind', async () => {
    const store = useAdminThemeStore()
    store.setSurfaceStyle('elevated')
    await nextTick()
    expect(read('--tnzi-surface-chrome-shadow-header')).toContain('var(')

    store.setSurfaceStyle('flat')
    await nextTick()
    expect(read('--tnzi-surface-chrome-shadow-header')).toBe('none')
  })

  it('rejects an unknown style', () => {
    const store = useAdminThemeStore()
    store.setSurfaceStyle('elevated')
    // @ts-expect-error - testing the runtime guard
    store.setSurfaceStyle('embossed')
    expect(store.surfaceStyle).toBe('elevated')
  })

  it('reset() returns the style to the default', async () => {
    const store = useAdminThemeStore()
    store.setSurfaceStyle('flat')
    await nextTick()
    store.reset()
    await nextTick()
    expect(store.surfaceStyle).toBe('outlined')
    expect(read('--tnzi-surface-card-shadow')).toBe('')
  })
})
