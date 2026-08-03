import { describe, it, expect, vi } from 'vitest'
import { ref } from 'vue'

const settingsRef = ref({
  palettes: {
    primary: { 50: '#fafafa', 100: '#f5f5f5', 500: '#3b82f6', 900: '#1e3a8a' },
    success: { 50: '#ecfdf5', 500: '#10b981', 900: '#064e3b' },
  },
})

vi.mock('../../../src/headless/theme/useTheme', () => ({
  useTheme: () => ({ settings: settingsRef }),
}))

import { usePalette } from '../../../src/headless/theme/usePalette'

describe('usePalette', () => {
  it('returns a reactive computed ref for the requested role', () => {
    const primary = usePalette('primary' as any)
    expect(primary.value[500]).toBe('#3b82f6')
    expect(primary.value[50]).toBe('#fafafa')
  })

  it('different roles return different palettes', () => {
    const primary = usePalette('primary' as any)
    const success = usePalette('success' as any)
    expect(primary.value[500]).toBe('#3b82f6')
    expect(success.value[500]).toBe('#10b981')
  })

  it('reflects updates to the underlying settings', () => {
    const primary = usePalette('primary' as any)
    expect(primary.value[500]).toBe('#3b82f6')
    settingsRef.value = {
      palettes: {
        primary: { 50: '#000', 100: '#111', 500: '#ff0000', 900: '#880000' },
        success: settingsRef.value.palettes.success,
      },
    }
    expect(primary.value[500]).toBe('#ff0000')
  })
})
