<template>
  <!--
    TTabsPage — the batteries-included container for a tabbed content page.

    A page with primary tabs (each tab = a table list, a card grid, a status
    board, a report…) declares its tabs once via `:sections` and drops each
    tab's content into a same-named slot. Everything that used to be hand-written
    boilerplate is owned here:
      • the white page-header chrome (title / icon / help / #actions) — via TContentPage
      • the standard NTabs (line, medium, animated) + the `t-table-tabs` surface
        (white fill, border, radius, residual-height fill, pinned nav)
      • the required per-pane `t-table-tabs__pane` wrapper (flex-column fill)
      • `?section=` deep-linking + Back/Forward + the app-level kill switch
        (via useSectionRoute — the consumer does not wire this)
      • card-in-a-card flattening when a tab embeds a TCrudPage / TCardPage
        (the list-card's doubled surface/inset is dropped — see polish.css)

    Consumer shape:
      <TTabsPage :title icon :help :translate :sections="[
        { name: 'servers',  label: t('tabs.servers') },
        { name: 'status',   label: t('tabs.status'), scroll: true },
        { name: 'analytics', label: t('tabs.analytics') },
      ]" default-section="servers">
        <template #actions="{ active }">…header search, per-tab…</template>
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
  >
    <template v-if="$slots.title" #title><slot name="title" /></template>
    <template v-if="$slots.actions" #actions>
      <slot name="actions" :active="active" />
    </template>
    <template v-if="$slots.extra" #extra><slot name="extra" /></template>

    <!-- Cross-tab band ABOVE the tab surface (e.g. a KPI strip that spans every
         tab). Sits between the page header and the tabs, flush on the canvas —
         mirrors TListShell's `#kpis`. Use this (not `#extra`) for content that
         belongs above the tabs rather than inside the header card. -->
    <div v-if="$slots.kpis" class="t-tabs-page__kpis"><slot name="kpis" /></div>

    <NTabs
      :value="active"
      type="line"
      animated
      class="t-table-tabs"
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
        <div class="t-table-tabs__pane" :class="{ 't-table-tabs__pane--scroll': s.scroll }">
          <slot :name="s.name" :active="active" />
        </div>
      </NTabPane>
    </NTabs>

    <!-- Tab-independent overlays (drawers / dialogs). They teleport to <body>,
         so their position in the tree is irrelevant — this slot just gives the
         page one tidy place to declare them. -->
    <slot name="overlays" />
  </TContentPage>
</template>

<script setup lang="ts">
import { watch } from 'vue'
import { NTabPane, NTabs } from 'naive-ui'
import TContentPage from './TContentPage.vue'
import { useSectionRoute } from '../../headless/useSectionRoute'

/** One primary tab. `label` is the (already-translated) tab title; `scroll`
 *  makes the pane own its vertical scroll (mixed variable-height content like
 *  reports/status boards) — leave it off for a single flex-height table/cards. */
export interface TabSection {
  name: string
  label?: string
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
    /** Optional `v-model:section` — lets the page read/programmatically drive
     *  the active tab. Deep-linking is owned internally regardless. */
    section?: string
    /** Enable `?section=` deep-linking. Default true. */
    deepLink?: boolean
    title?: string
    icon?: string
    help?: string
    helpTitle?: string
    back?: boolean | string
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
watch(active, (v) => emit('update:section', v))
watch(
  () => props.section,
  (v) => {
    if (v != null && v !== active.value) active.value = v
  },
)

function onTab(v: string | number): void {
  active.value = String(v)
}

function tabLabel(s: TabSection): string {
  return s.label ?? (props.translate ? props.translate(s.name) : s.name)
}
</script>

<style scoped>
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
