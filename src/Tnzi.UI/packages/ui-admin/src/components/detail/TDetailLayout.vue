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
      <div class="t-detail-layout__nav-col">
        <div class="t-detail-layout__nav-card">
          <!-- Optional header INSIDE the nav container (e.g. a filter/search
               box) - integrated at the top of the menu card, divided from the
               menu by a hairline. Hidden on the collapsed phone rail where the
               60px width can't fit it. -->
          <div v-if="$slots['nav-header']" class="t-detail-layout__nav-header">
            <slot name="nav-header" />
          </div>
          <NMenu
            ref="navRef"
            :value="activeSection ?? undefined"
            :options="menuOptions"
            mode="vertical"
            :indent="14"
            :collapsed="isSm"
            :collapsed-width="60"
            :collapsed-icon-size="20"
            class="t-detail-layout__nav"
            @update:value="onSection"
          />
        </div>
      </div>
      <div ref="panelRef" class="t-detail-layout__panel">
        <slot :section="activeSection" :section-icon="activeSectionIcon" />
      </div>
    </div>

    <!-- plain + tabs share the single body region -->
    <div v-else ref="bodyRef" class="t-detail-layout__body">
      <slot :section="activeSection" :section-icon="activeSectionIcon" />
    </div>

    <div v-if="$slots.footer" class="t-detail-layout__footer">
      <slot name="footer" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, h, nextTick, ref, type CSSProperties, type ComponentPublicInstance } from 'vue'
import { NTabs, NTabPane, NMenu } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import TPageHeader from '../layout/TPageHeader.vue'
import { maybeTranslateKey } from '../../pages/_shared/translate'
import { useBreakpoint } from '../../headless/useBreakpoint'
import type { DetailSection, DetailLayout } from '../../headless/useDetail'

interface Props {
  layout?: DetailLayout
  sections?: DetailSection[]
  activeSection?: string | null
  title?: string
  icon?: string
  /** Back affordance forwarded to the in-layout `TPageHeader` - `true` (history),
   *  a string (static push), or `{ fallback }` (smart back). See {@link BackTarget}. */
  back?: boolean | string | { fallback?: string }
  translate?: (key: string) => string
  /** Render the in-layout TPageHeader. Set false when an overlay (modal/drawer) already provides the title chrome. */
  showHeader?: boolean
  /**
   * Cap the width of constrained content (wrapped in `.t-detail-content`) so
   * form inputs don't stretch edge-to-edge on ultrawide displays. Writes the
   * `--tnzi-detail-content-max` CSS var onto the layout root; descendants that
   * opt in via the `.t-detail-content` utility inherit it. Data tables stay
   * full-width by simply not opting in. A number is treated as px; a string
   * (e.g. '1100px', '80ch', 'none') is used verbatim - `none` lets content
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
  /** Rendered inside the left nav card, above the menu, in `side` layout (e.g. a section search box). */
  'nav-header'?: () => unknown
  default?: (props: { section: string | null; sectionIcon?: string }) => unknown
}>()

function label(s: DetailSection): string {
  return maybeTranslateKey(props.translate, s.label, s.label)
}

// Icon of the currently-active section (from the nav `sections` metadata) so a
// side/tabs panel header can mirror the menu icon. undefined when the section
// carries no icon (or none is active).
const activeSectionIcon = computed<string | undefined>(
  () => props.sections.find((s) => s.key === props.activeSection)?.icon,
)

function onSection(key: string): void {
  emit('update:activeSection', key)
}

// Phone (<768px, isSm): the side menu collapses to a narrow icon rail (see
// NMenu `:collapsed`) instead of stacking on top of the panel, so it reclaims
// width without eating vertical space.
const { isSm } = useBreakpoint()

// Template refs backing the scrollToSection public API.
const navRef = ref<ComponentPublicInstance | null>(null)
const bodyRef = ref<HTMLElement | null>(null)
const panelRef = ref<HTMLElement | null>(null)

/**
 * Activate a section and scroll it into view - the public replacement for
 * consumers reaching into the layout's private DOM (e.g. `.t-detail-layout__nav`).
 * For `side` layout it brings the selected nav item into view; for every layout
 * it returns the content region to the top so the section starts at the top.
 */
async function scrollToSection(key: string): Promise<void> {
  emit('update:activeSection', key)
  await nextTick()
  const navEl = navRef.value?.$el as HTMLElement | undefined
  navEl?.querySelector?.('.n-menu-item-content--selected')?.scrollIntoView?.({ block: 'nearest' })
  bodyRef.value?.scrollTo?.({ top: 0 })
  panelRef.value?.scrollTo?.({ top: 0 })
}

// exposed for unit testing the section handler without simulating naive events
defineExpose({ onSection, scrollToSection })

/**
 * Badge text for a section that has no `icon`. Used only on the collapsed
 * phone rail (60px) where NMenu shows the icon slot alone - without a fallback
 * an icon-less section would render as an indistinguishable blank dot. Prefer
 * the first character of the (translated) label; fall back to the 1-based
 * ordinal when the label is empty so every section stays tellable apart.
 */
function sectionBadge(s: DetailSection, index: number): string {
  const text = label(s).trim()
  if (text) return text.slice(0, 1).toUpperCase()
  return String(index + 1)
}

