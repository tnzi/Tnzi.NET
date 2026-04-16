/**
 * @tnzi/ui-ai/shell — shell components for composing a chat application
 *
 * All exports marked @experimental — surface may iterate until 0.3.0.
 * Components are slot-driven chrome; state is owned by consumer composables.
 */
export { default as TCollapsibleSidebar } from './TCollapsibleSidebar.vue'
export { default as TCommandPalette } from './TCommandPalette.vue'
export { default as TSettingsDialog } from './TSettingsDialog.vue'
export { default as TLandingPage } from './TLandingPage.vue'

export {
  useSidebarState,
  type SidebarMode,
  type UseSidebarStateOptions,
  type UseSidebarStateReturn,
} from '../composables/useSidebarState'
export {
  useCommandPalette,
  type CommandAction,
  type UseCommandPaletteOptions,
  type UseCommandPaletteReturn,
} from '../composables/useCommandPalette'
export {
  useSettingsDialog,
  type SettingsSection,
  type UseSettingsDialogOptions,
  type UseSettingsDialogReturn,
} from '../composables/useSettingsDialog'
