<template>
  <!-- page mode: the host IS the route page; render the layout bare. -->
  <TDetailLayout
    v-if="state.mode.value === 'page'"
    :layout="layout"
    :sections="sections"
    :title="resolvedTitle"
    :back="true"
    :active-section="state.activeSection.value"
    :translate="translate"
    @update:active-section="state.setSection"
  >
    <template v-if="$slots.title" #title><slot name="title" :data="state.data.value" /></template>
    <template #actions><slot name="actions" :data="state.data.value" :action="state.action.value" /></template>
    <template #footer><slot name="footer" :submit="state.submit" :close="state.close" /></template>
    <template #default="{ section }">
      <slot :data="state.data.value" :action="state.action.value" :section="section" />
    </template>
  </TDetailLayout>

  <!-- drawer/modal: the overlay owns the title + close + footer actions; the in-layout header is suppressed. #title/#actions header slots apply to page mode. -->

  <!-- drawer mode -->
  <NDrawer
    v-else-if="state.mode.value === 'drawer'"
    :show="state.visible.value"
    :width="width"
    placement="right"
    @update:show="(v: boolean) => { if (!v) state.close() }"
  >
    <NDrawerContent :title="resolvedTitle" closable>
      <TDetailLayout
        :layout="layout"
        :sections="sections"
        :active-section="state.activeSection.value"
        :translate="translate"
        :show-header="false"
        @update:active-section="state.setSection"
      >
        <template #default="{ section }">
          <slot :data="state.data.value" :action="state.action.value" :section="section" />
        </template>
      </TDetailLayout>
      <template #footer>
        <slot name="footer" :submit="state.submit" :close="state.close">
          <NButton @click="state.close">{{ t('admin.common.cancel') }}</NButton>
          <NButton v-if="state.action.value !== 'view'" type="primary" @click="state.submit">{{ t('admin.common.confirm') }}</NButton>
        </slot>
      </template>
    </NDrawerContent>
  </NDrawer>

  <!-- modal mode (default) -->
  <NModal
    v-else
    :show="state.visible.value"
    preset="card"
    :title="resolvedTitle"
    :style="{ width: `min(${width}px, 95vw)` }"
    :mask-closable="false"
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
      <template #default="{ section }">
        <slot :data="state.data.value" :action="state.action.value" :section="section" />
      </template>
    </TDetailLayout>
    <template #footer>
      <slot name="footer" :submit="state.submit" :close="state.close">
        <div class="t-detail-host__footer">
          <NButton @click="state.close">{{ t('admin.common.cancel') }}</NButton>
          <NButton v-if="state.action.value !== 'view'" type="primary" @click="state.submit">{{ t('admin.common.confirm') }}</NButton>
        </div>
      </slot>
    </template>
  </NModal>
</template>

<script setup lang="ts" generic="T">
import { computed } from 'vue'
import { NModal, NDrawer, NDrawerContent, NButton } from 'naive-ui'
import TDetailLayout from './TDetailLayout.vue'
import type { UseDetailReturn, DetailSection, DetailLayout } from '../../headless/useDetail'

export interface TDetailHostProps<T> {
  state: UseDetailReturn<T>
  title?: string
  width?: number
  layout?: DetailLayout
  sections?: DetailSection[]
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<TDetailHostProps<T>>(), {
  title: undefined,
  width: 560,
  layout: 'plain',
  sections: () => [],
  translate: undefined,
})

defineSlots<{
  default?: (props: { data: T | null; action: string | null; section: string | null }) => unknown
  title?: (props: { data: T | null }) => unknown
  actions?: (props: { data: T | null; action: string | null }) => unknown
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
