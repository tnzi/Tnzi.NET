/**
 * `useWidget` re-export. Sunk to `@tnzi/ui` in 0.2.x; admin call-sites
 * keep their existing import path through this barrel.
 */
export { useWidget, WIDGET_CONTEXT_KEY } from '@tnzi/ui'
export type { WidgetContext } from '@tnzi/ui'
