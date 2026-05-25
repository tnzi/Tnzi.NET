/**
 * `useWidgetData()` — one-liner data hook for business widgets.
 *
 * Wraps the boilerplate every Workbench widget needs:
 *   - Fire `loader()` on mount.
 *   - Surface busy state via `WidgetContext.setBusy`.
 *   - Surface errors via `WidgetContext.reportError`.
 *   - Re-run the loader when the user clicks the card's refresh button.
 *   - Cleanly dispose the refresh listener on unmount.
 *
 * Usage:
 * ```ts
 * const data = ref<UsageSummary | null>(null)
 * useWidgetData(async () => {
 *   data.value = await bridge.usage.summary({ pageIndex: 1, pageSize: 1 })
 * })
 * ```
 */
import { onMounted, onBeforeUnmount } from 'vue'
import { useWidget } from './useWidget'

export function useWidgetData(loader: () => Promise<void>): {
  reload: () => Promise<void>
} {
  const widget = useWidget()

  async function run(): Promise<void> {
    widget.setBusy(true)
    try {
      await loader()
    } catch (err) {
      widget.reportError(err)
    } finally {
      widget.setBusy(false)
    }
  }

  onMounted(() => {
    void run()
  })

  const stop = widget.onRefresh(run)
  onBeforeUnmount(stop)

  return { reload: run }
}
