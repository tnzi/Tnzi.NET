<template>
  <!-- trigger="click" + uncontrolled: NPopover fully manages its own
       open/close state (trigger click toggles, outside click closes).
       The optional :show prop only seeds initial visibility for tests
       that need to mount with the popover already open; passing it as
       a reactive value puts NPopover into "controlled" mode where
       outside-click dismissal silently no-ops. Forward update:show so
       parents can still observe state changes. -->
  <NPopover
    :default-show="show"
    trigger="click"
    placement="bottom-end"
    :show-arrow="false"
    raw
    class="t-crud-column-setting-popover"
    @update:show="(v: boolean) => emit('update:show', v)"
  >
    <template #trigger>
      <slot name="trigger">
        <!-- NPopover.Binder.Target requires exactly one child; fall back to a hidden anchor
             so the popover stays render-safe when the consumer drives `show` manually
             without supplying its own trigger element. -->
        <span aria-hidden="true" class="inline-block w-0 h-0" />
      </slot>
    </template>
    <div class="t-crud-column-setting">
      <!-- Header: select-all tri-state checkbox + reset button.
           Mirrors soybean (NCheckbox indeterminate when partial). -->
      <div class="t-crud-column-setting__header">
        <NCheckbox
          :checked="selectAllChecked"
          :indeterminate="selectAllIndeterminate"
          :disabled="localOrder.length === 0"
          @update:checked="(c: boolean) => toggleSelectAll(c)"
        >
          {{ t('admin.crud.selectAll') }}
        </NCheckbox>
        <button
          type="button"
          class="t-crud-column-setting__reset"
          :aria-label="t('admin.crud.reset')"
          @click="onReset"
        >
          {{ t('admin.crud.reset') }}
        </button>
      </div>
      <NDivider class="t-crud-column-setting__divider" />
      <!-- Draggable rows. `filter=".none_draggable"` keeps clicks on the
           checkbox from initiating a drag.
           v-model AND v-for both bind to `localOrder` (string[]) — earlier
           split (v-model on the keys array, v-for on a derived ColumnDef
           list) made VueDraggable's reactivity inconsistent: after a drag
           the popover went blank because the two arrays drifted out of
           sync. -->
      <VueDraggable
        v-model="localOrder"
        :animation="150"
        :disabled="bp.isSm.value"
        handle=".t-crud-column-setting__drag"
        filter=".none_draggable"
        class="t-crud-column-setting__list"
        @update:model-value="onReorder"
      >
        <div
          v-for="key in localOrder"
          :key="key"
          class="t-crud-column-setting__row"
        >
          <div class="t-crud-column-setting__row-main">
            <!-- Touch/phone (isSm): hide the tiny 16px drag handle — its hit
                 area is unusable on touch and the drag gesture fights the
                 list's vertical scroll. Cards keep only the checkbox toggle
                 (reordering has little value on a stacked card list). -->
            <span
              v-if="!bp.isSm.value"
              class="t-crud-column-setting__drag"
              :aria-label="t('admin.crud.dragToReorder')"
            >
              <TSvgIcon icon="mdi:drag" :size="16" />
            </span>
            <NCheckbox
              class="none_draggable t-crud-column-setting__check"
              :checked="!props.settings.hiddenKeys.value.has(key)"
              @update:checked="() => props.settings.toggle(key)"
            >
              {{ columnLabel(key) }}
            </NCheckbox>
          </div>
        </div>
      </VueDraggable>
    </div>
  </NPopover>
</template>

<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { NPopover, NCheckbox, NDivider } from 'naive-ui'
import { VueDraggable } from 'vue-draggable-plus'
import { TSvgIcon } from '@tnzi/ui'
import { useColumnSettings, type ColumnDef } from '../../headless/useColumnSettings'
import { useBreakpoint } from '../../headless/useBreakpoint'

type ColumnSettings = ReturnType<typeof useColumnSettings>

interface Props {
  settings: ColumnSettings
  allColumns: ColumnDef[]
  show: boolean
  translate?: (key: string) => string
}

const props = defineProps<Props>()

const emit = defineEmits<{
  'update:show': [boolean]
}>()

const bp = useBreakpoint()

function t(key: string): string {
  return props.translate ? props.translate(key) : key
}

