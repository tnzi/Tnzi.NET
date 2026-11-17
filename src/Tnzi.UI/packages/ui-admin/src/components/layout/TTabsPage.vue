<template>
  <!--
    TTabsPage - the batteries-included container for a tabbed content page.

    A page with primary tabs (each tab = a table list, a card grid, a status
    board, a report…) declares its tabs once via `:sections` and drops each
    tab's content into a same-named slot. Everything that used to be hand-written
    boilerplate is owned here:
      • the white page-header chrome (title / icon / help / #actions) - via TContentPage
      • the standard NTabs (line, medium, animated) + the `t-table-tabs` surface
        (white fill, border, radius, residual-height fill, pinned nav)
      • the required per-pane `t-table-tabs__pane` wrapper (flex-column fill)
      • `?section=` deep-linking + Back/Forward + the app-level kill switch
        (via useSectionRoute - the consumer does not wire this)
      • card-in-a-card flattening when a tab embeds a TCrudPage / TCardPage
        (the list-card's doubled surface/inset is dropped - see polish.css)
      • the active tab's list search in THIS header bar: a header-less list
        that is the only searchable list of its pane teleports its keyword
        search + Advanced into the header's action column (phones: the 🔍 /
        Advanced toggles, the form expanding under the bar), so search sits in
        the header and the list toolbar keeps only filters and list actions
        (see ./tabs-page-search.ts)

    Consumer shape:
      <TTabsPage :title icon :help :translate :sections="[
        { name: 'servers',  label: t('tabs.servers') },
        { name: 'status',   label: t('tabs.status'), scroll: true },
        { name: 'analytics', label: t('tabs.analytics') },
      ]" default-section="servers">
        <template #actions="{ active }">…per-tab header actions…</template>
        <template #servers><TCardPage … :show-header="false" /></template>
        <template #status>…mixed scrollable content…</template>
        <template #analytics><TResponsiveTable … /></template>
        <template #overlays><NDrawer …/></template>  ← tab-independent teleports
      </TTabsPage>
  -->
  <TContentPage
    :title="title"
    :icon="icon"
    :help="help"
    :help-title="helpTitle"
    :back="back"
    :translate="translate"
    scroll="fill"
    :inline-actions="isSm && activeHasSearch"
  >
    <template v-if="$slots.title" #title><slot name="title" /></template>
    <template v-if="$slots.actions || activeHasSearch" #actions>
      <!-- The active tab's list teleports its search cluster here, ahead of the
           page's own header actions. `display: contents` keeps its members
           flex items of the action column. -->
      <div v-if="activeHasSearch" ref="searchActionsEl" class="t-tabs-page__search" />
      <slot name="actions" :active="active" />
    </template>
    <template v-if="activeHasSearch && isSm" #below>
      <!-- Phone: the list's search panel expands here, inside the header card. -->
      <div ref="searchPanelEl" class="t-tabs-page__search-panel" />
    </template>
    <template v-if="$slots.extra" #extra><slot name="extra" /></template>

    <!-- Cross-tab band ABOVE the tab surface (e.g. a KPI strip that spans every
         tab). Sits between the page header and the tabs, flush on the canvas -
         mirrors TListShell's `#kpis`. Use this (not `#extra`) for content that
         belongs above the tabs rather than inside the header card. -->
    <div v-if="$slots.kpis" class="t-tabs-page__kpis"><slot name="kpis" /></div>

    <NTabs
      ref="tabsRef"
      :value="active"
      type="line"
      animated
      class="t-table-tabs t-surface-card"
      @update:value="onTab"
    >
      <NTabPane
        v-for="s in sections"
        :key="s.name"
        :name="s.name"
        :tab="tabLabel(s)"
        :disabled="s.disabled"
        :display-directive="s.displayDirective ?? 'if'"
      >
        <TTabsPagePane :context="paneContext(s.name)" :scroll="s.scroll">
          <slot :name="s.name" :active="active" />
        </TTabsPagePane>
      </NTabPane>
    </NTabs>

    <!-- Tab-independent overlays (drawers / dialogs). They teleport to <body>,
         so their position in the tree is irrelevant - this slot just gives the
         page one tidy place to declare them. -->
    <slot name="overlays" />
  </TContentPage>
</template>

<script setup lang="ts">
import { computed, ref, shallowRef, watch, type VNode, type VNodeChild } from 'vue'
import { NTabPane, NTabs } from 'naive-ui'
import TContentPage from './TContentPage.vue'
import TTabsPagePane from './TTabsPagePane.vue'
import type { TabsPagePaneContext } from './tabs-page-search'
import { useBreakpoint } from '../../headless/useBreakpoint'
import type { BackTarget } from './back-target'
import { useSectionRoute } from '../../headless/useSectionRoute'
import { useTabBarSync, type TabBarSyncTarget } from '../../headless/useTabBarSync'

/** One primary tab. `label` is the (already-translated) tab title; `scroll`
 *  makes the pane own its vertical scroll (mixed variable-height content like
 *  reports/status boards) - leave it off for a single flex-height table/cards. */
