/**
 * `useWidget()` — read the surrounding `TWidgetCard`'s context from
 * inside a widget component. Returns a fallback no-op context when
 * called outside a card (e.g. unit-test mounting a widget standalone)
 * so widgets don't crash without a host.
 *
 * Sunk from `@tnzi/ui-admin/widgets/shell/useWidget.ts` in 0.2.x.
 */
import { inject } from 'vue'
import { WIDGET_CONTEXT_KEY, type WidgetContext } from '../../components/layout/widget-types'

export { WIDGET_CONTEXT_KEY }
export type { WidgetContext }

export function useWidget(): WidgetContext {
  const ctx = inject(WIDGET_CONTEXT_KEY, null)
  if (ctx) return ctx
  return {
    id: 'standalone',
    refresh: () => undefined,
    reportError: () => undefined,
    setBusy: () => undefined,
    onRefresh: () => () => undefined,
  }
}
