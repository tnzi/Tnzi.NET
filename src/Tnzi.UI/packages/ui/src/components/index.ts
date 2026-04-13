/**
 * @tnzi/ui/components
 *
 * Tnzi T* components based on Naive UI.
 */

export * from './layout'
export * from './auth'
export * from './form'
export * from './form/fields'
export * from './data'
export * from './feedback'
export * from './control'
export * from './navigation'
export * from './card'
export * from './list'
export * from './icon'

// CRUD components (deprecated — moving to @tnzi/ui-admin in Phase 2)
/** @deprecated Moving to @tnzi/ui-admin (headless-driven TCrudPage). Removed in Phase 2 of 2026-04-12 refactor. */
export { default as TCrudPage } from './crud/TCrudPage.vue'