/** Translate a column title that may be either an i18n key
 *  (`columns.username`, `admin.crud.columns.role`) or a plain label
 *  ("Username"). Same heuristic as TCrudPage.maybeTranslate so the
 *  Columns popover shows the same human label as the table header. */
function maybeTranslate(value: string | undefined, fallback: string): string {
  if (!value) return fallback
  if (/^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)*$/.test(value)) return t(value)
  return value
}

const localOrder = ref<string[]>([...props.settings.orderedKeys.value])

watch(
  () => props.settings.orderedKeys.value,
  (next) => {
    localOrder.value = [...next]
  },
  { deep: true },
)

const columnMap = computed(() => new Map(props.allColumns.map((c) => [c.key, c])))

/** Resolve display label for a key — handles i18n keys (`columns.username`,
 *  `admin.crud.X`) and falls back to the raw title or the key itself. */
function columnLabel(key: string): string {
  const col = columnMap.value.get(key)
  if (!col) return key
  return maybeTranslate(col.title, col.title)
}

/** Tri-state select-all: derived from the count of currently-checked
 *  columns vs total. Mirrors soybean (full / indeterminate / unchecked). */
const visibleStats = computed(() => {
  const total = localOrder.value.length
  let checked = 0
  for (const k of localOrder.value) {
    if (!props.settings.hiddenKeys.value.has(k)) checked += 1
  }
  return { total, checked }
})
const selectAllChecked = computed(() => {
  const { total, checked } = visibleStats.value
  return total > 0 && checked === total
})
const selectAllIndeterminate = computed(() => {
  const { total, checked } = visibleStats.value
  return checked > 0 && checked < total
})
function toggleSelectAll(checked: boolean): void {
  for (const k of localOrder.value) {
    const currentlyHidden = props.settings.hiddenKeys.value.has(k)
    if (checked && currentlyHidden) props.settings.show(k)
    if (!checked && !currentlyHidden) props.settings.hide(k)
  }
}

function onReorder(next: unknown[]): void {
  // VueDraggable now binds to `localOrder` (string[]) directly, so we
  // get a string[] back. Keep the cast loose to tolerate other shapes
  // emitted by older / patched vue-draggable-plus releases.
  const keys = (next as unknown[]).map((v) => (typeof v === 'string' ? v : (v as ColumnDef).key))
  props.settings.reorder(keys)
}

function onReset(): void {
  props.settings.reset()
}
</script>

<style scoped>
.t-crud-column-setting {
  min-width: 260px;
  background: var(--tnzi-container-bg, #fff);
  border-radius: var(--tnzi-admin-radius-md, 8px);
  box-shadow: 0 4px 16px rgb(0 0 0 / 0.08);
  padding: 6px;
}
.t-crud-column-setting__header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 4px 8px;
}
.t-crud-column-setting__reset {
  background: none;
  border: none;
  cursor: pointer;
  color: var(--tnzi-primary, #646cff);
  padding: 0;
  font: inherit;
  font-size: 13px;
}
.t-crud-column-setting__reset:hover {
  text-decoration: underline;
}
.t-crud-column-setting__divider {
  margin: 4px 0 !important;
}
/* List — capped height so very wide tables don't push the popover off
   screen; internal scroll picks up the global macOS-style overlay rules. */
.t-crud-column-setting__list {
  display: flex;
  flex-direction: column;
  gap: 2px;
  max-height: 280px;
  overflow-y: auto;
  padding: 2px;
}
.t-crud-column-setting__row {
  display: flex;
  align-items: center;
  gap: 4px;
  height: 32px;
  padding: 0 4px;
  border-radius: var(--tnzi-admin-radius-sm, 4px);
  transition: background-color 0.12s ease;
}
.t-crud-column-setting__row:hover {
  background: rgb(var(--tnzi-primary-rgb, 100 108 255) / 0.08);
}
.t-crud-column-setting__row-main {
  display: flex;
  align-items: center;
  gap: 6px;
  flex: 1 1 auto;
  min-width: 0;
}
.t-crud-column-setting__drag {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 20px;
  height: 20px;
  cursor: grab;
  color: var(--tnzi-base-text-muted, #9ca3af);
}
.t-crud-column-setting__drag:active {
  cursor: grabbing;
}
.t-crud-column-setting__check {
  flex: 1 1 auto;
  min-width: 0;
}
</style>
