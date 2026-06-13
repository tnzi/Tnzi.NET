<template>
  <!--
    TDetailSection — the standard chrome for ONE section inside a TDetailLayout
    side/tabs panel. Encapsulates the header bar + body + form-end save bar so
    every section (form pages AND resource pickers) renders identical chrome.

    Why a component (not shared classes): section chrome used to live in
    AgentDetail.vue's `<style scoped>`, so a sibling component (AgentResourcePicker)
    that reused the class NAMES got NO styling — scoped CSS doesn't cross the
    component boundary. Owning the markup + CSS here fixes that once for all.

    Layout: a fixed header bar (title + optional hint on the left, `#actions` on
    the right) above a body that claims the residual panel height and scrolls.
    The default slot is the body; `#savebar` renders a top-bordered, right-aligned
    action strip at the end of the content column (mirrors Fabrikam BotDetail tabs).
  -->
  <section class="t-detail-section">
    <header class="t-detail-section__bar">
      <div class="t-detail-section__heading">
        <h3 class="t-detail-section__title">{{ title }}</h3>
        <p v-if="hint" class="t-detail-section__hint">{{ hint }}</p>
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
import { computed, type CSSProperties } from 'vue'

interface Props {
  /** Section header title (left of the bar). */
  title: string
  /** Optional one-line hint shown under the title. */
  hint?: string
  /** Cap the content column width. Number → px; 'none' → full width (grids). */
  maxWidth?: number | 'none'
  /** Body clips and a flex-height child (e.g. a data table) owns the scroll. */
  bodyFill?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  hint: undefined,
  maxWidth: 920,
  bodyFill: false,
})

defineSlots<{
  default?: () => unknown
  actions?: () => unknown
  savebar?: () => unknown
}>()

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
  background: var(--tnzi-container-bg);
}
.t-detail-section__heading {
  min-width: 0;
}
.t-detail-section__title {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
  color: var(--tnzi-base-text);
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
/* Form-end action bar — right-aligned within the content column, top divider. */
.t-detail-section__savebar {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  margin-top: 12px;
  padding-top: 14px;
  border-top: 1px solid var(--tnzi-border);
}
</style>
