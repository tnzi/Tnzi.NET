<template>
  <!-- page mode: the host IS the route page; render the layout bare. -->
  <TDetailLayout
    v-if="state.mode.value === 'page'"
    :layout="layout"
    :sections="sections"
    :title="resolvedTitle"
    :icon="icon"
    :back="back"
    :content-max-width="contentMaxWidth"
    :active-section="state.activeSection.value"
    :translate="translate"
    @update:active-section="state.setSection"
  >
    <template v-if="$slots.title" #title><slot name="title" :data="state.data.value" /></template>
    <template v-if="$slots.actions" #actions><slot name="actions" :data="state.data.value" :action="state.action.value" /></template>
    <template v-if="$slots['nav-header']" #nav-header><slot name="nav-header" /></template>
    <template v-if="$slots.footer" #footer><slot name="footer" :submit="state.submit" :close="state.close" /></template>
    <template #default="{ section, sectionIcon }">
      <slot :data="state.data.value" :action="state.action.value" :section="section" :section-icon="sectionIcon" />
    </template>
  </TDetailLayout>

  <!-- drawer/modal: the overlay owns the title + close + footer actions; the in-layout header is suppressed. #title/#actions header slots apply to page mode. -->

  <!-- drawer mode -->
  <TDrawerShell
    v-else-if="state.mode.value === 'drawer'"
    :show="state.visible.value"
    :width="width"
    :title="resolvedTitle"
    @update:show="(v: boolean) => { if (!v) state.close() }"
  >
    <TDetailLayout
      :layout="layout"
      :sections="sections"
      :active-section="state.activeSection.value"
      :translate="translate"
      :show-header="false"
      @update:active-section="state.setSection"
    >
      <template #default="{ section, sectionIcon }">
        <slot :data="state.data.value" :action="state.action.value" :section="section" :section-icon="sectionIcon" />
      </template>
    </TDetailLayout>
    <template v-if="footer" #footer>
      <slot name="footer" :submit="state.submit" :close="state.close">
        <NButton @click="state.close">{{ t('admin.common.cancel') }}</NButton>
        <NButton v-if="state.action.value !== 'view'" type="primary" @click="state.submit">{{ t('admin.common.confirm') }}</NButton>
      </slot>
    </template>
  </TDrawerShell>

  <!-- modal mode (default) -->
  <TModalShell
    v-else
    :show="state.visible.value"
    :title="resolvedTitle"
    :width="width"
    @update:show="(v: boolean) => { if (!v) state.close() }"
  >
    <TDetailLayout
      :layout="layout"
      :sections="sections"
      :active-section="state.activeSection.value"
      :translate="translate"
      :show-header="false"
      @update:active-section="state.setSection"
    >
      <template #default="{ section, sectionIcon }">
        <slot :data="state.data.value" :action="state.action.value" :section="section" :section-icon="sectionIcon" />
      </template>
    </TDetailLayout>
    <template v-if="footer" #footer>
      <slot name="footer" :submit="state.submit" :close="state.close">
        <div class="t-detail-host__footer">
          <NButton @click="state.close">{{ t('admin.common.cancel') }}</NButton>
          <NButton v-if="state.action.value !== 'view'" type="primary" @click="state.submit">{{ t('admin.common.confirm') }}</NButton>
        </div>
      </slot>
    </template>
  </TModalShell>
</template>

<script setup lang="ts" generic="T">
import { computed } from 'vue'
import { NButton } from 'naive-ui'
import TModalShell from '../overlay/TModalShell.vue'
import TDrawerShell from '../overlay/TDrawerShell.vue'
import TDetailLayout from './TDetailLayout.vue'
import type { UseDetailReturn, DetailSection, DetailLayout } from '../../headless/useDetail'

export interface TDetailHostProps<T> {
  state: UseDetailReturn<T>
  title?: string
  width?: number
  layout?: DetailLayout
  sections?: DetailSection[]
  translate?: (key: string) => string
  /**
   * Render the modal/drawer footer (Cancel/Confirm or the `#footer` slot). Set
   * `false` for a footer-less management panel (e.g. a documents drawer) whose
   * own controls live in the body and whose close affordance is the X button.
   * (Page mode renders the footer only when a `#footer` slot is supplied.)
   */
  footer?: boolean
  /** Page-mode header icon (forwarded to the in-layout `TPageHeader`). */
  icon?: string
  /**
   * Page-mode back affordance: `true` → `router.back()`, a string → push that
   * path, `{ fallback }` → smart back (in-app history, else push `fallback` -
   * preferred for a drilled-into detail), `false` → no back button (a top-level
   * page reached from the menu). Default `true`. Ignored in modal/drawer mode.
   */
  back?: boolean | string | { fallback?: string }
  /** Page-mode content max-width (forwarded to `TDetailLayout`). */
  contentMaxWidth?: number | string
}

const props = withDefaults(defineProps<TDetailHostProps<T>>(), {
  title: undefined,
  width: 560,
  layout: 'plain',
  sections: () => [],
  translate: undefined,
  footer: true,
  icon: undefined,
  back: true,
  contentMaxWidth: undefined,
})

defineSlots<{
  default?: (props: { data: T | null; action: string | null; section: string | null; sectionIcon?: string }) => unknown
  title?: (props: { data: T | null }) => unknown
  actions?: (props: { data: T | null; action: string | null }) => unknown
  /** Page-mode only: rendered inside the side-layout nav card, above the menu (forwarded to TDetailLayout). */
  'nav-header'?: () => unknown
  footer?: (props: { submit: () => Promise<void>; close: () => void }) => unknown
}>()

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

const resolvedTitle = computed(() => {
  if (props.title) return props.title
  const a = props.state.action.value
  return a ? t(`admin.crud.${a}Title`) : ''
})
</script>

<style scoped>
.t-detail-host__footer { display: flex; justify-content: flex-end; gap: 8px; }
</style>
