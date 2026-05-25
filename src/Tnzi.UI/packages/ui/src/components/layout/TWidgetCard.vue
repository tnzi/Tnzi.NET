<script setup lang="ts">
/**
 * `TWidgetCard` — universal frame around a single Workbench widget.
 *
 * Responsibilities:
 *   - Render the NCard chrome (title + optional icon + refresh button +
 *     drag handle when the parent layout is `'draggable'`).
 *   - Provide `WidgetContext` to descendants via `WIDGET_CONTEXT_KEY` so
 *     widgets can `useWidget()` to imperatively trigger refresh / set
 *     busy state / surface errors.
 *   - Show a unified busy / error fallback so individual widgets stay
 *     small and focused on their happy-path render.
 *
 * The card *does not* fetch data — that lives in each widget. The card
 * just coordinates the UX around the widget's reactive state.
 *
 * Sunk from `@tnzi/ui-admin/widgets/shell/TWidgetCard.vue` in 0.2.x.
 * The `translate` prop replaces the previous direct import of
 * `translatePageKey` so the card is usable outside the admin shell.
 */
import { computed, provide, ref } from 'vue'
import { NCard, NButton, NSpin, NAlert } from 'naive-ui'
import TSvgIcon from '../display/TSvgIcon.vue'
import { WIDGET_CONTEXT_KEY, type WidgetContext } from './widget-types'

interface Props {
  /** Stable widget id, surfaced into the WidgetContext for diagnostics. */
  id: string
  /** Card title — i18n key (resolved via `translate`) or raw text. */
  title?: string
  /** Iconify icon shown next to the title (e.g. `mdi:chart-bar`). */
  icon?: string
  /** Fixed body height in pixels, or omit for content-driven sizing. */
  height?: number | 'auto'
  /** Show the refresh button on the toolbar. Default `true`. */
  refreshable?: boolean
  /** Render the body bare without the NCard chrome. Default `false`. */
  bare?: boolean
  /**
   * When inside a `'draggable'` workbench layout the card surfaces a
   * drag handle so the user can grab it. Default `false`.
   */
  draggable?: boolean
  /**
   * Optional i18n translator. Receives a key (`admin.widgets.x.title`),
   * returns the translated string. When omitted or returning an empty
   * string the raw value passes through unchanged.
   */
  translate?: (key: string) => string
  /** Refresh button tooltip. Default `"Refresh"`. */
  refreshLabel?: string
  /** Drag-handle tooltip. Default `"Drag to reorder"`. */
  dragLabel?: string
  /** Error alert title. Default `"Error"`. */
  errorLabel?: string
}

const props = withDefaults(defineProps<Props>(), {
  title: undefined,
  icon: undefined,
  height: 'auto',
  refreshable: true,
  bare: false,
  draggable: false,
  translate: undefined,
  refreshLabel: 'Refresh',
  dragLabel: 'Drag to reorder',
  errorLabel: 'Error',
})

const emit = defineEmits<{
  /** Fired when the user clicks the refresh button. */
  refresh: []
}>()

// --- WidgetContext ----------------------------------------------------------
const busy = ref(false)
const error = ref<unknown>(null)
const refreshCallbacks = new Set<() => void | Promise<void>>()

// `id` is exposed as a getter so descendants calling `useWidget().id` see
// the live prop value even when the parent (e.g. a custom workbench
// layout missing `:key="def.id"`) reuses the same TWidgetCard instance
// for different widget definitions.
const context: WidgetContext = {
  get id() {
    return props.id
  },
  refresh: () => {
    fireRefreshCallbacks()
    emit('refresh')
  },
  reportError: (err: unknown) => {
    error.value = err
  },
  setBusy: (b: boolean) => {
    busy.value = b
  },
  onRefresh: (cb: () => void | Promise<void>) => {
    refreshCallbacks.add(cb)
    return () => {
      refreshCallbacks.delete(cb)
    }
  },
}
provide(WIDGET_CONTEXT_KEY, context)

