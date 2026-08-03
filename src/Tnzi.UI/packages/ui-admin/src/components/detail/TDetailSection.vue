<template>
  <section class="t-detail-section">
    <header class="t-detail-section__bar">
      <div class="t-detail-section__heading">
        <div class="t-detail-section__title-row">
          <h3 class="t-detail-section__title">
            <TSvgIcon v-if="resolvedIcon" :icon="resolvedIcon" :size="18" class="t-detail-section__title-icon" />
            <span>{{ title }}</span>
          </h3>
          <!-- Beside the title: the hint as a ⓘ + hover popover (`hintMode:
               'popover'`), then whatever the page hangs on `#titleExtra`
               (counters, status chips). Both sit OUTSIDE the <h3> on purpose -
               a THint trigger nested in a heading would splice its whole label
               into the heading's accessible name. -->
          <THint v-if="popoverHint" type="info" :size="16" :content="popoverHint" placement="bottom-start" />
          <slot name="titleExtra" />
        </div>
        <p v-if="inlineHint" class="t-detail-section__hint">{{ inlineHint }}</p>
      </div>
      <div v-if="$slots.actions" class="t-detail-section__actions">
        <slot name="actions" />
      </div>
    </header>

    <div class="t-detail-section__body" :class="{ 't-detail-section__body--fill': bodyFill }">
      <div class="t-detail-section__inner" :class="{ 't-detail-section__inner--fill': bodyFill }" :style="innerStyle">
        <slot />
        <div v-if="$slots.savebar" class="t-detail-section__savebar">
          <slot name="savebar" />
        </div>
      </div>
    </div>
  </section>
</template>

<script setup lang="ts">
/**
 * TDetailSection - the standard chrome for ONE section inside a TDetailLayout
 * side/tabs panel. Encapsulates the header bar + body + form-end save bar so
 * every section (form pages AND resource pickers) renders identical chrome.
 *
 * Why a component (not shared classes): section chrome used to live in
 * AgentDetail.vue's `<style scoped>`, so a sibling component (AgentResourcePicker)
 * that reused the class NAMES got NO styling - scoped CSS doesn't cross the
 * component boundary. Owning the markup + CSS here fixes that once for all.
 *
 * Layout: a fixed header bar (title + optional hint on the left, `#actions` on
 * the right) above a body that claims the residual panel height and scrolls.
 * The default slot is the body; `#savebar` renders a top-bordered, right-aligned
 * action strip at the end of the content column (mirrors Fabrikam BotDetail tabs).
 *
 * The title row also takes `#titleExtra` (a chip / counter / tag beside the
 * heading) and can move `hint` into a hover popover via `hintMode="popover"`
 * when the copy runs longer than one line.
 *
 * (Doc comment in the script, not above the root element: a leading comment
 * node in `<template>` makes the component multi-root and breaks fallthrough.)
 */
import { computed, inject, type CSSProperties } from 'vue'
import { THint, TSvgIcon } from '@tnzi/ui'
import { DETAIL_ACTIVE_SECTION_ICON } from './active-section-icon'

/**
 * How `hint` is presented.
 *  - `'inline'` (default): a muted line under the title. Right for ONE short
 *    line - it is read for free, without an interaction.
 *  - `'popover'`: a ⓘ after the title, full text on hover/focus. Right for copy
 *    that would wrap to two or more lines and start outweighing the section
 *    body it is explaining.
 */
export type TDetailSectionHintMode = 'inline' | 'popover'

interface Props {
  /** Section header title (left of the bar). */
  title: string
  /** Optional Iconify icon shown before the title. Defaults to the active nav
   *  section's icon when the enclosing `side`/`tabs` detail PAGE provides one
   *  (`DETAIL_ACTIVE_SECTION_ICON`) - so a section inside a settings / user-center
   *  panel mirrors the menu icon for free. An explicit prop always wins. */
  icon?: string
  /** Explanatory copy for the section. Shown under the title by default; set
   *  `hintMode="popover"` to move it beside the title instead. */
  hint?: string
  /** Where `hint` goes. See {@link TDetailSectionHintMode}. */
  hintMode?: TDetailSectionHintMode
  /** Cap the content column width. Number → px; 'none' → full width (grids). */
  maxWidth?: number | 'none'
  /** Body clips and a flex-height child (e.g. a data table) owns the scroll. */
  bodyFill?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  icon: undefined,
  hint: undefined,
  hintMode: 'inline',
  maxWidth: 920,
  bodyFill: false,
})

defineSlots<{
  default?: () => unknown
  actions?: () => unknown
  savebar?: () => unknown
  /** Chips / counters / tags rendered after the title, on the heading row. */
  titleExtra?: () => unknown
}>()

// Fall back to the page-provided active section icon (side/tabs layout: menu
// icon ⇒ panel title icon). undefined outside such a page - harmless.
const providedIcon = inject(DETAIL_ACTIVE_SECTION_ICON, undefined)
const resolvedIcon = computed(() => props.icon ?? providedIcon?.value)

// One hint, two mutually exclusive placements - the copy has a single source
// (the same `hint` prop / i18n key) so the two can never drift apart.
const inlineHint = computed(() => (props.hintMode === 'inline' ? props.hint : undefined))
const popoverHint = computed(() => (props.hintMode === 'popover' ? props.hint : undefined))

const innerStyle = computed<CSSProperties>(() => {
  if (props.bodyFill || props.maxWidth === 'none') return {}
  return { maxWidth: `${props.maxWidth}px` }
})
</script>

<style scoped>
.t-detail-section {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
}
/* Fixed header bar: title + hint (left), actions (right). */
.t-detail-section__bar {
  flex-shrink: 0;
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  gap: 16px;
  padding: 16px 28px 14px;
  border-bottom: 1px solid var(--tnzi-border);
  background: var(--tnzi-admin-card-bg, var(--tnzi-container-bg));
}
.t-detail-section__heading {
  min-width: 0;
}
/* Heading row: title, its optional hint icon, and `#titleExtra` chips. */
.t-detail-section__title-row {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  min-width: 0;
}
.t-detail-section__title {
  margin: 0;
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 16px;
  font-weight: 600;
  color: var(--tnzi-base-text);
  min-width: 0;
}
/* Mirror the left nav item's icon before the title - muted so the label leads. */
.t-detail-section__title-icon {
  flex-shrink: 0;
  color: var(--tnzi-base-text-muted, #888);
}
.t-detail-section__hint {
  margin: 4px 0 0;
  font-size: 12.5px;
  line-height: 1.5;
  color: var(--tnzi-base-text-muted, #888);
}
.t-detail-section__actions {
  flex-shrink: 0;
  display: flex;
  align-items: center;
  gap: 8px;
}
/* Body claims the rest of the panel height and owns the scroll. */
.t-detail-section__body {
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  padding: 16px 28px 24px;
}
/* Fill variant: clip + flex column so an inner table/list owns the scroll. */
.t-detail-section__body--fill {
  overflow: hidden;
  display: flex;
  flex-direction: column;
  padding-bottom: 16px;
}
.t-detail-section__inner {
  display: flex;
  flex-direction: column;
  gap: 12px;
}
.t-detail-section__inner--fill {
  flex: 1 1 auto;
  min-height: 0;
}
/* Form-end action bar - right-aligned within the content column, top divider. */
.t-detail-section__savebar {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 12px;
  padding-top: 14px;
  border-top: 1px solid var(--tnzi-border);
}
</style>
