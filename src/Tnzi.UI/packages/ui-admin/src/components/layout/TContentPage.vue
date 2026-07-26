<template>
  <div class="t-content-page" :class="`t-content-page--scroll-${scroll}`">
    <slot name="header">
      <TPageHeader
        v-if="renderHeader"
        :surface="headerSurface"
        :title="title"
        :icon="icon"
        :help="help"
        :help-title="helpTitle"
        :back="back"
        :translate="translate"
      >
        <template v-if="$slots.title" #title><slot name="title" /></template>
        <template v-if="$slots.actions" #actions><slot name="actions" /></template>
        <template v-if="$slots.extra" #extra><slot name="extra" /></template>
      </TPageHeader>
    </slot>
    <div class="t-content-page__body">
      <div v-if="card" class="t-content-page__card"><slot /></div>
      <slot v-else />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, useSlots } from 'vue'
import { useRoute, type RouteLocationNormalizedLoaded } from 'vue-router'
import TPageHeader from './TPageHeader.vue'
import type { BackTarget } from './backTarget'

interface Props {
  title?: string
  icon?: string
  help?: string
  helpTitle?: string
  /** Back affordance; `{ fallback }` = smart back (preferred for a drilled-into page). */
  back?: BackTarget
  /** Force header on/off. Default: auto - on when a title (prop/route) or any header slot exists. */
  showHeader?: boolean
  /** Render the header as a white surface card. Default true. */
  headerSurface?: boolean
  /** Wrap the body in a white surface card (for pages whose body is a single
      bare content block). Default false - pages with their own cards leave it off. */
  card?: boolean
  /** Body scroll behaviour. 'auto' = page scrolls (long-form); 'fill' = body flex-fills, inner element scrolls (tables); 'none' = no scroll mgmt. */
  scroll?: 'auto' | 'fill' | 'none'
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<Props>(), {
  title: undefined,
  icon: undefined,
  help: undefined,
  helpTitle: undefined,
  back: undefined,
  showHeader: undefined,
  headerSurface: true,
  card: false,
  scroll: 'auto',
  translate: undefined,
})

defineSlots<{
  header?: () => unknown
  title?: () => unknown
  actions?: () => unknown
  extra?: () => unknown
  default?: () => unknown
}>()

const slots = useSlots()
const route = useRoute() as RouteLocationNormalizedLoaded | undefined

const renderHeader = computed(() => {
  if (props.showHeader === false) return false
  if (props.showHeader === true) return true
  return !!(
    props.title ||
    props.back ||
    route?.meta?.title ||
    slots.title ||
    slots.actions ||
    slots.extra
  )
})
</script>

<style scoped>
.t-content-page {
  display: flex;
  flex-direction: column;
  gap: 12px;
  width: 100%;
  height: 100%;
  min-height: 0;
}
/* Body is a flex column with a 12px gap so stacked sections (KPI strip /
   progress / table-card / etc.) keep their vertical rhythm - this replaces
   the per-page `t-stack-page` gap the migrated pages used to rely on, so
   content no longer sticks to the header or to each other. */
.t-content-page__body {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.t-content-page--scroll-auto .t-content-page__body { overflow: auto; }
.t-content-page--scroll-fill .t-content-page__body { overflow: hidden; }
.t-content-page--scroll-none .t-content-page__body { overflow: visible; }

/* Optional white body surface (card). Use for pages whose body is a single
   bare content block (file browser, a lone table/timeline) so the body
   matches the white header instead of sitting on the transparent canvas.
   Pages that render their own cards (dashboards, multi-section) leave it off. */
.t-content-page__card {
  /* Follows the theme drawer's "Card / List" background
     (`--tnzi-admin-card-bg`) like every other content card; base container
     color when unset. Dark-card text flip is handled by polish.css
     (`[data-tnzi-card-tone]`). */
  background: var(--tnzi-admin-card-bg, var(--tnzi-container-bg, #fff));
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
  padding: 12px;
}
.t-content-page--scroll-fill .t-content-page__card {
  flex: 1 1 auto;
  min-height: 0;
  overflow: auto;
  display: flex;
  flex-direction: column;
}
</style>