/** Group sections into NMenu option groups when any section declares a `group`. */
const menuOptions = computed(() => {
  const hasGroups = props.sections.some((s) => s.group)
  // On the collapsed phone rail, icon-less sections fall back to a letter /
  // ordinal badge instead of an empty icon slot. Desktop (expanded, with text
  // labels) keeps the original behaviour: no badge when there's no icon.
  const phone = isSm.value
  const toItem = (s: DetailSection, index: number) => ({
    key: s.key,
    label: label(s),
    disabled: s.disabled,
    icon: s.icon
      ? () => h(TSvgIcon, { icon: s.icon as string, size: 14 })
      : phone
        ? () => h('span', { class: 't-detail-layout__nav-badge' }, sectionBadge(s, index))
        : undefined,
  })
  if (!hasGroups) return props.sections.map((s, i) => toItem(s, i))
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
    // Badge ordinal tracks the section's position in the flat list so numbers
    // stay stable and unique across groups.
    children: (byGroup.get(g) ?? []).map((s) => toItem(s, props.sections.indexOf(s))),
  }))
})
</script>

<style scoped>
.t-detail-layout { display: flex; flex-direction: column; gap: 12px; height: 100%; min-height: 0; }
.t-detail-layout__tabs { flex-shrink: 0; }
.t-detail-layout__body { flex: 1 1 auto; min-height: 0; overflow: auto; }
.t-detail-layout__split { flex: 1 1 auto; min-height: 0; display: flex; gap: 12px; }
.t-detail-layout__nav-col {
  width: 220px; flex-shrink: 0; min-height: 0;
  display: flex; flex-direction: column;
}
/* The search header + menu share ONE bordered card so the search box reads as
   an integrated top bar of the navigation container rather than a floating box
   above it. The card is a clipping flex column: header pinned, menu scrolls.
   No divider under the search + a tight gap so it sits close to the menu. */
.t-detail-layout__nav-card {
  flex: 1 1 auto; min-height: 0;
  display: flex; flex-direction: column; overflow: hidden;
  background: var(--tnzi-admin-card-bg, var(--tnzi-container-bg)); border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius, 6px);
}
.t-detail-layout__nav-header {
  flex-shrink: 0; padding: 8px 8px 0;
}
.t-detail-layout__nav {
  width: 100%; flex: 1 1 auto; min-height: 0; overflow-y: auto;
  background: transparent; border: none; border-radius: 0; padding: 6px 0;
}
/* The panel owns no scroll of its own - like BotDetail's `.panel`, it's a
   clipping flex column so the slotted section can pin a fixed header bar and
   let only its body scroll / a flex-height table fill the residual space.
   Slot content is expected to be a full-height flex child (the single
   `layout="side"` consumer, UserCenter, satisfies this). */
.t-detail-layout__panel {
  flex: 1 1 auto; min-width: 0; min-height: 0; overflow: hidden;
  display: flex; flex-direction: column;
  background: var(--tnzi-admin-card-bg, var(--tnzi-container-bg)); border: 1px solid var(--tnzi-border);
  border-radius: var(--tnzi-admin-radius, 6px);
}
.t-detail-layout__footer {
  flex-shrink: 0; display: flex; justify-content: flex-end; gap: 8px;
  padding-top: 12px; border-top: 1px solid var(--tnzi-border);
}
/* Fallback badge for icon-less sections on the collapsed phone rail - a small
   letter/ordinal chip that reads like an icon so each section stays distinct.
   Only produced when `isSm` is true (see `menuOptions`), so desktop is unaffected. */
.t-detail-layout__nav-badge {
  display: inline-flex; align-items: center; justify-content: center;
  width: 20px; height: 20px;
  border-radius: var(--tnzi-admin-radius-sm, 3px);
  background: var(--tnzi-layout-bg);
  color: var(--tnzi-base-text);
  font-size: 12px; font-weight: 600; line-height: 1;
}
@media (max-width: 767px) {
  /* Phone: keep the LEFT/RIGHT split (do NOT stack) and collapse the menu to
     a narrow icon rail. Stacking is what used to eat ~40% of the screen height;
     staying side-by-side removes that entirely. The panel's section header
     names the active section, so the icon-only rail stays legible. Width tracks
     the NMenu `:collapsed-width` (60). */
  .t-detail-layout__nav-col { width: 60px; }
  /* The 60px rail can't fit a search box - hide the nav header on phones. */
  .t-detail-layout__nav-header { display: none; }
  .t-detail-layout__nav { width: 60px; padding: 6px 0; }
  /* The 60px rail can't fit group titles like "Setup" / "Operations", so they
     clip to "Setu" / "Oper". Hide the text but keep a small gap so the group
     boundary still reads as a subtle separation between icon clusters. */
  .t-detail-layout__nav :deep(.n-menu-item-group-title) {
    height: 10px;
    min-height: 10px;
    padding: 0;
    font-size: 0;
    overflow: hidden;
  }
  /* Center the glyph in the 60px rail. naive's collapsed grouped item is a CSS
     grid whose (hidden) label cell still reserves width, pinning the icon to
     the left ~22px off centre. Force the row to flex, remove the label element
     entirely, and center the lone icon that remains. */
  .t-detail-layout__nav :deep(.n-menu-item-content) {
    display: flex !important;
    align-items: center !important;
    justify-content: center !important;
    padding-left: 0 !important;
    padding-right: 0 !important;
  }
  .t-detail-layout__nav :deep(.n-menu-item-content__icon) {
    margin: 0 !important;
  }
  .t-detail-layout__nav :deep(.n-menu-item-content-header) {
    display: none !important;
  }
}
</style>
