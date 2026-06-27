<template>
  <TCrudPage
    :state="crud"
    :all-columns="columns"
    :title="title"
    :translate="t"
    :form-modal-width="760"
    :row-actions="rowActions"
  >
    <template #form="{ formData, mode }">
      <TFormSchemaRenderer
        :schema="menuFormSchema"
        :model="(formData ?? {}) as Record<string, unknown>"
        :readonly="mode === 'view'"
        :translate="t"
        :field-renderers="fieldRenderers"
      />
    </template>
  </TCrudPage>
</template>

<script setup lang="ts">
import { computed, h, ref, onMounted } from 'vue'
import { NSelect } from 'naive-ui'
import { Icon } from '@iconify/vue'
import TCrudPage from '../../components/crud/TCrudPage.vue'
import TStatusBadge from '../../components/display/TStatusBadge.vue'
import TFormSchemaRenderer, { type FieldRenderContext } from '../_shared/form-schema'
import { useCrudPage } from '../../headless/useCrudPage'
import { editAction, deleteAction, type RowAction } from '../../headless/rowActions'
import { createSystemBridge } from '../../services/bridges/system-bridge'
import { useAdminClient } from '../../plugin/client'
import { translatePageKey } from '../_shared/translate'
import type { ColumnDef } from '../../headless/useColumnSettings'
import { menuFormSchema, type MenuRow } from './menu-config'

const title = 'title'
const bridge = createSystemBridge({ client: useAdminClient() })
const t = (key: string) => translatePageKey('system.menus', key)

const allMenus = ref<MenuRow[]>([])
const menuById = computed(() => {
  const m = new Map<string, MenuRow>()
  for (const x of allMenus.value) if (x.id) m.set(x.id, x)
  return m
})

async function loadAllMenus(): Promise<void> {
  try {
    // Re-use the existing fetch — backend GetMenus returns the full list so
    // a single high-pageSize call is cheap.
    const result = await bridge.menus.fetch({
      pageIndex: 1,
      pageSize: 500,
      searchText: '',
      filters: {},
      sortField: 'sortOrder',
      sortOrder: 'asc',
    })
    allMenus.value = result.items as MenuRow[]
  } catch {
    allMenus.value = []
  }
}

const editingId = computed(() => (crud.formModal.formData.value as MenuRow | null)?.id)

const parentOptions = computed(() => {
  /** Walk parent chain with visited-set so cyclic data terminates. */
  const depthOf = (id: string | undefined): number => {
    if (!id) return 0
    const visited = new Set<string>()
    let d = 0
    let cur = menuById.value.get(id)
    while (cur?.parentId) {
      if (visited.has(cur.parentId)) break
      visited.add(cur.parentId)
      d += 1
      cur = menuById.value.get(cur.parentId)
      if (d > 32) break
    }
    return d
  }
  return allMenus.value
    .filter((m) => m.id && m.id !== editingId.value)
    .map((m) => ({
      label: `${'  '.repeat(depthOf(m.id))}${m.name ?? '(unnamed)'}`,
      value: m.id!,
    }))
})

// MenuType enum: 0=Directory, 1=Menu, 2=Button.
const typeOptions = [
  { label: 'Directory', value: 0 },
  { label: 'Menu', value: 1 },
  { label: 'Button', value: 2 },
]
function typeLabel(v?: number): string {
  return typeOptions.find((o) => o.value === v)?.label ?? '—'
}

const columns: ColumnDef<MenuRow>[] = [
  { key: 'name', title: 'columns.name', minWidth: 150 },
  {
    key: 'icon',
    title: 'columns.icon',
    width: 80,
    render: (row) =>
      row.icon
        ? h('span', { class: 'inline-flex items-center gap-4px' }, [
            h(Icon, { icon: row.icon, width: 16, height: 16 }),
          ])
        : h('span', { class: 'text-muted' }, '—'),
  },
  {
    key: 'parentName',
    title: 'columns.parentName',
    minWidth: 140,
    render: (row) => {
      if (!row.parentId) return h('span', { class: 'text-muted' }, '—')
      const p = menuById.value.get(row.parentId)
      return p?.name ?? row.parentId
    },
  },
  { key: 'path', title: 'columns.path', minWidth: 160 },
  { key: 'permission', title: 'columns.permission', minWidth: 150 },
  {
    key: 'type',
    title: 'columns.type',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.type ?? 0,
        type: row.type === 0 ? 'info' : row.type === 1 ? 'success' : 'warning',
        label: typeLabel(row.type),
      }),
  },
  { key: 'sortOrder', title: 'columns.sortOrder', width: 80 },
  {
    key: 'isHidden',
    title: 'columns.isHidden',
    width: 110,
    render: (row) =>
      h(TStatusBadge, {
        value: row.isHidden ?? false,
        mapping: {
          true: { type: 'warning', labelKey: 'admin.modules.system.menus.status.hidden' },
          false: { type: 'success', labelKey: 'admin.modules.system.menus.status.visible' },
        },
      }),
  },
]

const crud = useCrudPage<MenuRow, string>({
  pageId: 'system.menus',
  columns,
  rowKey: (r) => String(r.id ?? ''),
  fetchData: (query) => bridge.menus.fetch(query) as never,
  createData: async (data) => {
    const created = await bridge.menus.create(data as never)
    await loadAllMenus()
    return created as MenuRow
  },
  updateData: async (id, data) => {
    const updated = await bridge.menus.update(String(id), data as never)
    await loadAllMenus()
    return updated as MenuRow
  },
  deleteData: async (ids) => {
    await bridge.menus.delete(ids.map(String))
    await loadAllMenus()
  },
})

const rowActions: RowAction<MenuRow>[] = [editAction(crud), deleteAction(crud)]

crud.refresh().catch(() => undefined)

// Custom field renderer for the schema's `menu-parent` field: a filterable +
// clearable parent picker built from the runtime menu tree (the static schema
// `select` can't carry the dynamic, indented parent options).
const fieldRenderers = {
  'menu-parent': (ctx: FieldRenderContext) =>
    h(NSelect, {
      value: (ctx.value as string | null) ?? null,
      options: parentOptions.value,
      placeholder: t('form.parentIdPlaceholder'),
      clearable: true,
      filterable: true,
      disabled: ctx.readonly,
      'onUpdate:value': (v: string | null) => ctx.onUpdate(v ?? undefined),
    }),
}

onMounted(() => {
  void loadAllMenus()
})
</script>
