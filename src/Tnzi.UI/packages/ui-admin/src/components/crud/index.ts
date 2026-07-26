export { default as TCrudColumnSetting } from './TCrudColumnSetting.vue'
export { default as TFormModal } from './TFormModal.vue'
export { default as TCrudPage } from './TCrudPage.vue'
export { default as TRowActions } from './TRowActions.vue'
// 0.2.72+ (B5): TCrudSearch was sunk out of TCrudPage so it can be used
// stand-alone (e.g. a custom list shell that wants the simple/advanced
// search panel without the data table chrome).
export { default as TCrudSearch } from './TCrudSearch.vue'
export type { SearchableState } from './TCrudSearch.vue'
export { default as TListShell } from './TListShell.vue'
export type { TListShellProps } from './TListShell.vue'
export { default as TCardPage } from './TCardPage.vue'
export { default as TCardRenderer } from './renderers/TCardRenderer.vue'
export { default as TTableRenderer } from './renderers/TTableRenderer.vue'
// Third list shape: full-width document rows (see TItemCard). Same shell, so a
// page moves between table / tile grid / row list in one line.
export { default as TItemPage } from './TItemPage.vue'
export { default as TItemRenderer } from './renderers/TItemRenderer.vue'
export { default as TCrudSearchDrawer } from './TCrudSearchDrawer.vue'
