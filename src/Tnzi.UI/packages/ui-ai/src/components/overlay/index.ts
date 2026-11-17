/**
 * Overlay components - floating surfaces layered above the page.
 *
 * A *structural* domain, not a business one. `TPopoverMenu` in particular has
 * nothing to do with chat; it is the generic "click a trigger, get a small
 * list of actions" container. It lived under `components/chat/` only because
 * the directory tree had no structural domain to put it in.
 */

export { default as TPopoverMenu } from './TPopoverMenu.vue';
/* The same menu, not floating: a window or a column that is itself a menu. */
export { default as TMenuPanel } from './TMenuPanel.vue';
/* A trigger plus its menu, and the entries to put in it (any TPopoverMenu takes them). */
export { default as TDropdownMenu } from './TDropdownMenu.vue';
export { default as TMenuItem } from './TMenuItem.vue';
export { default as TMenuSeparator } from './TMenuSeparator.vue';
/* A menu opened where the pointer is (a right click), and a floating panel of
   content that is not a menu. */
export { default as TContextMenu } from './TContextMenu.vue';
export { default as TPopover } from './TPopover.vue';
export { default as TActionMenu } from './TActionMenu.vue';
export type { ActionMenuItem } from './TActionMenu.vue';
export { default as TUserMenu } from './TUserMenu.vue';
export type { UserMenuItem } from './TUserMenu.vue';

/* Folded in from the former `src/shell/` on 2026-08-02: both are dialogs
   layered over the page, which is what this domain is for. */
export { default as TCommandPalette } from './TCommandPalette.vue';
export { default as TSettingsDialog } from './TSettingsDialog.vue';

/* The general dialog. Its chrome (backdrop, panel, header, close button, body,
   footer) is the global `dialog.css`, which the two dialogs above share. */
export { default as TDialog } from './TDialog.vue';
export { default as TImageViewer } from './TImageViewer.vue';
/* A non-modal window docked in a corner (a mail being written). */
export { default as TDockedDialog } from './TDockedDialog.vue';
export type { DialogSize, DialogNarrowLayout } from './dialog';
export type { DialogCloseReason, DialogOrigin, DialogOriginBox, DialogOriginSource } from './dialog-motion';
export type { UserBarAction } from './TUserMenu.vue';

/* Asking before something that cannot be undone: `useConfirm()` and the one
   dialog that shows it. */
export { default as TConfirmHost } from './TConfirmHost.vue';
export { useConfirm, createConfirmer } from './confirm';
export type { ConfirmFn, ConfirmOptions, ConfirmRequest, Confirmer } from './confirm';
