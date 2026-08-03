/**
 * `@tnzi/ui-ai/chat` - the drop-in chat product shell.
 *
 * A dedicated build entry rather than part of `./components`: `TChatApp` plus
 * the parts it composes is ~82 kB gzipped, while the full component barrel is
 * ~183 kB. A consumer building a chat product should not pay for the workflow,
 * knowledge and skill domains it never renders.
 *
 * This is a **file, not a directory**, on purpose. It used to be `src/chat/`
 * holding `index.ts` + `TChatApp.vue` and nothing else, so the package had two
 * folders named `chat` (this one and `components/chat/`, which holds the 20-odd
 * parts `TChatApp` is assembled from). The component now lives with its parts;
 * this file keeps the subpath, so consumers are unaffected.
 *
 * Types come from wherever they are declared rather than being funnelled
 * through `TChatApp.vue`: forwarding them made the same name reachable twice
 * once the components stopped being split across two directories.
 */
export { default as TChatApp } from './components/chat/TChatApp.vue';
export type { ThemePref, ChatAppView, BuiltinViewId } from './components/chat/TChatApp.vue';
export type { ThreadItem } from './components/chat/TThreadList.vue';
export type { LandingChip } from './components/chat/TLandingPage.vue';
export type { NavItem, NavGroup } from './components/layout/TSidebarNav.vue';
export type { UserMenuItem, UserBarAction } from './components/overlay/TUserMenu.vue';
