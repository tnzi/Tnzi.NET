<template>
  <div class="t-row-actions">
    <slot name="prepend" :row="row" />
    <!--
      Read-only rows (file-source / embedded / workspace / code-defined
      entries from dual-source modules — see TSourceBadge): the row's
      `isReadOnly === true` flag suppresses every default action button.
      Consumers can still render a `view` button via #append if they want
      to expose read-only inspection. The "View" entry routes through
      the same form-modal pipeline but with `mode === 'view'`.
    -->
    <template v-if="readOnly">
      <NButton
        ghost
        size="small"
        @click="handleView"
      >
        {{ t('admin.crud.view') }}
      </NButton>
      <!-- Consumers can still attach non-mutating actions to read-only
           rows (e.g. preview, send-test, export) via `moreOptions`. The
           dropdown is suppressed only when no extra options were supplied. -->
      <NDropdown
        v-if="hasMoreOptions"
        trigger="click"
        :options="dropdownOptions"
        @select="(key: string) => onDropdownSelect(key)"
      >
        <NButton size="small" ghost>
          {{ t('admin.crud.more') }} ▾
        </NButton>
      </NDropdown>
    </template>
    <template v-else>
      <NButton
        v-if="showEdit"
        type="primary"
        ghost
        size="small"
        @click="handleEdit"
      >
        {{ t('admin.crud.edit') }}
      </NButton>
      <slot name="middle" :row="row" />
      <NDropdown
        v-if="hasMoreOptions"
        trigger="click"
        :options="dropdownOptions"
        @select="(key: string) => onDropdownSelect(key)"
      >
        <NButton size="small" ghost>
          {{ t('admin.crud.more') }} ▾
        </NButton>
      </NDropdown>
      <NPopconfirm
        v-else-if="showDelete"
        @positive-click="handleDelete"
      >
        <template #trigger>
          <NButton type="error" ghost size="small">
            {{ t('admin.crud.delete') }}
          </NButton>
        </template>
        {{ confirmText ?? t('admin.crud.confirmDelete') }}
      </NPopconfirm>
    </template>
    <slot name="append" :row="row" />
  </div>
</template>

<script setup lang="ts" generic="T, TId extends string | number = string | number">
import { computed } from 'vue'
import { NButton, NDropdown, NPopconfirm, useDialog, type DropdownOption } from 'naive-ui'
import type { UseCrudPageReturn } from '../../headless/useCrudPage'

export interface TRowActionsProps<
  T,
  TId extends string | number = string | number,
> {
  row: T
  state: UseCrudPageReturn<T, TId>
  showEdit?: boolean
  showDelete?: boolean
  confirmText?: string
  /**
   * Strict 2-button mode. When supplied, the row column collapses every
   * secondary operation (incl. Delete) into a `[More▾]` dropdown beside
   * the primary action — see component comments above. The function form
   * lets per-row state (e.g. row.isEnabled) drive the option list.
   */
  moreOptions?: DropdownOption[] | ((row: T) => DropdownOption[])
  /**
   * Selection callback for the More▾ dropdown. The framework intercepts
   * the reserved `delete` key (renders a confirm dialog + invokes
   * `state.handleDelete([rowId])`) so consumers only handle their own
   * custom keys.
   */
  onMoreSelect?: (key: string, row: T) => void
  /**
   * When `false`, the framework's default Delete entry is NOT injected
   * into the More dropdown. Use this if `showDelete` is also `false`
   * (e.g. immutable records like PaymentRefund). Default `true`.
   */
  includeDeleteInMore?: boolean
  translate?: (key: string) => string
}

const props = withDefaults(defineProps<TRowActionsProps<T, TId>>(), {
  showEdit: true,
  showDelete: true,
  confirmText: undefined,
  moreOptions: undefined,
  onMoreSelect: undefined,
  includeDeleteInMore: true,
  translate: undefined,
})

function t(key: string): string {
  if (props.translate) return props.translate(key)
  const fallback: Record<string, string> = {
    'admin.crud.edit': 'Edit',
    'admin.crud.delete': 'Delete',
    'admin.crud.more': 'More',
    'admin.crud.confirmDelete': 'Are you sure to delete?',
  }
  return fallback[key] ?? key
}

/**
 * True when this row originates from a read-only source (file system,
 * embedded resource, code-defined, workspace, appsettings…). Detected
 * by inspecting `row.isReadOnly`. The dual-source modules
 * (Skills/Template/Feature/Workspace Agent/…) backfill this field at
 * the DTO layer so the front-end doesn't need module-specific logic.
 */
const readOnly = computed<boolean>(() => {
  const r = props.row as { isReadOnly?: boolean } | null
  return r?.isReadOnly === true
})

const hasMoreOptions = computed(() => props.moreOptions !== undefined)

const resolvedMoreOptions = computed<DropdownOption[]>(() => {
  if (!props.moreOptions) return []
  return typeof props.moreOptions === 'function'
    ? props.moreOptions(props.row)
    : props.moreOptions
})

/**
 * Final option list shown in the More▾ dropdown. The framework's
 * built-in `delete` entry is appended at the bottom (after a divider
 * separator if consumer options exist) so destructive ops stay visually
 * distinct from neutral ones. Suppressed when `showDelete=false` or
 * `includeDeleteInMore=false`.
 */
const dropdownOptions = computed<DropdownOption[]>(() => {
  const opts: DropdownOption[] = [...resolvedMoreOptions.value]
  if (props.showDelete && props.includeDeleteInMore) {
    if (opts.length > 0) {
      opts.push({ type: 'divider', key: 'd-delete' })
    }
    opts.push({
      key: 'delete',
      label: t('admin.crud.delete'),
      props: { style: 'color: var(--tnzi-color-error, #e54040)' },
    })
  }
  return opts
})

// useDialog is provider-dependent; cache a safe wrapper so calling it
// without a provider (e.g. unit-test mount that skips n-dialog-provider)
// doesn't crash — we fall back to window.confirm so the destructive
// confirm guarantee still holds in test environments.
function safeDialog(): { warning: (cfg: { title: string; content: string; positiveText: string; negativeText: string; onPositiveClick: () => void | Promise<void> }) => void } {
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

function handleEdit(): void {
  props.state.openEdit(props.row)
}

function handleView(): void {
  props.state.openView(props.row)
}

async function handleDelete(): Promise<void> {
  const id = props.state.rowKey(props.row)
  await props.state.handleDelete([id])
}

function onDropdownSelect(key: string): void {
  if (key === 'delete') {
    dialog.warning({
      title: t('admin.crud.delete'),
      content: props.confirmText ?? t('admin.crud.confirmDelete'),
      positiveText: t('admin.crud.confirm'),
      negativeText: t('admin.crud.cancel'),
      onPositiveClick: async () => {
        await handleDelete()
      },
    })
    return
  }
  props.onMoreSelect?.(key, props.row)
}
</script>

<style scoped>
.t-row-actions {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
}
</style>
