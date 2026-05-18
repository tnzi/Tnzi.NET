/**
 * AI Chat — full chat interface components.
 *
 * Primary entry point: `TChatApp` — production-grade chat application
 * shell with Manus-inspired design. Compose data via props/events and
 * override visuals via slots. See `TChatApp.vue` JSDoc for the API.
 *
 * The legacy `ChatLayout` family is being removed; use `TChatApp` for
 * any new code.
 */

export { default as TChatApp } from './TChatApp.vue';
export type { ThreadItem, NavItem, ThemePref, LandingChip } from './TChatApp.vue';
