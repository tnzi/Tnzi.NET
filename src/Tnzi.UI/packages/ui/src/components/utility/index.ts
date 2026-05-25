/**
 * Utility widgets — generic UI controls reusable beyond admin shells
 * (theme/lang switchers, fullscreen toggle, reload button, sidebar togglers).
 *
 * Sunk from `@tnzi/ui-admin` in 0.2.71+ so site/chat/mobile can reuse them.
 */
export { default as TThemeSchemaSwitch } from './TThemeSchemaSwitch.vue'
export type { ThemeSchema } from './TThemeSchemaSwitch.vue'
export { default as TLangSwitch } from './TLangSwitch.vue'
export type { LangOption } from './TLangSwitch.vue'
export { default as TFullScreen } from './TFullScreen.vue'
export { default as TReloadButton } from './TReloadButton.vue'
export { default as TPinToggler } from './TPinToggler.vue'
export { default as TMenuToggler } from './TMenuToggler.vue'
