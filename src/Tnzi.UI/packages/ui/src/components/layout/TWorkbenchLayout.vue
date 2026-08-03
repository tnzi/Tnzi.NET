<script setup lang="ts">
/**
 * `TWorkbenchLayout` - declarative grid renderer for the Workbench
 * widget system.
 *
 * Walks an array of `WidgetDef`s, renders each one inside a
 * `TWidgetCard` (so refresh + busy + error fallbacks come for free),
 * and lays everything out in a responsive `NGrid`.
 *
 * Two layout modes:
 *  - `'fixed'` (default): renders in declaration order, no drag UI.
 *  - `'draggable'`: wraps the grid in `VueDraggable` so the user can
 *    grab the card handle and re-order; the resulting sequence is
 *    persisted to localStorage by `useWorkbenchLayout`. Pinned widgets
 *    stay put.
 *
 * Permission filtering is handled here (not inside `TWidgetCard`) so
 * widgets the user can't see never even mount.
 *
 * Sunk from `@tnzi/ui-admin/components/pages/TWorkbenchLayout.vue` in
 * 0.2.x. `vue-draggable-plus` is an optional peer dependency - when the
 * consumer never uses `layout: 'draggable'` the bundler tree-shakes the
 * draggable branch out.
 */
import {
  computed,
  defineAsyncComponent,
  markRaw,
  onBeforeUnmount,
  onMounted,
  ref,
  toRef,
  watch,
  type Component,
} from 'vue'
import { NGrid, NGi } from 'naive-ui'
import TWidgetCard from './TWidgetCard.vue'
import { useWorkbenchLayout } from '../../headless/layout/useWorkbenchLayout'
import type { SpanValue, WidgetDef, WorkbenchConfig } from './widget-types'

// `vue-draggable-plus` is an optional peer dependency. Lazy-load it via
// defineAsyncComponent so fixed-mode consumers (who never set
// `layout: 'draggable'`, and may not have it installed) never resolve the
// module. The `<VueDraggable v-if="draggable">` branch only mounts - and
// thus only triggers this dynamic import - in draggable mode.
const VueDraggable = defineAsyncComponent(
  () => import('vue-draggable-plus').then((m) => m.VueDraggable as unknown as Component),
)

interface Props {
  /** Widget array - the source of truth. */
  widgets: WidgetDef[]
  /** Layout mode. Default `'fixed'`. */
  layout?: WorkbenchConfig['layout']
  /** localStorage key for the draggable layout. */
  persistKey?: string
  /** NGrid x-gap. Default 16. */
  xGap?: number
  /** NGrid y-gap. Default 16. */
  yGap?: number
  /**
   * Permission check callback. When provided, widgets whose
   * `permission` fails the check are filtered out *before* render (they
   * never mount). Defaults to "allow everything" so the layout works
   * without a permission store wired up.
   */
  hasPermission?: (key: string) => boolean
  /**
   * Optional i18n translator forwarded to each `TWidgetCard` so widget
   * titles can be resolved against the active locale.
   */
  translate?: (key: string) => string
  /** Refresh button tooltip forwarded to each card. */
  refreshLabel?: string
  /** Drag-handle tooltip forwarded to each card. */
  dragLabel?: string
  /** Error alert title forwarded to each card. */
  errorLabel?: string
}

const props = withDefaults(defineProps<Props>(), {
  layout: 'fixed',
  persistKey: undefined,
  xGap: 16,
  yGap: 16,
  hasPermission: undefined,
  translate: undefined,
  refreshLabel: 'Refresh',
  dragLabel: 'Drag to reorder',
  errorLabel: 'Error',
})

// Resolve permissions first so the persisted-order machinery never sees
// a widget the user can't render (the id would be dropped silently
// anyway, but filtering up-front keeps storage clean).
const visibleWidgets = computed<WidgetDef[]>(() => {
  const allowed = props.hasPermission
    ? props.widgets.filter((w) => !w.permission || props.hasPermission!(w.permission))
    : props.widgets
  // `draggableList` below is a deep `ref`, so anything reachable from a widget
  // gets wrapped in a reactive Proxy - including `component`, which Vue then
  // warns about ("received a Component that was made a reactive object") once
  // per widget. Reactivity is wanted for the ARRAY (VueDraggable reorders it in
  // place); a component definition is inert, so opt it out here, at the single
  // point every render path reads from.
  return allowed.map((w) => (w.component ? { ...w, component: markRaw(w.component) } : w))
})

const draggable = computed(() => props.layout === 'draggable')

