<template>
  <!-- One pane of a TTabsPage: the flex-column wrapper the tab surface needs,
       plus the per-pane context a header-less list registers its search with
       (see ./tabs-page-search.ts). Internal to TTabsPage, not public. -->
  <div class="t-table-tabs__pane" :class="{ 't-table-tabs__pane--scroll': scroll }">
    <slot />
  </div>
</template>

<script setup lang="ts">
import { provide } from 'vue'
import { TABS_PAGE_PANE, type TabsPagePaneContext } from './tabs-page-search'

const props = defineProps<{
  /** The pane's context, built by TTabsPage (which owns the registry and the header targets). */
  context: TabsPagePaneContext
  scroll?: boolean
}>()

provide(TABS_PAGE_PANE, props.context)
</script>
