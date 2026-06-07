<template>
  <div class="t-row-actions">
    <slot name="prepend" :row="row" />

    <!-- Inline action buttons. Actions flagged `confirm` are wrapped in a
         popconfirm so destructive ops still gate before firing. -->
    <template v-for="action in inline" :key="action.key">
      <NPopconfirm
        v-if="needsConfirm(action)"
        @positive-click="() => fire(action)"
      >
        <template #trigger>
          <NButton
            :type="action.type ?? 'default'"
            ghost
            size="small"
            :disabled="isDisabled(action)"
          >
            <template v-if="action.icon" #icon><TSvgIcon :icon="action.icon" :size="14" /></template>
            {{ labelOf(action) }}
          </NButton>
        </template>
        {{ confirmTextOf(action) }}
      </NPopconfirm>
      <NButton
        v-else
        :type="action.type ?? 'default'"
        ghost
        size="small"
        :disabled="isDisabled(action)"
        @click="fire(action)"
      >
        <template v-if="action.icon" #icon><TSvgIcon :icon="action.icon" :size="14" /></template>
        {{ labelOf(action) }}
      </NButton>
    </template>

    <slot name="middle" :row="row" />

    <!-- Overflow → More▾ dropdown. -->
    <NDropdown
      v-if="overflow.length"
      trigger="click"
      :options="dropdownOptions"
      @select="onDropdownSelect"
    >
      <NButton size="small" ghost class="t-row-actions__more">
        {{ moreLabelText }} ▾
      </NButton>
    </NDropdown>

    <slot name="append" :row="row" />
  </div>
</template>

<script setup lang="ts" generic="T, TId extends string | number = string | number">
import { computed } from 'vue'
import { NButton, NDropdown, NPopconfirm, useDialog, type DropdownOption } from 'naive-ui'
import { TSvgIcon } from '@tnzi/ui'
import { splitRowActions, type RowAction } from '../../headless/rowActions'

export interface TRowActionsProps<T> {
  row: T
  /** Declarative action list. Use `editAction`/`viewAction`/`deleteAction`
   *  factories for the built-ins, plain objects for custom operations.
   *  Optional (defaults to `[]`) so a slot-only usage that just hosts
   *  `#prepend`/`#append` buttons can omit it. */
  actions?: RowAction<T>[]
  /** Max inline slots before the tail collapses into More▾ (default 2). */
  maxInline?: number
  /** Disable collapsing — render every action inline (default false → collapse on). */
  collapse?: boolean
  /** Override the More▾ button label. */
  moreLabel?: string
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<TRowActionsProps<T>>(), {
  actions: () => [],
  maxInline: 2,
  collapse: true,
  moreLabel: undefined,
  translate: undefined,
})

defineSlots<{
  prepend?: (props: { row: T }) => unknown
  middle?: (props: { row: T }) => unknown
  append?: (props: { row: T }) => unknown
}>()

function t(key: string): string {
  if (props.translate) return props.translate(key)
  const fallback: Record<string, string> = {
    'admin.crud.edit': 'Edit',
    'admin.crud.view': 'View',
    'admin.crud.delete': 'Delete',
    'admin.crud.more': 'More',
    'admin.crud.confirm': 'Confirm',
    'admin.crud.cancel': 'Cancel',
    'admin.crud.confirmDelete': 'Are you sure to delete?',
  }
  return fallback[key] ?? key
}
function maybeTranslate(value: string): string {
  if (/^[a-z][a-zA-Z0-9]*(\.[a-zA-Z0-9]+)*$/.test(value)) return t(value)
  return value
}

const { inline, overflow } = (() => {
  const split = computed(() =>
    splitRowActions(props.actions, props.row, {
      maxInline: props.maxInline,
      collapse: props.collapse,
    }),
  )
  return {
    inline: computed(() => split.value.inline),
    overflow: computed(() => split.value.overflow),
  }
})()

const moreLabelText = computed(() => props.moreLabel ?? t('admin.crud.more'))

/** Default i18n label keys for the built-in action keys. */
const DEFAULT_LABEL_KEY: Record<string, string> = {
  edit: 'admin.crud.edit',
  view: 'admin.crud.view',
  delete: 'admin.crud.delete',
}

function rawLabel(a: RowAction<T>): string {
  if (a.label != null) return typeof a.label === 'function' ? a.label(props.row) : a.label
  return DEFAULT_LABEL_KEY[a.key] ?? a.key
}
function labelOf(a: RowAction<T>): string {
  return maybeTranslate(rawLabel(a))
}
function isDisabled(a: RowAction<T>): boolean {
  return a.disabled ? a.disabled(props.row) : false
}
function needsConfirm(a: RowAction<T>): boolean {
  return a.confirm !== undefined && a.confirm !== false
}
function confirmTextOf(a: RowAction<T>): string {
  const c = a.confirm
  if (typeof c === 'function') return c(props.row)
  if (typeof c === 'string') return maybeTranslate(c)
  return t('admin.crud.confirmDelete')
}
function fire(a: RowAction<T>): void {
  void a.onClick?.(props.row)
}

// ── More▾ dropdown ────────────────────────────────────────────────────
const dropdownOptions = computed<DropdownOption[]>(() => {
  const opts: DropdownOption[] = []
  for (const a of overflow.value) {
    if (a.divider && opts.length > 0) {
      opts.push({ type: 'divider', key: `divider-${a.key}` })
    }
    opts.push({
      key: a.key,
      label: labelOf(a),
      disabled: isDisabled(a),
      ...(a.type === 'error'
        ? { props: { style: 'color: var(--tnzi-color-error, #e54040)' } }
        : {}),
    })
  }
  return opts
})

function safeDialog(): {
  warning: (cfg: {
    title: string
    content: string
    positiveText: string
    negativeText: string
    onPositiveClick: () => void | Promise<void>
  }) => void
} {
  try {
    const d = useDialog()
    return { warning: (cfg) => { d.warning(cfg) } }
  } catch {
    return {
      warning: (cfg) => {
        if (typeof window !== 'undefined' && typeof window.confirm === 'function') {
          if (window.confirm(cfg.content)) void cfg.onPositiveClick()
        }
      },
    }
  }
}
const dialog = safeDialog()

function onDropdownSelect(key: string): void {
  const action = overflow.value.find((a) => a.key === key)
  if (!action) return
  if (needsConfirm(action)) {
    dialog.warning({
      title: labelOf(action),
      content: confirmTextOf(action),
      positiveText: t('admin.crud.confirm'),
      negativeText: t('admin.crud.cancel'),
      onPositiveClick: () => fire(action),
    })
    return
  }
  fire(action)
}
</script>

<style scoped>
.t-row-actions {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  flex-wrap: wrap;
}
</style>
