import type { InjectionKey } from 'vue'

/**
 * How a header-less list inside a `TTabsPage` pane puts its search in the
 * page's header bar instead of its own toolbar.
 *
 * The house rule is "search lives in the page header, filters live at the top
 * left of the list". A top-level list page gets that from its own header card;
 * a list embedded in a tab turns its header off (`showHeader=false`, because
 * the tabs page already owns the single title bar), and without this its search
 * would land in the list toolbar beside the filters.
 *
 * `TTabsPage` provides one context per pane. A `TListShell` that would have put
 * its search in its toolbar registers with the context of the pane it renders
 * in; when it is the ONLY such list in that pane it claims the header, and
 * while that pane is the active tab it teleports its search cluster into the
 * header's action column (and, on phones, its expanding panel under the bar).
 * Two or more searchable lists in one pane claim nothing: a single header
 * search box cannot say which of them it filters, so each keeps its own search
 * in its toolbar.
 *
 * Overlays (`TOverlayTheme`, hence every modal and drawer) provide `null`, so a
 * list inside a dialog opened from a tab never reaches for the page header
 * behind the dialog.
 */
export interface TabsPagePaneContext {
  /** The section name of the pane. */
  readonly name: string
  /** Whether this pane is the active tab. */
  isActive(): boolean
  register(token: symbol): void
  unregister(token: symbol): void
  /** The token of the pane's only searchable list, or null when it has none or several. */
  owner(): symbol | null
  /** Header action column element the owner teleports its search cluster into (null until rendered). */
  actionsTarget(): HTMLElement | null
  /** Element under the header bar the owner teleports its phone search panel into (null until rendered). */
  panelTarget(): HTMLElement | null
}

export const TABS_PAGE_PANE: InjectionKey<TabsPagePaneContext | null> = Symbol('tnzi-tabs-page-pane')
