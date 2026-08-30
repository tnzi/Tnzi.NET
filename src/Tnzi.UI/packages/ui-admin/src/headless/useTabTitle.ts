import { watch, toValue, type MaybeRefOrGetter } from 'vue'
import { useRoute } from 'vue-router'
import { useAdminTabStore, isMultiInstanceRoute, multiInstanceKey } from '../stores/useAdminTabStore'
import { useAdminDesktopStore } from '../stores/useAdminDesktopStore'
import { useDesktopWindowId } from './desktop-window-context'

/**
 * Set the CURRENT tab's display title from a reactive source - typically the
 * loaded record's name on a detail page.
 *
 * Why it exists: multi-instance detail routes (`/agents/:id`) open one tab per
 * record, but every such tab still inherits the static route `meta.title` (e.g.
 * "Agent Detail"), so two open detail tabs are indistinguishable. Calling
 * `useTabTitle(() => agent.value?.name)` makes each tab show its own record name.
 *
 * The tab id is computed once at call time with the SAME `multiInstanceKey` the
 * tab store uses (param routes → `route.path`, query-agnostic; multiTab+query →
 * `fullPath`; otherwise route name) so the update targets THIS detail's tab and
 * survives volatile query changes like `?section=`. No-op (swallowed) when there
 * is no router / pinia (e.g. isolated unit tests).
 *
 * Under the `desktop` layout the call retargets to the enclosing window's title
 * bar - the same intent ("label this open thing with its record name") against
 * the surface that actually exists there. Callers need no branch.
 *
 * @param source ref / getter producing the desired tab title; falsy values are
 *   ignored (the tab keeps its previous title until a real name resolves).
 */
export function useTabTitle(source: MaybeRefOrGetter<string | null | undefined>): void {
  // Inside a desktop window the equivalent surface is that window's title bar,
  // not the tab bar (which the desktop layout does not render at all). Writing
  // to the tab store here would be worse than useless: two windows on the same
  // route derive the same tab id and would overwrite each other's title, and
  // switching back to a tabbed layout would show a row of phantom tabs for
  // windows the user never opened as tabs.
  const windowId = useDesktopWindowId()
  if (windowId) {
    let desktop: ReturnType<typeof useAdminDesktopStore>
    try {
      desktop = useAdminDesktopStore()
    } catch {
      return
    }
    watch(
      () => toValue(source),
      (title) => {
        if (title) desktop.setTitle(windowId, title)
      },
      { immediate: true },
    )
    return
  }

  let tabId: string
  let tabStore: ReturnType<typeof useAdminTabStore>
  try {
    const route = useRoute()
    tabId = isMultiInstanceRoute(route)
      ? multiInstanceKey(route)
      : (typeof route.name === 'string' ? route.name : route.fullPath)
    tabStore = useAdminTabStore()
  } catch {
    return
  }
  watch(
    () => toValue(source),
    (title) => {
      if (title) tabStore.updateTabTitle(tabId, title)
    },
    { immediate: true },
  )
}
