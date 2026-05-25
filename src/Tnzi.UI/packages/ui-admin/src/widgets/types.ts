/**
 * Widget protocol re-export.
 *
 * Sunk to `@tnzi/ui/components/layout/widget-types` in 0.2.x. Admin
 * call-sites keep their `import type { WidgetDef } from '../widgets/types'`
 * paths through this barrel; new code should import directly from
 * `@tnzi/ui`:
 *
 *   import type { WidgetDef, WidgetContext, WorkbenchConfig, SpanValue } from '@tnzi/ui'
 */
export {
  WIDGET_CONTEXT_KEY,
  type WidgetContext,
  type WidgetDef,
  type WorkbenchConfig,
  type SpanValue,
} from '@tnzi/ui'
