<template>
  <!--
    Master-detail layout primitive - a list/tree pane on the left and a
    detail/preview/timeline pane on the right. Encapsulates the responsive
    grid + fill-height chain + mobile stacking that ~6 built-in pages
    (Permissions / RoleFunctions / Organizations / AgentRunMonitor /
    WorkflowRunViewer / EvaluationViewer) each used to hand-roll.

    Layout-only by design: selection state, data and inner content stay with
    the consumer (a tree, a list, cards - the primitive is agnostic). Drop it
    inside a `TContentPage card scroll="fill"` (or any fill-height flex parent)
    and both panes fill the available height and scroll internally; below the
    `stackAt` breakpoint the master moves above the detail with a capped
    height so phones get a usable stacked view for free.
  -->
  <div
    class="t-master-detail"
    :class="{ 't-master-detail--plain': !bordered }"
    :style="rootStyle"
  >
    <div class="t-master-detail__master">
      <slot name="master" />
    </div>
    <div
      class="t-master-detail__detail"
      :class="{ 't-master-detail__detail--scroll': detailScroll }"
    >
      <slot name="detail" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

interface Props {
  /** Left (master) pane width on desktop. Number → px; string → used verbatim. */
  masterWidth?: number | string
  /** Gap between the two panes (px). Doubles as the bordered master's right padding. */
  gap?: number
  /** Capped master height once stacked below 768px (so the detail stays reachable on phones). */
  masterMobileMaxHeight?: string
  /** Draw the divider (right border desktop / bottom border stacked) between panes. */
  bordered?: boolean
  /** Let the detail pane scroll internally (default). Turn off for a detail that owns its own scroll. */
  detailScroll?: boolean
}

const props = withDefaults(defineProps<Props>(), {
  masterWidth: 300,
  gap: 12,
  masterMobileMaxHeight: '40vh',
  bordered: true,
  detailScroll: true,
})

defineSlots<{
  master?: () => unknown
  detail?: () => unknown
}>()

const masterWidthCss = computed(() =>
  typeof props.masterWidth === 'number' ? `${props.masterWidth}px` : props.masterWidth,
)

// CSS-var driven so the breakpoint stays a pure media query (matches every
// existing master-detail page, no JS resize listener) while width / gap /
// stacked-height are all consumer-tunable.
const rootStyle = computed(() => ({
  '--t-md-master-w': masterWidthCss.value,
  '--t-md-gap': `${props.gap}px`,
  '--t-md-master-mh': props.masterMobileMaxHeight,
}))
</script>

<style scoped>
.t-master-detail {
  flex: 1 1 auto;
  min-height: 0;
  height: 100%;
  display: grid;
  grid-template-columns: var(--t-md-master-w, 300px) 1fr;
  /* Cap the single row to the container so each pane gets a definite height
     and its own overflow can engage (panes scroll internally, not the page). */
  grid-template-rows: minmax(0, 1fr);
  gap: var(--t-md-gap, 12px);
}
.t-master-detail__master {
  display: flex;
  flex-direction: column;
  min-height: 0;
  min-width: 0;
  /* Scrolls internally so the master works whether its content owns the
     scroll (a `flex:1; overflow:auto` tree) or not (a `max-height` list). */
  overflow-y: auto;
  border-right: 1px solid var(--tnzi-border);
  padding-right: var(--t-md-gap, 12px);
}
.t-master-detail--plain .t-master-detail__master {
  border-right: none;
  padding-right: 0;
}
.t-master-detail__detail {
  min-height: 0;
  min-width: 0;
}
.t-master-detail__detail--scroll {
  overflow: auto;
}

/* Stacked (phone): master on top with a capped, self-scrolling height and a
   bottom divider instead of the right one. The breakpoint mirrors the
   codebase-wide 768px master-detail convention. */
@media (max-width: 767px) {
  .t-master-detail {
    grid-template-columns: 1fr;
    grid-template-rows: none;
    height: auto;
  }
  .t-master-detail__master {
    border-right: none;
    padding-right: 0;
    border-bottom: 1px solid var(--tnzi-border);
    padding-bottom: 12px;
    max-height: var(--t-md-master-mh, 40vh);
    overflow: auto;
  }
  .t-master-detail--plain .t-master-detail__master {
    border-bottom: none;
    padding-bottom: 0;
  }
  /* Stacked: the page (outer fill container) owns the scroll, so the detail
     flows at natural height instead of trapping a second scrollbar. */
  .t-master-detail__detail--scroll {
    overflow: visible;
  }
}
</style>
