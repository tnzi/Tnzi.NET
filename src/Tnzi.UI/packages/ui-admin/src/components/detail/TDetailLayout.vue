<template>
  <div class="t-detail-layout" :class="`t-detail-layout--${layout}`" :style="rootStyle">
    <TPageHeader v-if="showHeader" surface :title="title" :icon="icon" :back="back" :translate="translate">
      <template v-if="$slots.title" #title><slot name="title" /></template>
      <template v-if="$slots.actions" #actions><slot name="actions" /></template>
    </TPageHeader>

    <!-- tabs: horizontal section nav above a single body -->
    <NTabs
      v-if="layout === 'tabs' && sections.length"
      :value="activeSection ?? undefined"
      type="line"
      class="t-detail-layout__tabs"
      @update:value="onSection"
    >
      <NTabPane v-for="s in sections" :key="s.key" :name="s.key" :tab="label(s)" :disabled="s.disabled" />
    </NTabs>

    <!-- side: left vertical menu | right panel -->
    <div v-if="layout === 'side'" class="t-detail-layout__split">
      <NMenu
        :value="activeSection ?? undefined"
        :options="menuOptions"
        mode="vertical"
        :indent="14"
        class="t-detail-layout__nav"
        @update:value="onSection"
      />
      <div class="t-detail-layout__panel">
        <slot :section="activeSection" />
      </div>
    </div>

    <!-- plain + tabs share the single body region -->
    <div v-else class="t-detail-layout__body">
      <slot :section="activeSection" />
    </div>

    <div v-if="$slots.footer" class="t-detail-layout__footer">
      <slot name="footer" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, h, type CSSProperties } from 'vue'
import { NTabs, NTabPane, NMenu } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TPageHeader from '../layout/TPageHeader.vue'
import { maybeTranslateKey } from '../../pages/_shared/translate'
import type { DetailSection, DetailLayout } from '../../headless/useDetail'

interface Props {
  layout?: DetailLayout
  sections?: DetailSection[]
  activeSection?: string | null
  title?: string
  icon?: string
  back?: boolean | string
  translate?: (key: string) => string
  /** Render the in-layout TPageHeader. Set false when an overlay (modal/drawer) already provides the title chrome. */
  showHeader?: boolean
  /**
   * Cap the width of constrained content (wrapped in `.t-detail-content`) so
   * form inputs don't stretch edge-to-edge on ultrawide displays. Writes the
   * `--tnzi-detail-content-max` CSS var onto the layout root; descendants that
   * opt in via the `.t-detail-content` utility inherit it. Data tables stay
   * full-width by simply not opting in. A number is treated as px; a string
   * (e.g. '1100px', '80ch', 'none') is used verbatim — `none` lets content
   * fill again. When omitted, `.t-detail-content` falls back to its 920px
   * default.
   */
  contentMaxWidth?: number | string
}

const props = withDefaults(defineProps<Props>(), {
  layout: 'plain',
  sections: () => [],
  activeSection: null,
  title: undefined,
  icon: undefined,
  back: undefined,
  translate: undefined,
  showHeader: true,
  contentMaxWidth: undefined,
})

const rootStyle = computed<CSSProperties | undefined>(() => {
  if (props.contentMaxWidth == null) return undefined
  const value = typeof props.contentMaxWidth === 'number' ? `${props.contentMaxWidth}px` : props.contentMaxWidth
  return { '--tnzi-detail-content-max': value } as CSSProperties
})

const emit = defineEmits<{ 'update:activeSection': [key: string] }>()

defineSlots<{
  title?: () => unknown
  actions?: () => unknown
  footer?: () => unknown
  default?: (props: { section: string | null }) => unknown
}>()

function label(s: DetailSection): string {
  return maybeTranslateKey(props.translate, s.label, s.label)
}

function onSection(key: string): void {
  emit('update:activeSection', key)
}
// exposed for unit testing the section handler without simulating naive events
defineExpose({ onSection })

/** Group sections into NMenu option groups when any section declares a `group`. */
const menuOptions = computed(() => {
  const hasGroups = props.sections.some((s) => s.group)
  const toItem = (s: DetailSection) => ({
    key: s.key,
    label: label(s),
    disabled: s.disabled,
    icon: s.icon ? () => h(TSvgIcon, { icon: s.icon as string, size: 14 }) : undefined,
  })
  if (!hasGroups) return props.sections.map(toItem)
  const order: string[] = []
  const byGroup = new Map<string, DetailSection[]>()
  for (const s of props.sections) {
    const g = s.group ?? ''
    if (!byGroup.has(g)) {
      byGroup.set(g, [])
      order.push(g)
    }
    byGroup.get(g)!.push(s)
  }
  return order.map((g) => ({
    type: 'group' as const,
    key: `group:${g || 'default'}`,
    label: g ? maybeTranslateKey(props.translate, g, g) : '',
    children: (byGroup.get(g) ?? []).map(toItem),
  }))
})
</script>

<style scoped>
.t-detail-layout { display: flex; flex-direction: column; gap: 12px; height: 100%; min-height: 0; }
.t-detail-layout__tabs { flex-shrink: 0; }
.t-detail-layout__body { flex: 1 1 auto; min-height: 0; overflow: auto; }
.t-detail-layout__split { flex: 1 1 auto; min-height: 0; display: flex; gap: 12px; }
.t-detail-layout__nav {
  width: 220px; flex-shrink: 0; overflow-y: auto;
  background: var(--tnzi-container-bg); border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius, 6px); padding: 8px 0;
}
/* The panel owns no scroll of its own — like BotDetail's `.panel`, it's a
   clipping flex column so the slotted section can pin a fixed header bar and
   let only its body scroll / a flex-height table fill the residual space.
   Slot content is expected to be a full-height flex child (the single
   `layout="side"` consumer, UserCenter, satisfies this). */
.t-detail-layout__panel {
  flex: 1 1 auto; min-width: 0; min-height: 0; overflow: hidden;
  display: flex; flex-direction: column;
  background: var(--tnzi-container-bg); border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius, 6px);
}
.t-detail-layout__footer {
  flex-shrink: 0; display: flex; justify-content: flex-end; gap: 8px;
  padding-top: 12px; border-top: 1px solid var(--tnzi-border);
}
@media (max-width: 767px) {
  .t-detail-layout__split { flex-direction: column; }
  /* 移动堆叠：nav 必须限高且可收缩（flex 0 1 auto + max-height），否则在
     column 方向上 flex-shrink:0 的 nav 吃满容器高度，把 panel 压成 0 高。 */
  .t-detail-layout__nav { width: 100%; flex: 0 1 auto; max-height: 40%; }
}
</style>
