import { defineComponent } from 'vue'
import { useDialog, useLoadingBar, useMessage, useNotification } from 'naive-ui'

/**
 * Internal renderless component that publishes the Naive UI provider APIs
 * as the `window.$message` / `$dialog` / `$notification` / `$loadingBar`
 * global handles.
 *
 * Rendered by `TAdminAppRoot` INSIDE its provider stack (the naive
 * `useXxx()` composables must run under the corresponding providers), so
 * every app that mounts `TAdminAppRoot` gets working global feedback for
 * free - `useCrudPage`'s default error toast and the window-handle
 * adapters `createTnziUiAdmin` registers (`adapters/`) both read these
 * handles. Apps NOT using `TAdminAppRoot` must still mount the
 * providers and register the handles from their own setup component.
 *
 * Plain assignment on setup: if an application registers its own handles
 * from a component deeper in the tree, that setup runs later and wins;
 * the instances are equivalent either way (same provider stack).
 */
export default defineComponent({
  name: 'TAdminWindowHandles',
  setup() {
    const message = useMessage()
    const dialog = useDialog()
    const notification = useNotification()
    const loadingBar = useLoadingBar()

    // SSR-safe: only touch window in a browser context. Assigned via a local
    // shape: naive's API types are wider than the `Window` augmentation in
    // `types/global.d.ts`.
    if (typeof window !== 'undefined') {
      const w = window as unknown as {
        $message?: unknown
        $dialog?: unknown
        $notification?: unknown
        $loadingBar?: unknown
      }
      w.$message = message
      w.$dialog = dialog
      w.$notification = notification
      w.$loadingBar = loadingBar
    }

    return () => null
  },
})
