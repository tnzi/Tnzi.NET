import { useMessage, type MessageApi } from 'naive-ui'

/**
 * `useSafeMessage` - a `useMessage()` wrapper that returns a noop API
 * when no `NMessageProvider` is installed (e.g. unit test mounts that
 * skip the full naive-ui provider stack, or out-of-tree usage from a
 * detached effect scope).
 *
 * Centralises the try/catch pattern previously copy-pasted across 8+
 * admin pages. Always returns the same `MessageApi` shape Naive UI
 * exposes - callers can use any of `success / error / warning / info /
 * loading` without thinking about whether a provider is installed.
 *
 * Sunk from `@tnzi/ui-admin` in 0.2.x so site/chat/mobile etc. can reuse
 * the same safety wrapper.
 */
export function useSafeMessage(): MessageApi {
  try {
    return useMessage()
  } catch {
    const noop = (): { destroy: () => void } => ({ destroy: () => undefined })
    return {
      info: noop,
      success: noop,
      warning: noop,
      error: noop,
      loading: noop,
      create: noop,
      destroyAll: () => undefined,
    } as unknown as MessageApi
  }
}