const { orderedWidgets, setOrder } = useWorkbenchLayout({
  widgets: visibleWidgets,
  draggable,
  // `toRef` keeps the persistKey tracking the live prop, so a parent
  // swapping the prop (e.g. tab-scoped workbenches) propagates to the
  // hook without re-mounting.
  persistKey: toRef(props, 'persistKey'),
})

// VueDraggable expects a reactive list. Plain `ref` (not `shallowRef`) is
// used so in-place array mutations from VueDraggable trigger reactivity -
// the previous `shallowRef` only kept the UI in sync because the upstream
// `update:modelValue` happened to reassign the value; a future
// vue-draggable-plus that drops the v-model emit would silently desync
// the rendered order and the persisted order.
const draggableList = ref<WidgetDef[]>([...orderedWidgets.value])

// `dragging` flag - set on @start / cleared on @end. The watch below
// pauses while a drag gesture is in flight so an unrelated reactive
// change (e.g. permission refresh causing `visibleWidgets` to re-emit)
// doesn't reassign `draggableList.value` mid-gesture and sever
// VueDraggable's pointer tracking.
const dragging = ref(false)

// Keep the local draggable copy in sync with the computed source when
// the upstream widget array (or persisted order) changes - but only when
// no drag is in flight, otherwise we'd abort the user's gesture.
watch(
  orderedWidgets,
  (next) => {
    if (dragging.value) return
    draggableList.value = [...next]
  },
  { deep: false },
)

function handleDragStart(): void {
  dragging.value = true
}

function handleDragEnd(): void {
  // VueDraggable mutates draggableList.value in place. Persist the new ids.
  setOrder([...draggableList.value])
  dragging.value = false
}

/** Naive-UI NGi span string. `responsive="screen"` reads `xs/sm/md/lg/xl`. */
function spanString(value: SpanValue | undefined): string {
  if (value === undefined) return '24'
  if (typeof value === 'number') return String(value)
  const parts: string[] = []
  // xs is the implicit base - emit it first without a prefix so the
  // breakpoint lookup falls back to it on every smaller width.
  if (value.xs !== undefined) parts.push(String(value.xs))
  else parts.push('24')
  if (value.sm !== undefined) parts.push(`s:${value.sm}`)
  if (value.md !== undefined) parts.push(`m:${value.md}`)
  if (value.lg !== undefined) parts.push(`l:${value.lg}`)
  if (value.xl !== undefined) parts.push(`xl:${value.xl}`)
  return parts.join(' ')
}

/**
 * Pick the most-applicable span for the active viewport width -
 * VueDraggable needs a single grid-column span value (not a NGrid-style
 * breakpoint string), so we resolve the span object client-side using
 * the same Tailwind breakpoints naive-ui's `responsive="screen"` uses.
 *
 * Reactive on window.innerWidth so the cards reflow as the viewport
 * changes. Defaults to 24 (full row) on SSR or when the breakpoint key
 * is missing.
 */
const viewportWidth = ref(typeof window !== 'undefined' ? window.innerWidth : 1280)
function onResize(): void {
  if (typeof window !== 'undefined') viewportWidth.value = window.innerWidth
}
onMounted(() => {
  if (typeof window === 'undefined') return
  window.addEventListener('resize', onResize)
})
onBeforeUnmount(() => {
  if (typeof window === 'undefined') return
  window.removeEventListener('resize', onResize)
})

function activeBreakpoint(width: number): 'xs' | 'sm' | 'md' | 'lg' | 'xl' {
  // Tailwind / naive-ui responsive screen breakpoints.
  if (width >= 1536) return 'xl'
  if (width >= 1024) return 'lg'
  if (width >= 768) return 'md'
  if (width >= 640) return 'sm'
  return 'xs'
}

function resolveSpan(value: SpanValue | undefined): number {
  if (value === undefined) return 24
  if (typeof value === 'number') return value
  const bp = activeBreakpoint(viewportWidth.value)
  // Fall back from current breakpoint to smaller ones so a widget that
  // only defines `lg: 8` still renders sensibly on md/sm/xs (defaults
  // to 24 = full width when no smaller value is supplied).
  const order: Array<'xs' | 'sm' | 'md' | 'lg' | 'xl'> = ['xs', 'sm', 'md', 'lg', 'xl']
  const idx = order.indexOf(bp)
  for (let i = idx; i >= 0; i--) {
    const v = value[order[i]!]
    if (v !== undefined) return v
  }
  // If only larger-than-current keys are set, pick the smallest larger one.
  for (let i = idx + 1; i < order.length; i++) {
    const v = value[order[i]!]
    if (v !== undefined) return v
  }
  return 24
}

