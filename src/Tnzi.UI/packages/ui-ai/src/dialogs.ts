/**
 * `@tnzi/ui-ai/dialogs` - the package's dialogs for a page built from `@tnzi/ui-ai/controls`:
 * `TDialog`, the confirmation (`useConfirm()` with the one `TConfirmHost` that shows it) and the
 * full-size image viewer (`TImageViewer`: a set of pictures, zoom, pan, keys).
 *
 * Their styles are in the controls stylesheet (`@tnzi/ui-ai/controls.css`) with the controls': the
 * shared dialog chrome (`dialog.css`), the dialogs' and the viewer's own and the tokens, so a page
 * of controls draws them without the whole `style.css`. A page that bundles its icons registers
 * `CONTROLS_ICON_NAMES`, which holds these components' icons too.
 *
 * Not part of the controls entry, which reaches only `vue` and `@iconify/vue`: a dialog, and the
 * viewer, is a layer of the page's overlay stack (Escape and Tab go to the topmost open overlay,
 * whichever package opened it; a viewer opened from a dialog closes first), and that stack is `useFocusTrap` from `@tnzi/ui/headless` over `@tnzi/core`'s
 * page-wide registry. Importing this entry brings those two subpaths (both are peers of this
 * package); a second, controls-only stack would answer Escape twice on a page that has both.
 *
 * The same components are also exported from `@tnzi/ui-ai/components`.
 *
 * @example
 * ```ts
 * import { TConfirmHost, TDialog, TImageViewer, useConfirm } from '@tnzi/ui-ai/dialogs'
 * import '@tnzi/ui-ai/controls.css'
 * ```
 */
export { default as TDialog } from './components/overlay/TDialog.vue';
export { default as TConfirmHost } from './components/overlay/TConfirmHost.vue';
export { useConfirm, createConfirmer } from './components/overlay/confirm';
export type { ConfirmFn, ConfirmOptions, ConfirmRequest, Confirmer } from './components/overlay/confirm';
export type { DialogSize, DialogNarrowLayout } from './components/overlay/dialog';
export type { DialogCloseReason, DialogOrigin, DialogOriginBox, DialogOriginSource } from './components/overlay/dialog-motion';
export { default as TImageViewer } from './components/overlay/TImageViewer.vue';
export type { ViewerImage } from './components/chat/image-group';