export interface TabSection {
  name: string
  /**
   * The (already-translated) tab title. A plain string covers the common case;
   * a `VNode` or a render function is forwarded verbatim to `NTabPane`'s `tab`
   * prop (whose own type is `string | number | VNode | (() => VNodeChild)`),
   * for a tab that needs a badge / icon / count alongside its text. Omit it and
   * the section name is run through `translate`.
   */
  label?: string | VNode | (() => VNodeChild)
  scroll?: boolean
  disabled?: boolean
  /** naive display strategy. Default `'if'` (destroy on leave); `'show'` keeps
   *  every pane mounted (state survives tab switches). */
  displayDirective?: 'if' | 'show'
}

const props = withDefaults(
  defineProps<{
    sections: TabSection[]
    /** Section to land on when the URL carries none. Defaults to the first. */
    defaultSection?: string
    /** Optional `v-model:section` - lets the page read/programmatically drive
     *  the active tab. Deep-linking is owned internally regardless. */
    section?: string
    /** Enable `?section=` deep-linking. Default true. */
    deepLink?: boolean
    title?: string
    icon?: string
    help?: string
    helpTitle?: string
    /** Back affordance; `{ fallback }` = smart back (preferred for a drilled-into page). */
  back?: BackTarget
    translate?: (key: string) => string
  }>(),
  {
    defaultSection: undefined,
    section: undefined,
    deepLink: true,
    title: undefined,
    icon: undefined,
    help: undefined,
    helpTitle: undefined,
    back: undefined,
    translate: undefined,
  },
)

const emit = defineEmits<{ 'update:section': [value: string] }>()

// The container owns section state + deep-linking. A static non-null
// defaultSection resolves useSectionRoute's `Ref<string>` overload.
const active = useSectionRoute({
  sections: () => props.sections.map((s) => s.name),
  defaultSection: props.defaultSection ?? props.sections[0]?.name ?? '',
  enabled: () => props.deepLink,
})

// Two-way `v-model:section`: mirror internal → out, and adopt external writes.
// `immediate` matters: the initial value is resolved from `?section=` (or
// `defaultSection`), and without emitting it the binding stays at whatever the
// page initialised its own ref to. A page deep-linked straight into a non-first
// tab would then render its cross-tab controls for the WRONG tab until the user
// happened to click one.
watch(active, (v) => emit('update:section', v), { immediate: true })
watch(
  () => props.section,
  (v) => {
    if (v != null && v !== active.value) active.value = v
  },
)

// A tab whose label carries an asynchronously-loaded count / status badge grows
// after mount and pushes every tab after it sideways, and naive only re-measures
// the underline on a value change or a nav WIDTH change - neither of which
// happens here. Without this, deep-linking into a tab that sits after the one
// that grew underlines the wrong tab.
const tabsRef = ref<TabBarSyncTarget | null>(null)
// The watch source is the tab SET, not `sections` itself: a label that merely
// changed size is the observer's job, and the rendered tab elements survive a
// new `sections` array as long as the names are the same (both this `v-for` and
// naive's panes key on `name`), so re-reading the strip then would be churn.
useTabBarSync(tabsRef, () => JSON.stringify(props.sections.map((s) => s.name)))

// ── Header search for the active tab's list (see ./tabs-page-search.ts) ──
const { isSm } = useBreakpoint()
// Section name → the searchable header-less lists registered in that pane, in
// registration order. Replaced, never mutated, so every reader re-evaluates.
const searchLists = ref<Readonly<Record<string, readonly symbol[]>>>({})
const searchActionsEl = shallowRef<HTMLElement | null>(null)
const searchPanelEl = shallowRef<HTMLElement | null>(null)

function soleSearchList(name: string): symbol | null {
  const tokens = searchLists.value[name] ?? []
  return tokens.length === 1 ? (tokens[0] ?? null) : null
}
/** The header shows a search only when the active pane has exactly one searchable list. */
const activeHasSearch = computed(() => soleSearchList(active.value) !== null)

// One context per section name, kept for the page's lifetime so a pane's
// provided value never changes identity under the lists that injected it.
const paneContexts = new Map<string, TabsPagePaneContext>()
function paneContext(name: string): TabsPagePaneContext {
  const existing = paneContexts.get(name)
  if (existing) return existing
  const context: TabsPagePaneContext = {
    name,
    isActive: () => active.value === name,
    register(token) {
      const tokens = searchLists.value[name] ?? []
      if (tokens.includes(token)) return
      searchLists.value = { ...searchLists.value, [name]: [...tokens, token] }
    },
    unregister(token) {
      const tokens = searchLists.value[name] ?? []
      if (!tokens.includes(token)) return
      searchLists.value = { ...searchLists.value, [name]: tokens.filter((t) => t !== token) }
    },
    owner: () => soleSearchList(name),
    actionsTarget: () => searchActionsEl.value,
    panelTarget: () => searchPanelEl.value,
  }
  paneContexts.set(name, context)
  return context
}

function onTab(v: string | number): void {
  active.value = String(v)
}

function tabLabel(s: TabSection): string | VNode | (() => VNodeChild) {
  return s.label ?? (props.translate ? props.translate(s.name) : s.name)
}
</script>

<style scoped>
/* Teleport targets for the active list's search: no box of their own, so the
   search members are flex items of the header's action column / header card
   and an empty target adds no gap. */
.t-tabs-page__search,
.t-tabs-page__search-panel {
  display: contents;
}
/* Cross-tab band keeps its natural height; the tab surface below it flex-fills.
   A flex column so a band with several stacked blocks (e.g. a filter card + an
   error banner) spaces them consistently; a single child is unaffected. */
.t-tabs-page__kpis {
  flex-shrink: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
</style>
