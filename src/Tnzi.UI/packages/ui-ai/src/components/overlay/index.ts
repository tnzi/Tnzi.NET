/**
 * Overlay components - floating surfaces layered above the page.
 *
 * A *structural* domain, not a business one. `TPopoverMenu` in particular has
 * nothing to do with chat; it is the generic "click a trigger, get a small
 * list of actions" container. It lived under `components/chat/` only because
 * the directory tree had no structural domain to put it in.
 */

export { default as TPopoverMenu } from './TPopoverMenu.vue';
export { default as TUserMenu } from './TUserMenu.vue';
export type { UserMenuItem } from './TUserMenu.vue';

/* Folded in from the former `src/shell/` on 2026-08-02: both are dialogs
   layered over the page, which is what this domain is for. */
export { default as TCommandPalette } from './TCommandPalette.vue';
export { default as TSettingsDialog } from './TSettingsDialog.vue';
export type { UserBarAction } from './TUserMenu.vue';
