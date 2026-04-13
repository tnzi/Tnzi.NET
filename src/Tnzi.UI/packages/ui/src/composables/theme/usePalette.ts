import { computed, type ComputedRef } from 'vue'
import type { ColorPalette, ColorRole } from '../../theme/types'
import { useTheme } from './useTheme'

/**
 * Access the current 11-level palette for a given color role.
 * Reactive: updates when the theme color changes.
 *
 * @example
 * const primary = usePalette('primary')
 * console.log(primary.value[500]) // base color
 * console.log(primary.value[100]) // light variant
 */
export function usePalette(role: ColorRole): ComputedRef<ColorPalette> {
  const { settings } = useTheme()
  return computed(() => settings.value.palettes[role])
}