function itemStyle(def: WidgetDef): Record<string, string> {
  const span = resolveSpan(def.span)
  return { gridColumn: `span ${Math.min(Math.max(span, 1), 24)} / span ${Math.min(Math.max(span, 1), 24)}` }
}

/**
 * Resolve a widget component descriptor into a renderable component.
 * Plain components pass through; promise-returning factories get
 * wrapped with `defineAsyncComponent` so consumers can use the standard
 * dynamic-import idiom (`component: () => import('./MyWidget.vue')`).
 *
 * Discrimination: an async factory takes zero args (`() => Promise<...>`)
 * while a Vue functional component takes at least one (`(props) => VNode`).
 * Using `.length === 0` to distinguish - both are typeof 'function', so the
 * previous typeof-only check incorrectly wrapped functional components
 * in `defineAsyncComponent`, which then tried to `.then()` a VNode and
 * crashed at render.
 */
function resolveComponent(def: WidgetDef): Component {
  const c = def.component
  if (typeof c === 'function' && (c as (...args: unknown[]) => unknown).length === 0) {
    return defineAsyncComponent(c as () => Promise<{ default: Component }>)
  }
  return c as Component
}

function onWidgetRefresh(def: WidgetDef): void {
  // The card already provides WidgetContext.refresh to descendants -
  // this top-level handler exists so external observers (e.g.
  // analytics) could hook in later without touching every widget.
  // Right now it's a no-op pass-through.
  void def.id
}
</script>

<template>
  <div class="t-workbench-layout">
    <slot name="header" />

    <!-- Draggable mode: wrap NGrid items with VueDraggable so the user
         can re-order cards. The drag handle lives on TWidgetCard's
         header. -->
    <VueDraggable
      v-if="draggable"
      v-model="draggableList"
      :animation="180"
      handle=".t-widget-card__drag-handle"
      class="t-workbench-layout__grid t-workbench-layout__grid--draggable"
      :style="{ rowGap: `${yGap}px`, columnGap: `${xGap}px` }"
      @start="handleDragStart"
      @end="handleDragEnd"
    >
      <div
        v-for="def in draggableList"
        :key="def.id"
        class="t-workbench-layout__item"
        :data-widget-id="def.id"
        :style="itemStyle(def)"
      >
        <TWidgetCard
          :id="def.id"
          :title="def.title"
          :icon="def.icon"
          :height="def.height ?? 'auto'"
          :refreshable="def.refreshable ?? true"
          :bare="def.bare ?? false"
          :draggable="!def.pinned"
          :translate="translate"
          :refresh-label="refreshLabel"
          :drag-label="dragLabel"
          :error-label="errorLabel"
          @refresh="onWidgetRefresh(def)"
        >
          <component :is="resolveComponent(def)" v-bind="def.props ?? {}" />
        </TWidgetCard>
      </div>
    </VueDraggable>

    <!-- Fixed mode: a plain NGrid renders the widgets in declaration
         order. Each item picks its own responsive span. -->
    <NGrid
      v-else
      :x-gap="xGap"
      :y-gap="yGap"
      responsive="screen"
      item-responsive
      cols="24"
    >
      <NGi
        v-for="def in orderedWidgets"
        :key="def.id"
        :span="spanString(def.span)"
      >
        <TWidgetCard
          :id="def.id"
          :title="def.title"
          :icon="def.icon"
          :height="def.height ?? 'auto'"
          :refreshable="def.refreshable ?? true"
          :bare="def.bare ?? false"
          :translate="translate"
          :refresh-label="refreshLabel"
          :drag-label="dragLabel"
          :error-label="errorLabel"
          @refresh="onWidgetRefresh(def)"
        >
          <component :is="resolveComponent(def)" v-bind="def.props ?? {}" />
        </TWidgetCard>
      </NGi>
    </NGrid>

    <slot name="footer" />
  </div>
</template>

<style scoped>
.t-workbench-layout {
  display: flex;
  flex-direction: column;
  gap: 16px;
}
.t-workbench-layout__grid--draggable {
  /* Use CSS Grid with a 24-col track so draggable cards honour the same
     SpanValue contract as the fixed-mode NGrid renderer. VueDraggable
     mutates direct children which works fine with grid items - the
     reordering animation operates on flow position regardless. */
  display: grid;
  grid-template-columns: repeat(24, minmax(0, 1fr));
}
.t-workbench-layout__grid--draggable .t-workbench-layout__item {
  min-width: 0;
}
</style>