function fireRefreshCallbacks(): void {
  for (const cb of refreshCallbacks) {
    try {
      const ret = cb()
      // Surface async errors via the standard error channel so the user
      // sees them in the card's error slot instead of an unhandled
      // promise warning in the console.
      if (ret instanceof Promise) {
        ret.catch((err: unknown) => {
          error.value = err
        })
      }
    } catch (err) {
      error.value = err
    }
  }
}

// --- Display helpers --------------------------------------------------------
// `maybeTranslate` heuristic — treat dotted lower-camel ASCII as an i18n key,
// everything else as a literal string. Mirrors the convention the admin
// shell uses so existing widget descriptors keep working unchanged.
const I18N_KEY_PATTERN = /^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)+$/

function maybeTranslate(value: string | undefined | null): string {
  if (!value) return ''
  if (!props.translate) return value
  if (!I18N_KEY_PATTERN.test(value)) return value
  const hit = props.translate(value)
  return hit || value
}

const resolvedTitle = computed(() => maybeTranslate(props.title))

const errorMessage = computed(() => {
  const e = error.value
  if (!e) return ''
  if (e instanceof Error) return e.message
  return String(e)
})

const bodyStyle = computed(() => ({
  height: typeof props.height === 'number' ? `${props.height}px` : undefined,
}))

function handleRefresh(): void {
  error.value = null
  fireRefreshCallbacks()
  emit('refresh')
}
</script>

<template>
  <!-- Bare mode: skip the NCard chrome so widgets like the header banner
       can claim the full card surface without a duplicate title row. The
       provide() above still runs so children can useWidget() either way. -->
  <div v-if="bare" class="t-widget-card t-widget-card--bare" :data-widget-id="id">
    <slot />
  </div>

  <NCard
    v-else
    class="t-widget-card"
    :data-widget-id="id"
    size="small"
    :bordered="false"
  >
    <template #header>
      <div class="t-widget-card__header">
        <TSvgIcon v-if="icon" :icon="icon" :size="18" class="t-widget-card__icon" />
        <span class="t-widget-card__title">{{ resolvedTitle }}</span>
      </div>
    </template>
    <template #header-extra>
      <div class="t-widget-card__actions">
        <slot name="header-extra" />
        <NButton
          v-if="refreshable"
          quaternary
          circle
          size="small"
          :title="refreshLabel"
          :loading="busy"
          @click="handleRefresh"
        >
          <template #icon>
            <TSvgIcon icon="mdi:refresh" :size="16" />
          </template>
        </NButton>
        <span
          v-if="draggable"
          class="t-widget-card__drag-handle"
          :title="dragLabel"
        >
          <TSvgIcon icon="mdi:drag" :size="16" />
        </span>
      </div>
    </template>

    <NAlert v-if="error" type="error" :show-icon="true" :title="errorLabel">
      {{ errorMessage }}
    </NAlert>
    <NSpin v-else :show="busy" :delay="200">
      <div class="t-widget-card__body" :style="bodyStyle">
        <slot />
      </div>
    </NSpin>
  </NCard>
</template>

<style scoped>
.t-widget-card {
  /* soybean parity — soft drop shadow on the card body. */
  background: var(--tnzi-container-bg, #fff);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 1px 2px rgb(0 0 0 / 0.05);
  transition:
    transform var(--tnzi-admin-motion-duration-fast, 0.15s) ease,
    box-shadow var(--tnzi-admin-motion-duration-fast, 0.15s) ease;
}
.t-widget-card:hover {
  box-shadow: 0 4px 12px rgb(0 0 0 / 0.08);
}
.t-widget-card--bare {
  background: transparent;
  box-shadow: none;
}
.t-widget-card__header {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
}
.t-widget-card__icon {
  color: var(--tnzi-primary);
  flex-shrink: 0;
}
.t-widget-card__title {
  font-size: 14px;
  font-weight: 600;
  color: var(--tnzi-base-text);
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.t-widget-card__actions {
  display: flex;
  align-items: center;
  gap: 4px;
}
.t-widget-card__drag-handle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  cursor: grab;
  color: var(--tnzi-base-text-muted, #888);
}
.t-widget-card__drag-handle:active {
  cursor: grabbing;
}
.t-widget-card__body {
  display: flex;
  flex-direction: column;
}
</style>
